using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Contracts.Lookups;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modularity;

namespace soft.square.net.WebApi.Lookups;

/// <summary>
/// <see cref="ILookupClient"/> for modules: one call whatever the topology.
/// <list type="bullet">
/// <item>The owner is loaded and active on this replica: in process, in a scope of the owner layered over the current
/// request's host scope (same user and tenant), under a lease so the owner cannot unload mid-call.</item>
/// <item>Otherwise, over HTTP to <c>Lookups:RemoteBaseUrl</c> (an internal address that reaches every pool, never the
/// public gateway from outside), with the caller's Authorization and tenant headers. Without that setting the lookup
/// is reported unavailable.</item>
/// </list>
/// </summary>
internal sealed class LookupClient(
    ModuleRuntime runtime,
    LookupRegistry registry,
    ModuleEndpointDataSource endpoints,
    IOptions<ModuleRuntimeOptions> options,
    IHttpContextAccessor accessor,
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    IConfiguration configuration,
    IServiceProvider services) : ILookupClient
{
    public const string HttpClientName = "lookups";

    private string RouteBase => "/" + options.Value.RoutePrefix.Trim('/');

    public async Task<IReadOnlyList<LookupDescriptor>> CatalogAsync(CancellationToken ct = default)
    {
        var user = accessor.HttpContext?.User;
        var result = new List<LookupDescriptor>();
        foreach (var status in await runtime.ListAsync(ct))
        {
            if (status.Module.DesiredState != ModuleState.Active) continue;
            foreach (var lookup in status.Module.Lookups)
            {
                if (user is not null && !await LookupAccess.AllowedAsync(services, user, lookup.Permission)) continue;
                result.Add(lookup with { Url = $"{RouteBase}/{status.Module.RoutePrefix}/lookups/{lookup.Name}" });
            }
        }
        return result.OrderBy(l => l.Key, StringComparer.Ordinal).ToList();
    }

    public Task<LookupPage> SearchAsync(string key, LookupQuery query, CancellationToken ct = default) =>
        RunAsync(key,
            (definition, scope) => definition.SearchAsync(scope, query, ct),
            (http, url) => http.PostAsJsonAsync($"{url}/query", query, ct),
            async response => (await response.Content.ReadFromJsonAsync<LookupPage>(ct))!);

    public Task<IReadOnlyList<LookupItem>> ResolveAsync(string key, IReadOnlyCollection<string> keys, IReadOnlyList<LookupFilter>? filters = null, CancellationToken ct = default) =>
        keys.Count == 0
            ? Task.FromResult<IReadOnlyList<LookupItem>>([])
            : RunAsync(key,
                (definition, scope) => definition.ResolveAsync(scope, keys, filters, ct),
                (http, url) => http.PostAsJsonAsync($"{url}/keys", new LookupKeysRequest([.. keys], filters), ct),
                async response => (IReadOnlyList<LookupItem>)(await response.Content.ReadFromJsonAsync<List<LookupItem>>(ct))!);

    private async Task<TResult> RunAsync<TResult>(
        string key,
        Func<ILookupDefinition, IServiceProvider, Task<TResult>> local,
        Func<HttpClient, string, Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, Task<TResult>> read)
    {
        var (prefix, name) = Parse(key);

        using (var lease = registry.Acquire(prefix))
        {
            if (lease is not null && (endpoints.IsRouted(lease.ModuleId) || options.Value.Role == ModuleRole.Worker))
            {
                var set = (LookupSet)lease.Services.GetService(typeof(LookupSet))!;
                if (!set.Lookups.TryGetValue(name, out var definition)) throw new ArgumentException($"Lookup '{key}' does not exist.");

                var http = accessor.HttpContext;
                // Server code without a request (a background job) is trusted; a request is checked as its user.
                if (http is not null && !await LookupAccess.AllowedAsync(services, http.User, definition.RequiredPermission))
                    throw new UnauthorizedAccessException($"You need the permission '{definition.RequiredPermission}' to use lookup '{key}'.");

                IServiceScope? ownHostScope = null;
                var hostScope = http?.RequestServices is { } requestServices
                    ? requestServices is ModuleScope caller ? caller.Host : requestServices // never another module's scope
                    : (ownHostScope = scopes.CreateScope()).ServiceProvider;
                try
                {
                    await using var scope = ((ModuleScope)lease.Services).CreateScopeOver(hostScope);
                    return await local(definition, scope);
                }
                finally { ownHostScope?.Dispose(); }
            }
        }

        return await RemoteAsync(key, prefix, name, send, read);
    }

    private async Task<TResult> RemoteAsync<TResult>(string key, string prefix, string name,
        Func<HttpClient, string, Task<HttpResponseMessage>> send, Func<HttpResponseMessage, Task<TResult>> read)
    {
        var baseUrl = configuration["Lookups:RemoteBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new LookupUnavailableException(key, "its module is not active on this replica and Lookups:RemoteBaseUrl is not set");

        var http = httpClients.CreateClient(HttpClientName);
        http.BaseAddress ??= new Uri(baseUrl.TrimEnd('/') + "/");
        var request = accessor.HttpContext?.Request;
        if (request?.Headers.Authorization.ToString() is { Length: > 0 } authorization)
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);
        var tenant = request?.Headers[TenancyNames.Header].ToString() is { Length: > 0 } header
            ? header
            : accessor.HttpContext?.RequestServices.GetService<ITenantContext>()?.TenantId;
        if (tenant is not null) http.DefaultRequestHeaders.TryAddWithoutValidation(TenancyNames.Header, tenant);

        HttpResponseMessage response;
        try
        {
            response = await send(http, $"{RouteBase.TrimStart('/')}/{prefix}/lookups/{name}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new LookupUnavailableException(key, e.Message);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound: throw new LookupUnavailableException(key, "no pool serves it");
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden: throw new UnauthorizedAccessException($"Not allowed to use lookup '{key}'.");
                case HttpStatusCode.BadRequest: throw new ArgumentException(await response.Content.ReadAsStringAsync());
            }
            if (!response.IsSuccessStatusCode) throw new LookupUnavailableException(key, $"HTTP {(int)response.StatusCode}");
            return await read(response);
        }
    }

    private static (string Prefix, string Name) Parse(string key)
    {
        var dot = key.LastIndexOf('.');
        if (dot <= 0 || dot == key.Length - 1) throw new ArgumentException($"'{key}' is not a lookup key ({{prefix}}.{{name}}).");
        return (key[..dot], key[(dot + 1)..]);
    }
}

public static class LookupHostExtensions
{
    public static IServiceCollection AddPlatformLookups(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient(LookupClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<LookupRegistry>();
        services.AddSingleton<IModuleLifecycleObserver>(sp => sp.GetRequiredService<LookupRegistry>());
        services.AddScoped<ILookupClient, LookupClient>();
        return services;
    }
}

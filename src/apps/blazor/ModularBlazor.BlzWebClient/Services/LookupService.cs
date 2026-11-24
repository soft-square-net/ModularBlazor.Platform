using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using soft.square.net.ModularBlazor.Abstractions;
using soft.square.net.ModularBlazor.Abstractions.Lookups;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services;

/// <summary>
/// <see cref="ILookupService"/> over the gateway ("WebApi" client: the user's token and language). The catalog is read
/// once per session; the first page of each (lookup, parent) is kept for a minute, because cascades and pickers ask
/// for it again and again; searches with text or filters always go to the server.
/// </summary>
internal sealed class LookupService(IHttpClientFactory clients) : ILookupService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan FirstPageLifetime = TimeSpan.FromMinutes(1);

    private Task<IReadOnlyList<LookupDescriptor>>? _catalog;
    private readonly Dictionary<string, (DateTimeOffset At, LookupPage Page)> _firstPages = new(StringComparer.Ordinal);

    private HttpClient Http => clients.CreateClient(HttpClientNames.WebApi);

    public Task<IReadOnlyList<LookupDescriptor>> CatalogAsync(bool refresh = false, CancellationToken ct = default)
    {
        if (refresh || _catalog is null || _catalog.IsFaulted || _catalog.IsCanceled)
        {
            _catalog = LoadCatalogAsync(ct);
            _firstPages.Clear();
        }
        return _catalog;
    }

    private async Task<IReadOnlyList<LookupDescriptor>> LoadCatalogAsync(CancellationToken ct) =>
        await Http.GetFromJsonAsync<List<LookupDescriptor>>("api/lookups", Json, ct) ?? [];

    public async Task<LookupDescriptor?> FindAsync(string key, CancellationToken ct = default) =>
        (await CatalogAsync(ct: ct)).FirstOrDefault(l => l.Key == key);

    public async Task<LookupPage> SearchAsync(string key, LookupQuery query, CancellationToken ct = default)
    {
        var url = await UrlAsync(key, ct);
        var firstPage = string.IsNullOrWhiteSpace(query.Q) && query.Skip == 0 && query.Filters is not { Count: > 0 } && !query.IncludeTotal;
        var cacheKey = $"{key}|{query.Parent}|{query.Take}|{string.Join(',', query.Fields ?? [])}";
        if (firstPage && _firstPages.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.At < FirstPageLifetime)
            return cached.Page;

        HttpResponseMessage response;
        if (query.Filters is { Count: > 0 } || query.IncludeTotal)
        {
            response = await Http.PostAsJsonAsync($"{url}/query", query, Json, ct);
        }
        else
        {
            var parts = new List<string> { $"skip={query.Skip}", $"take={query.Take}" };
            if (!string.IsNullOrWhiteSpace(query.Q)) parts.Add("q=" + Uri.EscapeDataString(query.Q));
            if (query.Parent is not null) parts.Add("parent=" + Uri.EscapeDataString(query.Parent));
            if (query.Fields is { Count: > 0 } fields) parts.Add("fields=" + Uri.EscapeDataString(string.Join(',', fields)));
            response = await Http.GetAsync($"{url}?{string.Join('&', parts)}", ct);
        }
        var page = await ReadAsync<LookupPage>(response, key, ct);
        if (firstPage) _firstPages[cacheKey] = (DateTimeOffset.UtcNow, page);
        return page;
    }

    public async Task<IReadOnlyList<LookupItem>> ResolveAsync(string key, IReadOnlyCollection<string> keys, IReadOnlyList<LookupFilter>? filters = null, CancellationToken ct = default)
    {
        if (keys.Count == 0) return [];
        var url = await UrlAsync(key, ct);
        var response = await Http.PostAsJsonAsync($"{url}/keys", new { keys, filters }, Json, ct);
        return await ReadAsync<List<LookupItem>>(response, key, ct);
    }

    /// <summary>The catalog's URL, or the convention (/api/{prefix}/lookups/{name}) for a lookup it does not list.</summary>
    private async Task<string> UrlAsync(string key, CancellationToken ct)
    {
        LookupDescriptor? descriptor = null;
        try { descriptor = await FindAsync(key, ct); }
        catch (HttpRequestException) { } // the catalog is down: the convention still reaches the owner
        if (descriptor is not null) return descriptor.Url.TrimStart('/');
        var dot = key.LastIndexOf('.');
        if (dot <= 0) throw new ArgumentException($"'{key}' is not a lookup key ({{prefix}}.{{name}}).");
        return $"api/{key[..dot]}/lookups/{key[(dot + 1)..]}";
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, string key, CancellationToken ct)
    {
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
                throw new ArgumentException(problem.TryGetProperty("detail", out var detail) ? detail.GetString() : $"Invalid request to lookup '{key}'.");
            }
            if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException($"The list '{key}' is not available.");
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }
    }
}

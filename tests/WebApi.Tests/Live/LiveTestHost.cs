using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Live;
using soft.square.net.WebApi.Security;
using soft.square.net.WebApi.Tenancy;

namespace soft.square.net.WebApi.Tests.Live;

/// <summary>
/// One WebApi replica with the parts the live hub needs (tenancy from configuration, security with dev JWTs, live),
/// on Kestrel with a free port. With a broker, it is the real RabbitMQ path (live, grant and tenant fanouts).
/// </summary>
internal sealed class LiveTestHost : IAsyncDisposable
{
    public const string Issuer = "live-tests";
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("live-tests-signing-key-0123456789-abcdef");

    private readonly WebApplication _app;

    private LiveTestHost(WebApplication app, string url)
    {
        _app = app;
        Url = url;
    }

    public string Url { get; }

    public IServiceProvider Services => _app.Services;

    /// <param name="url">A fixed address (a replica that comes back where it was); a free port by default.</param>
    public static async Task<LiveTestHost> StartAsync(string? broker = null, string? url = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseUrls(url ?? "http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        if (Environment.GetEnvironmentVariable("LIVE_TEST_LOGS") is not null) builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Information);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:broker"] = broker,
            ["Tenancy:Tenants:0:Id"] = "acme",
            ["Tenancy:Tenants:1:Id"] = "globex",
            ["Authentication:Schemes:Bearer:ValidIssuer"] = Issuer,
            ["Authentication:Schemes:Bearer:ValidAudiences:0"] = Issuer,
            ["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = Issuer,
            ["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = Convert.ToBase64String(Key),
        });
        if (broker is not null) builder.AddRabbitMQClient("broker");
        builder.AddPlatformTenancy("broker");
        builder.AddPlatformSecurity("broker");
        builder.AddPlatformLive("broker");
        builder.Services.AddSingleton<ILiveTopicCatalog>(new FakeTopicCatalog());

        var app = builder.Build();
        app.UseRouting();
        app.UsePlatformSecurity();
        app.MapPlatformLive();
        await app.StartAsync();
        await app.Services.GetRequiredService<TenantDirectory>().Ready;

        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        return new LiveTestHost(app, address);
    }

    public static string Token(string userId, string tenant, params string[] roles)
    {
        var claims = new Dictionary<string, object>
        {
            [PlatformClaims.Subject] = userId,
            [PlatformClaims.Tenant] = tenant,
        };
        if (roles.Length > 0) claims[PlatformClaims.Role] = roles;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Issuer,
            Claims = claims,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Key), SecurityAlgorithms.HmacSha256)
        });
    }

    /// <summary>A connected hub client (WebSockets, negotiation skipped, token in access_token, like the Blazor app).</summary>
    public async Task<LiveTestClient> ConnectAsync(string userId, string tenant = "acme")
    {
        var token = Token(userId, tenant);
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(Url), LiveHubMethods.Path), options =>
            {
                options.SkipNegotiation = true;
                options.Transports = HttpTransportType.WebSockets;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        var client = new LiveTestClient(hub);
        await hub.StartAsync();
        return client;
    }

    public async Task GrantAsync(string tenant, string userId, string permission, string scope)
    {
        await using var services = Services.CreateAsyncScope();
        services.ServiceProvider.GetRequiredService<ITenantScopeInitializer>().Use(Services.GetRequiredService<ITenantDirectory>().Find(tenant)!, "test");
        await services.ServiceProvider.GetRequiredService<IGrantStore>().AddAsync(new NewGrant(userId, permission, scope));
    }

    public async Task RevokeAllAsync(string tenant, string userId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantScopeInitializer>().Use(Services.GetRequiredService<ITenantDirectory>().Find(tenant)!, "test");
        var store = scope.ServiceProvider.GetRequiredService<IGrantStore>();
        foreach (var grant in await store.ListAsync(userId)) await store.RevokeAsync(grant.Id);
    }

    /// <summary>Publishes like a module does: <see cref="ILiveNotifier"/> in a tenant scope.</summary>
    public async Task PublishAsync(string tenant, Func<ILiveNotifier, Task> publish)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantScopeInitializer>().Use(Services.GetRequiredService<ITenantDirectory>().Find(tenant)!, "test");
        await publish(scope.ServiceProvider.GetRequiredService<ILiveNotifier>());
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private sealed class FakeTopicCatalog : ILiveTopicCatalog
    {
        private static readonly Dictionary<string, LiveTopic[]> Topics = new()
        {
            ["content"] = [new LiveTopic("post", "content.view"), new LiveTopic("draft", "content.edit") { AcceptClientScopes = false }]
        };

        public Task<LiveTopic?> FindAsync(string moduleId, string kind, CancellationToken ct) =>
            Task.FromResult(Topics.GetValueOrDefault(moduleId)?.FirstOrDefault(t => t.Kind == kind));
    }
}

internal sealed class LiveTestClient : IAsyncDisposable
{
    private readonly HubConnection _hub;

    public LiveTestClient(HubConnection hub)
    {
        _hub = hub;
        hub.On<LiveEnvelope>(LiveHubMethods.Message, m => Messages.Writer.TryWrite(m));
        hub.On<string>(LiveHubMethods.Revoked, t => Revoked.Writer.TryWrite(t));
    }

    public Channel<LiveEnvelope> Messages { get; } = Channel.CreateUnbounded<LiveEnvelope>();

    public Channel<string> Revoked { get; } = Channel.CreateUnbounded<string>();

    public Task<LiveSubscribeResult> SubscribeAsync(string topic, params string[] scopes) =>
        _hub.InvokeAsync<LiveSubscribeResult>(LiveHubMethods.Subscribe, topic, scopes);

    public Task UnsubscribeAsync(string topic) => _hub.InvokeAsync(LiveHubMethods.Unsubscribe, topic);

    public async Task<LiveEnvelope> NextAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        return await Messages.Reader.ReadAsync(cts.Token);
    }

    /// <summary>Nothing arrives within <paramref name="wait"/>.</summary>
    public async Task<bool> QuietAsync(TimeSpan? wait = null)
    {
        using var cts = new CancellationTokenSource(wait ?? TimeSpan.FromMilliseconds(500));
        try { await Messages.Reader.WaitToReadAsync(cts.Token); return false; }
        catch (OperationCanceledException) { return true; }
    }

    public ValueTask DisposeAsync() => _hub.DisposeAsync();
}

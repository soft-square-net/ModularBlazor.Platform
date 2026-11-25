using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;

namespace soft.square.net.WebApi.Live;

/// <summary>
/// The platform's live hub (<see cref="LiveHubMethods.Path"/>), a host type. A connection belongs to one user in one
/// tenant (the tenant the connect request resolved: the token's <c>tenant</c> claim, or the header or host that agrees
/// with it). Clients join topics with <c>Subscribe</c>; each topic is the SignalR group <c>t:{tenant}:{topic}</c>, so
/// tenants never share a group. Every replica sends the messages it receives over the broker to its own connections.
/// </summary>
internal sealed class LiveHub(LiveConnections connections, LiveTopicAuthorizer authorizer, ITenantDirectory directory, ILogger<LiveHub> log) : Hub
{
    /// <summary>More topics on one connection than any page needs: refused to keep the group tables small.</summary>
    public const int MaxTopicsPerConnection = 256;

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        var userId = user?.FindFirst(PlatformClaims.Subject)?.Value;
        var tenant = Context.GetHttpContext()?.RequestServices.GetService<ITenantContext>()?.Tenant
                     ?? (user?.FindFirst(TenancyNames.Claim)?.Value is { Length: > 0 } claimed ? directory.Find(claimed) : null);
        if (user is null || userId is null || tenant is not { IsActive: true })
        {
            log.LogInformation("Live connection {Connection} refused: no user or no active tenant", Context.ConnectionId);
            Context.Abort();
            return;
        }

        connections.Add(new LiveConnection(Context.ConnectionId, tenant, userId, user));
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId); // SignalR drops the connection's groups itself
        return base.OnDisconnectedAsync(exception);
    }

    /// <param name="topic">See <see cref="LiveTopics"/>; never names the tenant.</param>
    /// <param name="scopes">The resource's scopes as the client knows them (its press, category, unit ancestors).</param>
    public async Task<LiveSubscribeResult> Subscribe(string topic, string[]? scopes)
    {
        if (connections.Get(Context.ConnectionId) is not { } connection) return new(false, LiveErrors.Forbidden);
        if (!connection.Topics.ContainsKey(topic) && connection.Topics.Count >= MaxTopicsPerConnection)
            return new(false, LiveErrors.TooMany);

        var clientScopes = (scopes ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Take(32).ToArray();
        var result = await authorizer.AuthorizeAsync(connection.Tenant, connection.User, topic, clientScopes, Context.ConnectionAborted);
        if (!result.Ok)
        {
            log.LogDebug("Live subscription of {User} to {Topic} refused: {Error}", connection.UserId, topic, result.Error);
            return result;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.Of(connection.Tenant.Id, topic), Context.ConnectionAborted);
        connection.Topics[topic] = clientScopes;
        return result;
    }

    public async Task Unsubscribe(string topic)
    {
        if (connections.Get(Context.ConnectionId) is not { } connection || !connection.Topics.TryRemove(topic, out _)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveGroups.Of(connection.Tenant.Id, topic));
    }
}

internal static class LiveErrors
{
    public const string Forbidden = "forbidden";
    public const string UnknownTopic = "unknown-topic";
    public const string InvalidTopic = "invalid-topic";
    public const string TooMany = "too-many-topics";
}

internal static class LiveGroups
{
    public static string Of(string tenantId, string topic) => $"t:{tenantId.ToLowerInvariant()}:{topic}";
}

/// <summary>One open hub connection: who, which tenant, and the topics it joined with the scopes it sent.</summary>
internal sealed class LiveConnection(string id, TenantInfo tenant, string userId, ClaimsPrincipal user)
{
    public string Id { get; } = id;
    public TenantInfo Tenant { get; } = tenant;
    public string UserId { get; } = userId;
    public ClaimsPrincipal User { get; } = user;
    public ConcurrentDictionary<string, string[]> Topics { get; } = new(StringComparer.Ordinal);
}

/// <summary>The hub connections of this replica (each replica only knows its own).</summary>
internal sealed class LiveConnections
{
    private readonly ConcurrentDictionary<string, LiveConnection> _connections = new(StringComparer.Ordinal);

    public void Add(LiveConnection connection) => _connections[connection.Id] = connection;

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    public LiveConnection? Get(string connectionId) => _connections.GetValueOrDefault(connectionId);

    public int Count => _connections.Count;

    /// <summary>The connections of these users in the tenant (every user of the tenant when null).</summary>
    public IReadOnlyList<LiveConnection> Of(string tenantId, IReadOnlyCollection<string>? userIds) =>
        _connections.Values
            .Where(c => string.Equals(c.Tenant.Id, tenantId, StringComparison.OrdinalIgnoreCase)
                        && (userIds is null || userIds.Contains(c.UserId)))
            .ToArray();
}

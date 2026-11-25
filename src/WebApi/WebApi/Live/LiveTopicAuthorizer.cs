using System.Security.Claims;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modularity;
using soft.square.net.WebApi.Security;

namespace soft.square.net.WebApi.Live;

/// <summary>The live topic kinds modules declare (<see cref="Contracts.ModuleManifest.LiveTopics"/>), as plain data.</summary>
internal interface ILiveTopicCatalog
{
    Task<LiveTopic?> FindAsync(string moduleId, string kind, CancellationToken ct);
}

/// <summary>
/// From the module catalog: the topics of an active module, read from its record (written at install from the
/// manifest), so no module is loaded or held. Cached briefly: a subscription burst after a deploy reads the catalog once.
/// </summary>
internal sealed class ModuleLiveTopicCatalog(ModuleRuntime runtime, TimeProvider clock) : ILiveTopicCatalog
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, IReadOnlyList<LiveTopic>> _topics = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _expires;

    public async Task<LiveTopic?> FindAsync(string moduleId, string kind, CancellationToken ct)
    {
        if (clock.GetUtcNow() >= _expires)
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (clock.GetUtcNow() >= _expires)
                {
                    _topics = (await runtime.ListAsync(ct))
                        .Where(s => s.Module.DesiredState == ModuleState.Active && s.Module.LiveTopics.Count > 0)
                        .ToDictionary(s => s.Module.Id, s => (IReadOnlyList<LiveTopic>)s.Module.LiveTopics.ToArray(), StringComparer.OrdinalIgnoreCase);
                    _expires = clock.GetUtcNow() + Lifetime;
                }
            }
            finally { _gate.Release(); }
        }

        return _topics.TryGetValue(moduleId, out var topics)
            ? topics.FirstOrDefault(t => string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase))
            : null;
    }
}

/// <summary>
/// Decides whether a user may receive a topic, through <see cref="IAccessScopes"/> in a scope of the connection's tenant
/// (the same grants, time windows and platform roles as every endpoint):
/// <list type="bullet">
/// <item><c>user:{id}</c>: the caller's own id only;</item>
/// <item><c>unit:{id}</c>: any permission the caller holds for the scopes <c>unit:{id}</c> plus the scopes the client sent
/// (the unit's ancestors);</item>
/// <item><c>{module}:{kind}:{key}</c>: the <see cref="LiveTopic.Permission"/> the active module declares for the kind, for
/// <c>{kind}:{key}</c> plus the client's scopes when the kind accepts them.</item>
/// </list>
/// </summary>
internal sealed class LiveTopicAuthorizer(IServiceScopeFactory scopes, ILiveTopicCatalog catalog)
{
    public const int MaxTopicLength = 256;

    public async Task<LiveSubscribeResult> AuthorizeAsync(TenantInfo tenant, ClaimsPrincipal user, string topic,
        IReadOnlyCollection<string> clientScopes, CancellationToken ct)
    {
        if (!TryParse(topic, out var kind, out var rest)) return new(false, LiveErrors.InvalidTopic);

        if (kind == LiveTopics.UserKind)
            return string.Equals(rest, user.FindFirst(PlatformClaims.Subject)?.Value, StringComparison.Ordinal)
                ? new(true)
                : new(false, LiveErrors.Forbidden);

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantScopeInitializer>().Use(tenant, "live");
        scope.ServiceProvider.GetRequiredService<ScopeUser>().Principal = user;
        var access = scope.ServiceProvider.GetRequiredService<IAccessScopes>();

        if (kind == LiveTopics.UnitKind)
        {
            if (rest.Contains(':')) return new(false, LiveErrors.InvalidTopic);
            string[] unitScopes = [AccessScope.Of(LiveTopics.UnitKind, rest), .. clientScopes];
            foreach (var permission in await access.PermissionsAsync(ct))
                if (await access.HasPermissionAsync(permission, unitScopes, ct)) return new(true);
            return new(false, LiveErrors.Forbidden);
        }

        // {module}:{kind}:{key}
        var split = rest.IndexOf(':');
        if (split <= 0 || split == rest.Length - 1) return new(false, LiveErrors.InvalidTopic);
        var (topicKind, key) = (rest[..split], rest[(split + 1)..]);
        if (await catalog.FindAsync(kind, topicKind, ct) is not { } declared) return new(false, LiveErrors.UnknownTopic);

        string[] resourceScopes = declared.AcceptClientScopes
            ? [AccessScope.Of(topicKind, key), .. clientScopes]
            : [AccessScope.Of(topicKind, key)];
        return await access.HasPermissionAsync(declared.Permission, resourceScopes, ct)
            ? new(true)
            : new(false, LiveErrors.Forbidden);
    }

    /// <summary>The first segment (lowercase: user, unit or a module id) and the rest; printable, bounded, no blanks.</summary>
    internal static bool TryParse(string? topic, out string kind, out string rest)
    {
        kind = rest = "";
        if (string.IsNullOrEmpty(topic) || topic.Length > MaxTopicLength || topic.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return false;
        var colon = topic.IndexOf(':');
        if (colon <= 0 || colon == topic.Length - 1) return false;
        kind = topic[..colon];
        rest = topic[(colon + 1)..];
        return kind.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-');
    }
}

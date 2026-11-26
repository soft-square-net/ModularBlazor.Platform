using System.Collections.Concurrent;
using System.Security.Claims;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modularity;
using soft.square.net.WebApi.Security;

namespace soft.square.net.WebApi.Live;

/// <summary>A live topic kind and the active module that declares it.</summary>
internal sealed record DeclaredLiveTopic(string ModuleId, LiveTopic Topic);

/// <summary>The live topic kinds modules declare (<see cref="Contracts.ModuleManifest.LiveTopics"/>), as plain data.</summary>
internal interface ILiveTopicCatalog
{
    /// <summary>The kind <paramref name="kind"/> of the module <paramref name="moduleId"/> (a <c>{module}:{kind}:{key}</c> topic).</summary>
    Task<DeclaredLiveTopic?> FindAsync(string moduleId, string kind, CancellationToken ct);

    /// <summary>The one active module's declaration of <see cref="LiveTopics.UnitKind"/>; null when none, or more than one, declares it.</summary>
    Task<DeclaredLiveTopic?> FindUnitAsync(CancellationToken ct);
}

/// <summary>
/// From the module catalog: the topics of active modules, read from their records (written at install from the
/// manifest), so no module is loaded or held. Cached briefly: a subscription burst after a deploy reads the catalog once.
/// </summary>
internal sealed class ModuleLiveTopicCatalog(ModuleRuntime runtime, TimeProvider clock, ILogger<ModuleLiveTopicCatalog> log) : ILiveTopicCatalog
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, IReadOnlyList<LiveTopic>> _topics = new(StringComparer.OrdinalIgnoreCase);
    private DeclaredLiveTopic? _unit;
    private DateTimeOffset _expires;

    public async Task<DeclaredLiveTopic?> FindAsync(string moduleId, string kind, CancellationToken ct)
    {
        await RefreshAsync(ct);
        return _topics.TryGetValue(moduleId, out var topics) && topics.FirstOrDefault(t => Same(t.Kind, kind)) is { } topic
            ? new DeclaredLiveTopic(moduleId, topic)
            : null;
    }

    /// <summary>
    /// A module's topics, one per kind. Install refuses a manifest that declares a kind twice; a record written before
    /// that check (or edited by hand) that still does gets none of that kind: refused rather than guessed.
    /// </summary>
    internal static IReadOnlyList<LiveTopic> OnePerKind(string moduleId, IEnumerable<LiveTopic> topics, ILogger log)
    {
        var kinds = topics.GroupBy(t => t.Kind, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var duplicate in kinds.Where(g => g.Count() > 1))
            log.LogWarning("Module {Module} declares the live topic kind '{Kind}' more than once; its topics are refused", moduleId, duplicate.Key);
        return kinds.Where(g => g.Count() == 1).Select(g => g.Single()).ToArray();
    }

    public async Task<DeclaredLiveTopic?> FindUnitAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
        return _unit;
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (clock.GetUtcNow() < _expires) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (clock.GetUtcNow() < _expires) return;
            _topics = (await runtime.ListAsync(ct))
                .Where(s => s.Module.DesiredState == ModuleState.Active && s.Module.LiveTopics.Count > 0)
                .ToDictionary(s => s.Module.Id, s => OnePerKind(s.Module.Id, s.Module.LiveTopics, log), StringComparer.OrdinalIgnoreCase);
            var units = _topics.SelectMany(m => m.Value.Where(t => Same(t.Kind, LiveTopics.UnitKind)).Select(t => new DeclaredLiveTopic(m.Key, t))).ToArray();
            if (units.Length > 1)
                log.LogWarning("Modules {Modules} all declare the live topic kind 'unit'; unit topics are refused until only one does",
                    string.Join(", ", units.Select(u => u.ModuleId)));
            _unit = units.Length == 1 ? units[0] : null;
            _expires = clock.GetUtcNow() + Lifetime;
        }
        finally { _gate.Release(); }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Asks a module for a topic resource's scopes (its <see cref="ILiveScopeResolver"/>).</summary>
internal interface ILiveScopeSource
{
    /// <returns>The module's scopes for the resource; empty when the module is not loaded here or has no resolver.</returns>
    Task<IReadOnlyList<string>> ScopesAsync(string moduleId, TenantInfo tenant, string kind, string key, CancellationToken ct);
}

/// <summary>
/// Follows the modules loaded on this replica (like the tenant initializer) and resolves a module's
/// <see cref="ILiveScopeResolver"/> per call, from its own container, in a tenant scope. Only strings come back: no
/// module object outlives the call, and a module being released is forgotten before its container is disposed.
/// </summary>
internal sealed class ModuleLiveScopeSource(ILogger<ModuleLiveScopeSource> log) : ILiveScopeSource, IModuleLifecycleObserver
{
    private readonly ConcurrentDictionary<string, IServiceProvider> _modules = new(StringComparer.OrdinalIgnoreCase);

    public void Loaded(string moduleId, IServiceProvider moduleServices) => _modules[moduleId] = moduleServices;

    public Task ReleasingAsync(string moduleId, IServiceProvider moduleServices)
    {
        _modules.TryRemove(new KeyValuePair<string, IServiceProvider>(moduleId, moduleServices));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<string>> ScopesAsync(string moduleId, TenantInfo tenant, string kind, string key, CancellationToken ct)
    {
        if (!_modules.TryGetValue(moduleId, out var services)) return [];
        try
        {
            await using var scope = services.CreateTenantScope(tenant);
            if (scope.ServiceProvider.GetService<ILiveScopeResolver>() is not { } resolver) return [];
            return [.. await resolver.ScopesAsync(kind, key, ct)];
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Fail closed: without the resource's scopes only its exact scope counts.
            log.LogWarning(e, "The live scope resolver of module {Module} failed for {Kind}:{Key}", moduleId, kind, key);
            return [];
        }
    }
}

/// <summary>
/// Decides whether a user may receive a topic, through <see cref="IAccessScopes"/> in a scope of the connection's tenant
/// (the same grants, time windows and platform roles as every endpoint). Only server-side scopes count: the topic's own
/// and those its module's <see cref="ILiveScopeResolver"/> gives.
/// <list type="bullet">
/// <item><c>user:{id}</c>: the caller's own id only;</item>
/// <item><c>unit:{id}</c>: the permission of the <c>unit</c> kind declared by the one active module providing it, for
/// <c>unit:{id}</c> or its ancestors; refused when no module provides it;</item>
/// <item><c>{module}:{kind}:{key}</c>: the permission the active module declares for the kind, for <c>{kind}:{key}</c>
/// or the resource's other scopes.</item>
/// </list>
/// </summary>
internal sealed class LiveTopicAuthorizer(IServiceScopeFactory scopes, ILiveTopicCatalog catalog, ILiveScopeSource resourceScopes)
{
    public const int MaxTopicLength = 256;

    public async Task<LiveSubscribeResult> AuthorizeAsync(TenantInfo tenant, ClaimsPrincipal user, string topic, CancellationToken ct)
    {
        if (!TryParse(topic, out var first, out var rest)) return new(false, LiveErrors.InvalidTopic);

        if (first == LiveTopics.UserKind)
            return string.Equals(rest, user.FindFirst(PlatformClaims.Subject)?.Value, StringComparison.Ordinal)
                ? new(true)
                : new(false, LiveErrors.Forbidden);

        DeclaredLiveTopic? declared;
        string kind, key;
        if (first == LiveTopics.UnitKind)
        {
            if (rest.Contains(':')) return new(false, LiveErrors.InvalidTopic);
            (kind, key) = (LiveTopics.UnitKind, rest);
            declared = await catalog.FindUnitAsync(ct);
        }
        else
        {
            // {module}:{kind}:{key}; the reserved kinds belong to the platform topics above.
            var split = rest.IndexOf(':');
            if (split <= 0 || split == rest.Length - 1) return new(false, LiveErrors.InvalidTopic);
            (kind, key) = (rest[..split], rest[(split + 1)..]);
            declared = kind is LiveTopics.UnitKind or LiveTopics.UserKind ? null : await catalog.FindAsync(first, kind, ct);
        }
        if (declared is null) return new(false, LiveErrors.UnknownTopic);

        string[] resource = [AccessScope.Of(kind, key), .. await resourceScopes.ScopesAsync(declared.ModuleId, tenant, kind, key, ct)];

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantScopeInitializer>().Use(tenant, "live");
        scope.ServiceProvider.GetRequiredService<ScopeUser>().Principal = user;
        var access = scope.ServiceProvider.GetRequiredService<IAccessScopes>();
        foreach (var permission in declared.Topic.Permissions)
        {
            // Any one permission is enough; a blank one (a damaged record) never is.
            if (!string.IsNullOrWhiteSpace(permission) && await access.HasPermissionAsync(permission, resource, ct))
                return new(true);
        }
        return new(false, LiveErrors.Forbidden);
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

using System.Collections.Concurrent;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;

namespace soft.square.net.WebApi.Security;

/// <summary>One <c>Access:DevGrants:N</c> entry: these permissions for the account with this email, in this tenant.</summary>
internal sealed class DevGrant
{
    public string Tenant { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>Permission names; <c>module.*</c> means every permission a module declares (e.g. <c>content.*</c>).</summary>
    public List<string> Permissions { get; set; } = [];

    public string Scope { get; set; } = AccessScope.Tenant;
}

/// <summary>
/// Development only: grants that a dev setup (a module playground, the lab) needs from the start, by email, because
/// seeded accounts get random user ids. When a configured account calls the API in its tenant, the grants it does not
/// hold yet are written to the grant store as ordinary grants with the source <c>dev</c> (visible and revocable like
/// any other). Runs after tenant resolution; permissions of modules that are not installed yet are granted once they
/// are. Never registered outside Development.
/// </summary>
internal sealed class DevGrantSeeder(RequestDelegate next, IConfiguration configuration, ILogger<DevGrantSeeder> log)
{
    public const string Source = "dev";

    private readonly IReadOnlyList<DevGrant> _entries = configuration.GetSection("Access:DevGrants").Get<List<DevGrant>>() ?? [];

    // (tenant, user, permission, scope) already ensured by this process.
    private readonly ConcurrentDictionary<(string, string, string, string), bool> _ensured = new();

    public async Task InvokeAsync(HttpContext context, ITenantContext tenant, IPermissionCatalog catalog, IGrantStore store)
    {
        if (_entries.Count > 0 && context.User.Identity?.IsAuthenticated == true && tenant.TenantId is { } tenantId
            && context.User.FindFirst(PlatformClaims.Email)?.Value is { Length: > 0 } email
            && context.User.FindFirst(PlatformClaims.Subject)?.Value is { Length: > 0 } userId)
        {
            var entries = _entries.Where(e => e.Tenant.Equals(tenantId, StringComparison.OrdinalIgnoreCase)
                                              && e.Email.Equals(email, StringComparison.OrdinalIgnoreCase)).ToList();
            if (entries.Count > 0)
            {
                try
                {
                    await EnsureAsync(tenantId, userId, email, entries, catalog, store, context.RequestAborted);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogWarning(e, "Dev grants for {Email} in {Tenant} could not be written.", email, tenantId);
                }
            }
        }
        await next(context);
    }

    private async Task EnsureAsync(string tenantId, string userId, string email, List<DevGrant> entries, IPermissionCatalog catalog,
        IGrantStore store, CancellationToken ct)
    {
        var known = (await catalog.ListAsync(ct)).SelectMany(s => s.Permissions).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var wanted = entries
            .SelectMany(e => e.Permissions.SelectMany(p => Expand(p, known)).Select(p => (Permission: p, e.Scope)))
            .Where(w => !PlatformPermissions.IsPlatform(w.Permission) || w.Permission == PlatformPermissions.GrantsManage)
            .Distinct()
            .Where(w => !_ensured.ContainsKey((tenantId, userId, w.Permission, w.Scope)))
            .ToList();
        if (wanted.Count == 0) return;

        var held = await store.ListAsync(userId, source: null, ct);
        foreach (var (permission, scope) in wanted)
        {
            if (!held.Any(g => g.Permission == permission && g.Scope == scope && g.ValidTo is null))
            {
                await store.AddAsync(new NewGrant(userId, permission, scope), Source, ct);
                log.LogInformation("Dev grant: {Permission} ({Scope}) for {Email} in {Tenant}.", permission, scope, email, tenantId);
            }
            _ensured[(tenantId, userId, permission, scope)] = true;
        }
    }

    private static IEnumerable<string> Expand(string pattern, HashSet<string> known) =>
        pattern.EndsWith(".*", StringComparison.Ordinal)
            ? known.Where(k => k.StartsWith(pattern[..^1], StringComparison.Ordinal))
            : [pattern];
}

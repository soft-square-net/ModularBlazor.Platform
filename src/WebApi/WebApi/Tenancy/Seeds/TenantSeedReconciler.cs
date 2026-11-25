using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Security;

namespace soft.square.net.WebApi.Tenancy.Seeds;

/// <summary>Configuration section <c>Tenancy:Seeds</c>.</summary>
public sealed class TenantSeedOptions
{
    public const string Section = "Tenancy:Seeds";

    /// <summary>How often the leader pulls the seeds (default one minute).</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How long one cycle may hold the Redis leader lock (default ten minutes).</summary>
    public TimeSpan LeaderLease { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long the reconciler waits for this replica's tenant initializers (default five minutes).</summary>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The connection string name of Redis for the leader lock (default <c>redis</c>; the database lock without it).</summary>
    public string LockConnectionName { get; set; } = "redis";
}

/// <summary>Completes when this replica's loaded modules have initialized a tenant (implemented by the module tenant initializer).</summary>
internal interface ITenantReadiness
{
    Task ReadyAsync(TenantInfo tenant, CancellationToken ct);
}

/// <summary>Creates a tenant's database on its server (a seam over <see cref="TenantDatabases"/> for tests).</summary>
internal interface ITenantDatabaseCreator
{
    bool IsConfigured(string databaseServer);

    Task EnsureAsync(TenantInfo tenant, CancellationToken ct);
}

internal sealed class TenantDatabaseCreator(TenantDatabases databases) : ITenantDatabaseCreator
{
    public bool IsConfigured(string databaseServer) => databases.IsConfigured(databaseServer);

    public Task EnsureAsync(TenantInfo tenant, CancellationToken ct) => databases.EnsureDatabaseAsync(tenant, ct);
}

/// <summary>The default placement policy: the seed's placement as it is.</summary>
public sealed class AcceptAsIsPlacementPolicy : ITenantPlacementPolicy
{
    public ValueTask<PlacementDecision> DecideAsync(TenantSeed seed, CancellationToken ct) => ValueTask.FromResult(PlacementDecision.Accept);
}

/// <summary>
/// Reconciles the tenant seeds of this deployment (<see cref="TenantSeedNames.DeploymentIdKey"/>) into the tenant
/// store. One replica leads each cycle (Redis lock, or the tenant store's database lock). A cycle runs on a timer,
/// when a module serving seeds is loaded, and on <see cref="Trigger"/>. For each seed that is new, or whose version
/// is newer than the tenant's:
/// <list type="number">
/// <item>a new tenant's placement is decided by <see cref="ITenantPlacementPolicy"/> (accept, adjust or reject);</item>
/// <item>the tenant is created (database first) or its name and hosts updated, then every replica is told
/// (<see cref="ITenantDirectory.NotifyChangedAsync"/>), so the modules' <see cref="ITenantInitializer"/>s run;</item>
/// <item>the seed's admins get standing <c>access.grants.manage</c> grants (grant source <c>seed:{id}</c>);</item>
/// <item>the status is reported to the source, then the version is recorded on the tenant (so a failed step is
/// retried on the next cycle, and an applied version is a no-op).</item>
/// </list>
/// </summary>
internal sealed partial class TenantSeedReconciler(
    TenantSeedSources sources,
    IServiceScopeFactory scopes,
    ITenantDirectory directory,
    ILeaderLock leader,
    ITenantPlacementPolicy policy,
    ITenantReadiness readiness,
    IGrantChangeBus grantChanges,
    IConfiguration configuration,
    IOptions<TenantSeedOptions> options,
    ILogger<TenantSeedReconciler> log) : BackgroundService
{
    public const string LockKey = "platform:tenant-seeds:leader";

    private readonly SemaphoreSlim _signal = new(0, 1);
    // Rejections already reported by this replica, so a rejected version is not reported again on every cycle.
    private readonly ConcurrentDictionary<string, long> _rejected = new(StringComparer.Ordinal);

    public string? DeploymentId => configuration[TenantSeedNames.DeploymentIdKey] is { Length: > 0 } id ? id : null;

    /// <summary>
    /// Runs a cycle now instead of at the next tick (also on the leader lock's terms: a replica that does not lead skips it).
    /// TODO(host integration subscription): call this when <see cref="TenantSeedNames.SeedChangedEvent"/> arrives.
    /// The integration events (P0-1) deliver only to subscriber modules, in a tenant scope of the module's container,
    /// so the host cannot subscribe yet. It needs a host-level subscription: with the broker, a per-replica exclusive
    /// queue bound to the <c>integration</c> exchange with the event type as routing key; without it, a hook in the
    /// local bus. That subscription must accept an envelope without a tenant, because a seed belongs to no tenant yet.
    /// Until then the timer (<see cref="TenantSeedOptions.Interval"/>) picks the change up.
    /// </summary>
    public void Trigger()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { } // a cycle is already pending
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        sources.SourceLoaded += Trigger;
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        sources.SourceLoaded -= Trigger;
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (DeploymentId is null)
        {
            log.LogInformation("Tenant seeds are off: {Key} is not set", TenantSeedNames.DeploymentIdKey);
            return;
        }

        try
        {
            if (directory is TenantDirectory known) await known.Ready.WaitAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await ReconcileOnceAsync(stoppingToken); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogError(e, "Tenant seed reconciliation failed; retrying on the next cycle");
                }
                await _signal.WaitAsync(options.Value.Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    /// <summary>One cycle: nothing without a deployment id or a seed source, or when another replica leads.</summary>
    public async Task ReconcileOnceAsync(CancellationToken ct)
    {
        if (DeploymentId is not { } deploymentId || !sources.Any) return;

        await using var lease = await leader.TryAcquireAsync(LockKey, options.Value.LeaderLease, ct);
        if (lease is null) return; // another replica leads this cycle

        await sources.ForEachAsync(async (moduleId, source, token) =>
        {
            IReadOnlyList<TenantSeed> seeds;
            try { seeds = await source.ListAsync(deploymentId, token); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "The seed source of module {Module} could not list the seeds", moduleId);
                return;
            }
            foreach (var seed in seeds.Where(s => string.Equals(s.DeploymentId, deploymentId, StringComparison.Ordinal)))
            {
                try { await ReconcileAsync(seed, source, token); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogError(e, "Reconciling seed {Seed} v{Version} (tenant {Tenant}) failed", seed.Id, seed.Version, seed.TenantId);
                    await ReportAsync(source, seed, SeedStatus.Failed, null, e.Message, token);
                }
            }
        }, ct);
    }

    private async Task ReconcileAsync(TenantSeed seed, ITenantSeedSource source, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<TenantRegistry>();

        var state = await registry.SeedStateAsync(seed.TenantId, ct);
        if (state is not null && state.SeedId == seed.Id && state.SeedVersion >= seed.Version) return; // applied already
        if (_rejected.TryGetValue(seed.Id, out var rejected) && rejected == seed.Version) return;   // reported already

        if (Invalid(seed, state) is { } invalid)
        {
            await RejectAsync(source, seed, invalid, ct);
            return;
        }

        TenantInfo tenant;
        TenantPlacement actual;
        if (state is null)
        {
            var decision = await policy.DecideAsync(seed, ct);
            if (decision.Kind == PlacementDecisionKind.Reject)
            {
                await RejectAsync(source, seed, decision.Reason!, ct);
                return;
            }
            actual = decision.Kind == PlacementDecisionKind.Adjust ? decision.Placement! : seed.RequestedPlacement;

            var databases = scope.ServiceProvider.GetRequiredService<ITenantDatabaseCreator>();
            if (!databases.IsConfigured(actual.DatabaseServer))
            {
                await RejectAsync(source, seed, $"Unknown database server '{actual.DatabaseServer}' on this deployment.", ct);
                return;
            }

            await ReportAsync(source, seed, SeedStatus.Provisioning, null, null, ct);
            tenant = new TenantInfo(seed.TenantId, Name(seed), TenantStatus.Active, seed.Hosts, actual.DatabaseServer, actual.DatabaseName);
            await databases.EnsureAsync(tenant, ct);
            if (!await registry.AddSeededAsync(tenant, seed.Id, ct))
                throw new InvalidOperationException($"Tenant '{seed.TenantId}' was created by someone else meanwhile.");
            log.LogInformation("Created tenant {Tenant} from seed {Seed} v{Version}", tenant.Id, seed.Id, seed.Version);
        }
        else
        {
            await ReportAsync(source, seed, SeedStatus.Provisioning, null, null, ct);
            tenant = await registry.UpdateAsync(seed.TenantId, Name(seed), seed.Hosts, status: null, ct)
                     ?? throw new InvalidOperationException($"Tenant '{seed.TenantId}' was removed meanwhile.");
            actual = new TenantPlacement(tenant.DatabaseServer, tenant.DatabaseName); // a seed never moves a database
            log.LogInformation("Updated tenant {Tenant} from seed {Seed} v{Version}", tenant.Id, seed.Id, seed.Version);
        }

        // Every replica reloads; the modules' tenant initializers run for a new or re-activated tenant.
        await directory.NotifyChangedAsync(tenant.Id, ct);
        await GrantAdminsAsync(scope.ServiceProvider, seed, ct);
        if (tenant.IsActive)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.Value.InitializationTimeout);
            try { await readiness.ReadyAsync(tenant, timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"The modules did not initialize tenant '{tenant.Id}' within {options.Value.InitializationTimeout}.");
            }
        }

        var status = actual == seed.RequestedPlacement ? SeedStatus.Provisioned : SeedStatus.Adjusted;
        await source.ReportAsync(seed.Id, seed.Version, status, actual, null, ct);
        await registry.MarkSeedVersionAsync(tenant.Id, seed.Id, seed.Version, ct);
        _rejected.TryRemove(seed.Id, out _);
    }

    /// <summary>Why the seed cannot be applied whatever the placement; null when it can.</summary>
    private string? Invalid(TenantSeed seed, TenantSeedState? state)
    {
        if (!Slug().IsMatch(seed.TenantId ?? ""))
            return $"Invalid tenant id '{seed.TenantId}': lowercase letters, digits and '-', at most 40 characters.";
        if (state is not null && state.SeedId != seed.Id)
            return state.SeedId is null
                ? $"Tenant '{seed.TenantId}' exists already and was not created from a seed."
                : $"Tenant '{seed.TenantId}' belongs to seed '{state.SeedId}'.";
        foreach (var host in seed.Hosts ?? [])
        {
            if (directory.FindByHost(host) is { } other && !string.Equals(other.Id, seed.TenantId, StringComparison.OrdinalIgnoreCase))
                return $"Host '{host}' belongs to tenant '{other.Id}'.";
        }
        return null;
    }

    private async Task GrantAdminsAsync(IServiceProvider services, TenantSeed seed, CancellationToken ct)
    {
        var grants = (seed.Admins ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.Ordinal)
            .Select(admin => new AccessGrant(Guid.NewGuid(), seed.TenantId, admin, PlatformPermissions.GrantsManage, AccessScope.Tenant,
                null, null, TenantSeedNames.GrantSource(seed.Id), DateTimeOffset.UtcNow, $"seed:{seed.Id}"))
            .ToArray();
        var repository = services.GetRequiredService<IGrantRepository>();
        var users = await repository.ReplaceSourceAsync(seed.TenantId, TenantSeedNames.GrantSource(seed.Id), grants, ct);
        if (users.Count > 0) await grantChanges.ChangedAsync(seed.TenantId, users, ct);
    }

    private async Task RejectAsync(ITenantSeedSource source, TenantSeed seed, string reason, CancellationToken ct)
    {
        log.LogWarning("Rejected seed {Seed} v{Version} (tenant {Tenant}): {Reason}", seed.Id, seed.Version, seed.TenantId, reason);
        if (await ReportAsync(source, seed, SeedStatus.Rejected, null, reason, ct)) _rejected[seed.Id] = seed.Version;
    }

    private async Task<bool> ReportAsync(ITenantSeedSource source, TenantSeed seed, SeedStatus status, TenantPlacement? actual, string? reason, CancellationToken ct)
    {
        try
        {
            await source.ReportAsync(seed.Id, seed.Version, status, actual, reason, ct);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Could not report {Status} for seed {Seed} v{Version}", status, seed.Id, seed.Version);
            return false;
        }
    }

    private static string Name(TenantSeed seed) => string.IsNullOrWhiteSpace(seed.Name) ? seed.TenantId : seed.Name;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex Slug();
}

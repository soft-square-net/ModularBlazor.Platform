using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Security;
using soft.square.net.WebApi.Tenancy;
using soft.square.net.WebApi.Tenancy.Seeds;

namespace soft.square.net.WebApi.Tests.Tenancy;

/// <summary>The tenant seed reconciler against a real tenant store (SQLite in memory) and the real tenant directory.</summary>
public sealed class TenantSeedReconcilerTests : IAsyncLifetime
{
    private const string Deployment = "dep-1";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeSeedSource _source = new();
    private readonly FakeDatabases _databases = new();
    private readonly FakeLeader _leader = new();
    private readonly FakeGrantChanges _grantChanges = new();
    private ITenantPlacementPolicy _policy = new AcceptAsIsPlacementPolicy();
    private ServiceProvider _host = null!;
    private ServiceProvider? _module;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        _host = Build();
        await using var scope = _host.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Database.EnsureCreatedAsync();
        await _host.GetRequiredService<TenantDirectory>().ReloadAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null) await _module.DisposeAsync();
        await _host.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ServiceProvider Build() => new ServiceCollection()
        .AddLogging()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{TenancyHostExtensions.StoreConnectionName}"] = "sqlite",
            [TenantSeedNames.DeploymentIdKey] = Deployment
        }).Build())
        .AddDbContext<TenantStoreDbContext>(o => o.UseSqlite(_connection))
        .AddScoped<TenantRegistry>()
        .AddSingleton<ITenantChangeBus, NullTenantChangeBus>()
        .AddSingleton<TenantDirectory>()
        .AddSingleton<ITenantDirectory>(sp => sp.GetRequiredService<TenantDirectory>())
        .AddSingleton<IGrantRepository, InMemoryGrantRepository>()
        .AddSingleton<IGrantChangeBus>(_grantChanges)
        .AddSingleton<ITenantDatabaseCreator>(_databases)
        .AddSingleton<ILeaderLock>(_leader)
        .AddSingleton<ITenantReadiness, ReadyAtOnce>()
        .AddSingleton<ITenantPlacementPolicy>(new Forward(() => _policy))
        .AddSingleton<TenantSeedSources>()
        .AddSingleton<TenantSeedReconciler>()
        .AddOptions<TenantSeedOptions>().Services
        .BuildServiceProvider();

    /// <summary>Loads a "module" whose container serves <see cref="_source"/>.</summary>
    private void LoadSourceModule()
    {
        _module = new ServiceCollection().AddSingleton<ITenantSeedSource>(_source).BuildServiceProvider();
        _host.GetRequiredService<TenantSeedSources>().Loaded("organization", _module);
    }

    private Task CycleAsync() => _host.GetRequiredService<TenantSeedReconciler>().ReconcileOnceAsync(CancellationToken.None);

    private static TenantSeed Seed(long version = 1, string name = "Acme", string[]? hosts = null, string[]? admins = null, string deployment = Deployment) =>
        new("seed-acme", version, deployment, "acme", name, hosts ?? ["acme.localhost"], "tenant-data", "tenant-acme",
            ["content"], admins ?? ["user-1"]);

    private async Task<TenantRecord?> RecordAsync(string id)
    {
        await using var scope = _host.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
    }

    [Fact]
    public async Task A_seed_creates_the_tenant_grants_its_admins_and_reports_provisioned()
    {
        _source.Seeds = [Seed(admins: ["user-1", "user-2"])];
        LoadSourceModule();

        await CycleAsync();

        var record = (await RecordAsync("acme")).ShouldNotBeNull();
        record.Name.ShouldBe("Acme");
        record.Hosts.ShouldBe(["acme.localhost"]);
        record.DatabaseServer.ShouldBe("tenant-data");
        record.DatabaseName.ShouldBe("tenant-acme");
        record.SeedId.ShouldBe("seed-acme");
        record.SeedVersion.ShouldBe(1);
        _databases.Ensured.ShouldBe(["acme"]);
        _host.GetRequiredService<ITenantDirectory>().FindByHost("acme.localhost")!.Id.ShouldBe("acme");

        var grants = await _host.GetRequiredService<IGrantRepository>().ListAsync("acme", null, TenantSeedNames.GrantSource("seed-acme"), CancellationToken.None);
        grants.Select(g => g.UserId).ShouldBe(["user-1", "user-2"]);
        grants.ShouldAllBe(g => g.Permission == PlatformPermissions.GrantsManage && g.Scope == AccessScope.Tenant && g.ValidTo == null);
        _grantChanges.Tenants.ShouldContain("acme");

        _source.Reports.Select(r => r.Status).ShouldBe([SeedStatus.Provisioning, SeedStatus.Provisioned]);
        _source.Reports[^1].Placement.ShouldBe(new TenantPlacement("tenant-data", "tenant-acme"));
    }

    [Fact]
    public async Task The_same_version_again_is_a_no_op()
    {
        _source.Seeds = [Seed()];
        LoadSourceModule();
        await CycleAsync();
        var reports = _source.Reports.Count;
        var updatedAt = (await RecordAsync("acme"))!.UpdatedAt;

        await CycleAsync();

        _source.Reports.Count.ShouldBe(reports);
        _databases.Ensured.Count.ShouldBe(1);
        (await RecordAsync("acme"))!.UpdatedAt.ShouldBe(updatedAt);
    }

    [Fact]
    public async Task A_new_version_updates_the_name_and_hosts_but_never_moves_the_database()
    {
        _source.Seeds = [Seed()];
        LoadSourceModule();
        await CycleAsync();

        _source.Seeds = [Seed(version: 2, name: "Acme News", hosts: ["news.acme.localhost"]) with { DatabaseName = "elsewhere" }];
        await CycleAsync();

        var record = (await RecordAsync("acme")).ShouldNotBeNull();
        record.Name.ShouldBe("Acme News");
        record.Hosts.ShouldBe(["news.acme.localhost"]);
        record.DatabaseName.ShouldBe("tenant-acme");
        record.SeedVersion.ShouldBe(2);
        _databases.Ensured.Count.ShouldBe(1);
        var tenant = _host.GetRequiredService<ITenantDirectory>().Find("acme")!;
        tenant.Name.ShouldBe("Acme News");

        var last = _source.Reports[^1];
        (last.Version, last.Status).ShouldBe((2L, SeedStatus.Adjusted)); // the requested database differs from the actual one
        last.Placement.ShouldBe(new TenantPlacement("tenant-data", "tenant-acme"));
    }

    [Fact]
    public async Task A_rejection_is_reported_once_and_creates_nothing()
    {
        _policy = new RejectAll("No capacity on tenant-data.");
        _source.Seeds = [Seed()];
        LoadSourceModule();

        await CycleAsync();
        await CycleAsync();

        (await RecordAsync("acme")).ShouldBeNull();
        _databases.Ensured.ShouldBeEmpty();
        _source.Reports.Count.ShouldBe(1);
        _source.Reports[0].Status.ShouldBe(SeedStatus.Rejected);
        _source.Reports[0].Reason.ShouldBe("No capacity on tenant-data.");
    }

    [Fact]
    public async Task An_adjusted_placement_is_used_and_reported()
    {
        _policy = new MoveTo(new TenantPlacement("tenant-data-2", "acme-db"));
        _databases.Servers.Add("tenant-data-2");
        _source.Seeds = [Seed()];
        LoadSourceModule();

        await CycleAsync();

        (await RecordAsync("acme"))!.DatabaseServer.ShouldBe("tenant-data-2");
        _source.Reports[^1].Status.ShouldBe(SeedStatus.Adjusted);
        _source.Reports[^1].Placement.ShouldBe(new TenantPlacement("tenant-data-2", "acme-db"));
    }

    [Fact]
    public async Task An_unknown_database_server_or_a_foreign_tenant_is_rejected()
    {
        _source.Seeds =
        [
            Seed() with { DatabaseServer = "nowhere" },
            Seed() with { Id = "seed-taken", TenantId = "taken", Hosts = [] }
        ];
        await using (var scope = _host.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TenantRegistry>().AddAsync(
                new TenantInfo("taken", "Taken", TenantStatus.Active, [], "tenant-data", "tenant-taken"), CancellationToken.None);
        LoadSourceModule();

        await CycleAsync();

        (await RecordAsync("acme")).ShouldBeNull();
        (await RecordAsync("taken"))!.SeedId.ShouldBeNull();
        _source.Reports.Select(r => (r.SeedId, r.Status)).ShouldBe([("seed-acme", SeedStatus.Rejected), ("seed-taken", SeedStatus.Rejected)]);
    }

    [Fact]
    public async Task Without_a_seed_source_nothing_happens()
    {
        _source.Seeds = [Seed()];

        await CycleAsync();

        _leader.Attempts.ShouldBe(0);
        (await RecordAsync("acme")).ShouldBeNull();
        _source.Lists.ShouldBe(0);
    }

    [Fact]
    public async Task A_released_module_is_no_longer_asked()
    {
        _source.Seeds = [Seed()];
        LoadSourceModule();
        await _host.GetRequiredService<TenantSeedSources>().ReleasingAsync("organization", _module!);

        await CycleAsync();

        _source.Lists.ShouldBe(0);
        (await RecordAsync("acme")).ShouldBeNull();
    }

    [Fact]
    public async Task A_replica_that_does_not_lead_skips_the_cycle()
    {
        _leader.Free = false;
        _source.Seeds = [Seed()];
        LoadSourceModule();

        await CycleAsync();

        _leader.Attempts.ShouldBe(1);
        _source.Lists.ShouldBe(0);
    }

    [Fact]
    public async Task Seeds_of_another_deployment_are_ignored_and_a_failed_report_is_retried()
    {
        _source.Seeds = [Seed(deployment: "dep-2"), Seed() with { Id = "seed-b", TenantId = "b", Hosts = [] }];
        _source.FailProvisioned = true;
        LoadSourceModule();

        await CycleAsync();
        (await RecordAsync("b"))!.SeedVersion.ShouldBeNull(); // not marked: the final report failed
        (await RecordAsync("acme")).ShouldBeNull();

        _source.FailProvisioned = false;
        await CycleAsync();
        (await RecordAsync("b"))!.SeedVersion.ShouldBe(1);
        _source.Reports[^1].Status.ShouldBe(SeedStatus.Provisioned);
        _databases.Ensured.ShouldBe(["b"]);
    }

    private sealed class FakeSeedSource : ITenantSeedSource
    {
        public IReadOnlyList<TenantSeed> Seeds { get; set; } = [];
        public List<(string SeedId, long Version, SeedStatus Status, TenantPlacement? Placement, string? Reason)> Reports { get; } = [];
        public int Lists { get; private set; }
        public bool FailProvisioned { get; set; }

        public Task<IReadOnlyList<TenantSeed>> ListAsync(string deploymentId, CancellationToken ct)
        {
            Lists++;
            deploymentId.ShouldBe(Deployment);
            return Task.FromResult(Seeds);
        }

        public Task ReportAsync(string seedId, long version, SeedStatus status, TenantPlacement? actualPlacement, string? reason, CancellationToken ct)
        {
            if (FailProvisioned && status == SeedStatus.Provisioned) throw new HttpRequestException("central is down");
            Reports.Add((seedId, version, status, actualPlacement, reason));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDatabases : ITenantDatabaseCreator
    {
        public HashSet<string> Servers { get; } = ["tenant-data"];
        public List<string> Ensured { get; } = [];

        public bool IsConfigured(string databaseServer) => Servers.Contains(databaseServer);

        public Task EnsureAsync(TenantInfo tenant, CancellationToken ct)
        {
            Ensured.Add(tenant.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLeader : ILeaderLock
    {
        public bool Free { get; set; } = true;
        public int Attempts { get; private set; }

        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan lease, CancellationToken ct)
        {
            Attempts++;
            return Task.FromResult<IAsyncDisposable?>(Free ? new Released() : null);
        }

        private sealed class Released : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeGrantChanges : IGrantChangeBus
    {
        public ConcurrentBag<string> Tenants { get; } = [];

        public Task ChangedAsync(string tenantId, IReadOnlyCollection<string>? userIds, CancellationToken ct)
        {
            Tenants.Add(tenantId);
            return Task.CompletedTask;
        }
    }

    private sealed class ReadyAtOnce : ITenantReadiness
    {
        public Task ReadyAsync(TenantInfo tenant, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Forward(Func<ITenantPlacementPolicy> current) : ITenantPlacementPolicy
    {
        public ValueTask<PlacementDecision> DecideAsync(TenantSeed seed, CancellationToken ct) => current().DecideAsync(seed, ct);
    }

    private sealed class RejectAll(string reason) : ITenantPlacementPolicy
    {
        public ValueTask<PlacementDecision> DecideAsync(TenantSeed seed, CancellationToken ct) => ValueTask.FromResult(PlacementDecision.Reject(reason));
    }

    private sealed class MoveTo(TenantPlacement placement) : ITenantPlacementPolicy
    {
        public ValueTask<PlacementDecision> DecideAsync(TenantSeed seed, CancellationToken ct) => ValueTask.FromResult(PlacementDecision.Adjust(placement));
    }
}

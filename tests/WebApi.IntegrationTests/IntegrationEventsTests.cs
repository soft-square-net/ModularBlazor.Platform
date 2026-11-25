using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Messaging;
using soft.square.net.WebApi.Modularity;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace soft.square.net.WebApi.IntegrationTests;

/// <summary>
/// RabbitMQ and PostgreSQL in Docker, a tenant "acme", and a shared module catalog holding the two test modules
/// (evt-publisher, evt-subscriber), seeded as core modules so every replica activates them on start.
/// </summary>
public sealed class EventsInfrastructure : IAsyncLifetime
{
    public const string Pool = "test";
    public const string TenantDatabase = "evt_acme";

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder().WithImage("rabbitmq:4-management").Build();
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:17").Build();

    public string CatalogPath { get; } = Path.Combine(Path.GetTempPath(), "mb-events-" + Guid.NewGuid().ToString("n"));
    public string BrokerConnection => _rabbit.GetConnectionString();
    public string ServerConnection => _postgres.GetConnectionString();
    public string TenantConnection => new NpgsqlConnectionStringBuilder(ServerConnection) { Database = TenantDatabase }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_rabbit.StartAsync(), _postgres.StartAsync());
        await using var connection = new NpgsqlConnection(ServerConnection);
        await connection.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {TenantDatabase}", connection);
        await create.ExecuteNonQueryAsync();
        Directory.CreateDirectory(CatalogPath);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_rabbit.DisposeAsync().AsTask(), _postgres.DisposeAsync().AsTask());
        try { Directory.Delete(CatalogPath, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public Replica StartReplica(bool broker = true) => new(this, broker);

    /// <summary>
    /// Waits until both test modules are migrated in the tenant database. Replicas initialize tenants in the background
    /// after start, so a test that reads the modules' tables first (on a fresh database) must wait for it.
    /// </summary>
    public async Task WaitForModulesAsync()
    {
        const string sql = """
            SELECT (to_regclass('evt_publisher.integration_outbox') IS NOT NULL
                AND to_regclass('evt_publisher.orders') IS NOT NULL
                AND to_regclass('evt_subscriber.integration_inbox') IS NOT NULL
                AND to_regclass('evt_subscriber.received') IS NOT NULL)::int
            """;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (await ScalarAsync(sql) == 0)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The test modules were not migrated for tenant acme in time.");
            await Task.Delay(250);
        }
    }

    public async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(TenantConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(TenantConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Ready messages in a queue (not those delivered and unacknowledged).</summary>
    public async Task<uint> QueueLengthAsync(string queue)
    {
        var factory = new ConnectionFactory { Uri = new Uri(BrokerConnection) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        return (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;
    }
}

/// <summary>One WebApi replica of pool "test" (in process), on the shared catalog, broker and tenant server.</summary>
public sealed class Replica : WebApplicationFactory<Program>
{
    private readonly EventsInfrastructure _infrastructure;
    private readonly bool _broker;

    public Replica(EventsInfrastructure infrastructure, bool broker)
    {
        _infrastructure = infrastructure;
        _broker = broker;
        _ = Services; // start now
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings:broker"] = _infrastructure.BrokerConnection,
            ["ConnectionStrings:tenant-data"] = _infrastructure.ServerConnection,
            ["DatabaseServers:tenant-data:Provider"] = "PostgreSql",
            ["Tenancy:Tenants:0:Id"] = "acme",
            ["Tenancy:Tenants:0:Name"] = "Acme",
            ["Tenancy:Tenants:0:DatabaseServer"] = "tenant-data",
            ["Tenancy:Tenants:0:DatabaseName"] = EventsInfrastructure.TenantDatabase,
            ["Modules:Pool"] = EventsInfrastructure.Pool,
            ["Modules:Path"] = _infrastructure.CatalogPath,
            ["Modules:CorePath"] = Path.Combine(AppContext.BaseDirectory, "EventModules"),
            ["Integration:MaxAttempts"] = "3",
            ["Integration:RetryDelay"] = "00:00:00.500",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:soft.square.net.WebApi.Messaging"] = "Information",
        };
        if (!_broker) settings.Remove("ConnectionStrings:broker"); // a single instance: the in-process bus
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
    }

    internal ModuleRuntime Runtime => Services.GetRequiredService<ModuleRuntime>();

    public async Task<HttpStatusCode> PlaceOrderAsync(int number, bool fail = false)
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant", "acme");
        using var response = await client.PostAsync($"/api/evt-publisher/orders?number={number}&fail={(fail ? "true" : "false")}", null);
        return response.StatusCode;
    }
}

[CollectionDefinition(nameof(EventsCollection))]
public sealed class EventsCollection : ICollectionFixture<EventsInfrastructure>;

/// <summary>
/// The "Done when" of integration events: an event published in module A's transaction reaches module B exactly
/// once across a replica restart; while B is unloaded its events wait and are delivered when it is loaded again; a
/// rolled-back save sends nothing. Each test uses its own range of order numbers.
/// </summary>
[Collection(nameof(EventsCollection))]
public sealed class IntegrationEventsTests(EventsInfrastructure infrastructure) : IAsyncLifetime
{
    private static readonly string SubscriberQueue = IntegrationNames.Queue("evt-subscriber", EventsInfrastructure.Pool);
    private Replica _first = null!;
    private Replica _second = null!;

    public async ValueTask InitializeAsync()
    {
        _first = infrastructure.StartReplica();
        _second = infrastructure.StartReplica();
        await infrastructure.WaitForModulesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _second.DisposeAsync();
        await _first.DisposeAsync();
    }

    [Fact]
    public async Task An_event_reaches_the_subscriber_exactly_once_across_a_replica_restart()
    {
        for (var n = 1000; n < 1010; n++) (await _first.PlaceOrderAsync(n)).ShouldBe(HttpStatusCode.OK);

        // Restart the second replica while it may hold unacknowledged events: they are delivered again.
        await _second.DisposeAsync();
        _second = infrastructure.StartReplica();
        for (var n = 1010; n < 1020; n++) (await _second.PlaceOrderAsync(n)).ShouldBe(HttpStatusCode.OK);

        await WaitUntilAsync(async () => await ReceivedAsync(1000, 1019) == 20);

        // Every event is sent again (as after a crash between the broker's confirm and marking the row sent).
        await infrastructure.ExecuteAsync("""UPDATE evt_publisher.integration_outbox SET "SentAt" = NULL""");
        await WaitUntilAsync(async () => await infrastructure.ScalarAsync(
            """SELECT count(*) FROM evt_publisher.integration_outbox WHERE "SentAt" IS NULL""") == 0);
        await WaitUntilAsync(async () => await infrastructure.QueueLengthAsync(SubscriberQueue) == 0);
        await Task.Delay(TimeSpan.FromSeconds(2)); // the last redeliveries are handled (and dropped)

        (await ReceivedAsync(1000, 1019)).ShouldBe(20);
        (await infrastructure.ScalarAsync(
            """SELECT count(DISTINCT "EventId") FROM evt_subscriber.received WHERE "Number" BETWEEN 1000 AND 1019""")).ShouldBe(20);
    }

    [Fact]
    public async Task Events_wait_while_the_subscriber_is_unloaded_and_arrive_when_it_is_loaded_again()
    {
        // The consumer let go of the module: its load context is collected (the host holds none of its types).
        var unloaded = await _first.Runtime.UnloadAsync("evt-subscriber", deferred: false, CancellationToken.None);
        unloaded.Unloaded.ShouldBeTrue(unloaded.Note);
        await _second.Runtime.SyncAsync(CancellationToken.None); // same process: the broadcast is ignored as our own
        try
        {
            for (var n = 2000; n < 2005; n++) (await _first.PlaceOrderAsync(n)).ShouldBe(HttpStatusCode.OK);

            await WaitUntilAsync(async () => await infrastructure.QueueLengthAsync(SubscriberQueue) == 5);
            (await ReceivedAsync(2000, 2004)).ShouldBe(0);
        }
        finally
        {
            await _first.Runtime.ActivateAsync("evt-subscriber", CancellationToken.None);
            await _second.Runtime.SyncAsync(CancellationToken.None);
        }

        await WaitUntilAsync(async () => await ReceivedAsync(2000, 2004) == 5);
        (await infrastructure.QueueLengthAsync(SubscriberQueue)).ShouldBe(0u);
    }

    [Fact]
    public async Task A_rolled_back_save_sends_nothing()
    {
        var outboxBefore = await infrastructure.ScalarAsync("SELECT count(*) FROM evt_publisher.integration_outbox");

        (await _first.PlaceOrderAsync(3000, fail: true)).ShouldBe(HttpStatusCode.Conflict);
        (await _first.PlaceOrderAsync(3001)).ShouldBe(HttpStatusCode.OK); // a committed one, to know delivery works

        await WaitUntilAsync(async () => await ReceivedAsync(3001, 3001) == 1);
        await Task.Delay(TimeSpan.FromSeconds(3)); // several sweeps

        (await ReceivedAsync(3000, 3000)).ShouldBe(0);
        (await infrastructure.ScalarAsync("""SELECT count(*) FROM evt_publisher.orders WHERE "Number" = 3000""")).ShouldBe(0);
        (await infrastructure.ScalarAsync("SELECT count(*) FROM evt_publisher.integration_outbox")).ShouldBe(outboxBefore + 1);
    }

    [Fact]
    public async Task An_event_that_keeps_failing_is_dead_lettered_and_can_be_replayed()
    {
        var dead = IntegrationNames.DeadQueue(SubscriberQueue);
        var before = await infrastructure.QueueLengthAsync(dead);

        (await _first.PlaceOrderAsync(-1)).ShouldBe(HttpStatusCode.OK);

        await WaitUntilAsync(async () => await infrastructure.QueueLengthAsync(dead) == before + 1);
        var letters = _first.Services.GetRequiredService<IDeadLetters>();
        var peeked = await letters.PeekAsync("evt-subscriber", 50, CancellationToken.None);
        var letter = peeked.FirstOrDefault(l => l.Type == "evtest.order-placed.v1" && l.Attempts == 3).ShouldNotBeNull();
        letter.Error.ShouldNotBeNull().ShouldContain("Order number -1 is invalid");
        (await infrastructure.QueueLengthAsync(dead)).ShouldBe(before + 1); // peek leaves them in place

        (await letters.ReplayAsync("evt-subscriber", CancellationToken.None)).ShouldBe((int)(before + 1));
        // Still failing: it goes through its attempts again and back to the dead letters.
        await WaitUntilAsync(async () => await infrastructure.QueueLengthAsync(dead) == before + 1);
        (await ReceivedAsync(-1, -1)).ShouldBe(0);
    }

    private Task<long> ReceivedAsync(int from, int to) =>
        infrastructure.ScalarAsync($"""SELECT count(*) FROM evt_subscriber.received WHERE "Number" BETWEEN {from} AND {to}""");

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int seconds = 60)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            try { if (await condition()) return; }
            catch (Exception) when (DateTime.UtcNow < deadline) { } // e.g. the table is not migrated yet
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(250);
        }
    }
}

/// <summary>Without a broker, the in-process bus delivers to the subscriber modules loaded on the same instance.</summary>
[Collection(nameof(EventsCollection))]
public sealed class LocalIntegrationBusTests(EventsInfrastructure infrastructure)
{
    [Fact]
    public async Task Without_a_broker_events_reach_the_modules_of_the_same_instance()
    {
        await using var single = infrastructure.StartReplica(broker: false);
        await infrastructure.WaitForModulesAsync();
        single.Services.GetRequiredService<IBrokerPublisher>().ShouldBeOfType<LocalIntegrationBus>();

        (await single.PlaceOrderAsync(4000)).ShouldBe(HttpStatusCode.OK);

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (await infrastructure.ScalarAsync("""SELECT count(*) FROM evt_subscriber.received WHERE "Number" = 4000""") == 0)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The event was not delivered.");
            await Task.Delay(250);
        }
        (await infrastructure.ScalarAsync("""SELECT count(*) FROM evt_subscriber.received WHERE "Number" = 4000""")).ShouldBe(1);
    }
}

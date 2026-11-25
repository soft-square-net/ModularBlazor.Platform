using Testcontainers.RabbitMq;

namespace soft.square.net.WebApi.Tests.Live;

/// <summary>
/// Two replicas on one RabbitMQ (in Docker): a module on one replica publishes, a client connected to the other one
/// receives it (fanout "live"). Skipped when Docker is not available.
/// </summary>
public sealed class LiveReplicaFanoutTests : IAsyncLifetime
{
    private RabbitMqContainer? _broker;
    private LiveTestHost? _a;
    private LiveTestHost? _b;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _broker = new RabbitMqBuilder().WithImage("rabbitmq:4-alpine").Build();
            await _broker.StartAsync();
        }
        catch (Exception e)
        {
            _broker = null;
            Console.WriteLine($"Docker is not available: {e.Message}");
            return;
        }
        _a = await LiveTestHost.StartAsync(_broker.GetConnectionString());
        _b = await LiveTestHost.StartAsync(_broker.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        if (_a is not null) await _a.DisposeAsync();
        if (_b is not null) await _b.DisposeAsync();
        if (_broker is not null) await _broker.DisposeAsync();
    }

    [Fact]
    public async Task A_message_published_on_one_replica_reaches_clients_on_every_replica()
    {
        if (_a is null || _b is null) Assert.Skip("Docker is not available.");

        await using var onA = await _a.ConnectAsync("alice");
        await using var onB = await _b.ConnectAsync("bob");
        (await onA.SubscribeAsync("user:alice")).Ok.ShouldBeTrue();
        (await onB.SubscribeAsync("user:bob")).Ok.ShouldBeTrue();

        await _b.PublishAsync("acme", n => n.PublishAsync("user:alice", "from-b", sequence: 1));
        await _a.PublishAsync("acme", n => n.PublishAsync("user:bob", "from-a", sequence: 1));

        (await onA.NextAsync()).Type.ShouldBe("from-b");
        (await onB.NextAsync()).Type.ShouldBe("from-a");
        // The publisher's replica delivers once: not again when its own message comes back from the broker.
        await _a.PublishAsync("acme", n => n.PublishAsync("user:alice", "local", sequence: 2));
        (await onA.NextAsync()).Type.ShouldBe("local");
        (await onA.QuietAsync(TimeSpan.FromSeconds(1))).ShouldBeTrue();
    }
}

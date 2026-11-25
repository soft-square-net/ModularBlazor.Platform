using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using soft.square.net.ModularBlazor.Abstractions.Live;
using soft.square.net.ModularBlazor.BlzWebClient.Services.Live;
using Testcontainers.RabbitMq;

namespace soft.square.net.WebApi.Tests.Live;

/// <summary>
/// The P0-2 acceptance scenario with the Blazor client's own channel and SignalR transport: a page connected to replica A
/// receives what a module publishes on replica B (RabbitMQ fanout "live"); replica A goes down, a message is missed, A
/// comes back, and the channel reconnects, joins the topic again and raises a resync from the last sequence the page
/// saw, so the page catches up from the module's feed. Skipped when Docker is not available.
/// </summary>
public sealed class LiveEndToEndTests : IAsyncLifetime
{
    private RabbitMqContainer? _broker;

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
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_broker is not null) await _broker.DisposeAsync();
    }

    [Fact]
    public async Task A_page_receives_messages_across_replicas_and_catches_up_after_the_connection_drops()
    {
        if (_broker is null) Assert.Skip("Docker is not available.");
        var broker = _broker.GetConnectionString();
        var address = $"http://127.0.0.1:{FreePort()}";
        var a = await LiveTestHost.StartAsync(broker, address);
        await using var b = await LiveTestHost.StartAsync(broker);
        await b.GrantAsync("acme", "alice", "content.view", "post:42");
        await a.GrantAsync("acme", "alice", "content.view", "post:42"); // in-memory grants: each replica has its own

        // The module's feed (GET .../posts/42/events?after=n) and what the page has applied, in order.
        var feed = new List<long>();
        var page = new List<long>();
        var gate = new object();
        void Apply(long sequence) { lock (gate) if (!page.Contains(sequence)) page.Add(sequence); }

        async Task PublishOnBAsync(long sequence)
        {
            lock (gate) feed.Add(sequence);
            await b.PublishAsync("acme", n => n.PublishAsync("content:post:42", "post.updated", sequence: sequence));
        }

        var token = LiveTestHost.Token("alice", "acme");
        await using var channel = new LiveChannel(
            new HubLiveTransport(new Uri(address + "/"), () => Task.FromResult<string?>(token)),
            NullLogger<LiveChannel>.Instance,
            new LiveChannelOptions { RetryDelays = [TimeSpan.Zero, TimeSpan.FromMilliseconds(250)] });
        var resyncs = new List<LiveResync>();
        await channel.StartAsync();
        await using var subscription = await channel.SubscribeAsync("content:post:42",
            m => { Apply(m.Sequence!.Value); return Task.CompletedTask; },
            fromSequence: 0,
            new LiveSubscribeOptions
            {
                OnResync = r =>
                {
                    lock (gate)
                    {
                        resyncs.Add(r);
                        foreach (var sequence in feed.Where(s => s > (r.LastSequence ?? 0)).ToArray()) Apply(sequence);
                    }
                    return Task.CompletedTask;
                }
            });
        await WaitAsync(() => channel.State == LiveState.Connected);

        await PublishOnBAsync(1);
        await WaitAsync(() => Snapshot().SequenceEqual([1L]));

        await a.DisposeAsync(); // replica A goes away; the page's connection drops
        await WaitAsync(() => channel.State == LiveState.Reconnecting);
        await PublishOnBAsync(2); // nobody on A hears it

        await using var aAgain = await LiveTestHost.StartAsync(broker, address);
        await aAgain.GrantAsync("acme", "alice", "content.view", "post:42");
        await WaitAsync(() => channel.State == LiveState.Connected, TimeSpan.FromSeconds(30));
        await WaitAsync(() => Snapshot().SequenceEqual([1L, 2L])); // caught up from the feed

        await PublishOnBAsync(3);
        await WaitAsync(() => Snapshot().SequenceEqual([1L, 2L, 3L]));
        lock (gate) resyncs.ShouldContain(new LiveResync("content:post:42", 1, LiveResyncReason.Reconnected));

        long[] Snapshot() { lock (gate) return [.. page]; }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("The condition was not met in time.");
            await Task.Delay(50);
        }
    }
}

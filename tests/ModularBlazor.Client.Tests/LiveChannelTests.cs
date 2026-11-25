using Microsoft.Extensions.Logging.Abstractions;
using soft.square.net.ModularBlazor.Abstractions.Live;
using soft.square.net.ModularBlazor.BlzWebClient.Services.Live;

namespace soft.square.net.ModularBlazor.Client.Tests;

/// <summary>The client's live channel over a fake hub: joins, reconnects, re-subscribes, resyncs and revocations.</summary>
public sealed class LiveChannelTests
{
    private readonly FakeTransport _hub = new();
    private readonly LiveChannel _channel;
    private readonly List<LiveResync> _resyncs = [];

    public LiveChannelTests()
    {
        _channel = new LiveChannel(_hub, NullLogger<LiveChannel>.Instance, new LiveChannelOptions { RetryDelays = [TimeSpan.Zero, TimeSpan.FromMilliseconds(10)] });
        _channel.Resync += r => { _resyncs.Add(r); return Task.CompletedTask; };
    }

    private static LiveMessage Message(string topic, long? sequence, string type = "changed") =>
        new(topic, type, null, sequence, DateTimeOffset.UtcNow);

    private async Task StartAsync()
    {
        await _channel.StartAsync();
        await _channel.Connecting;
    }

    [Fact]
    public async Task A_subscription_made_offline_joins_when_the_channel_connects_and_catches_up_from_its_sequence()
    {
        var received = new List<LiveMessage>();
        await using var subscription = await _channel.SubscribeAsync("content:post:1", m => { received.Add(m); return Task.CompletedTask; },
            fromSequence: 5, new LiveSubscribeOptions { Scopes = ["press:A"] });
        _channel.State.ShouldBe(LiveState.Offline);
        _hub.Joined.ShouldBeEmpty();

        await StartAsync();

        _channel.State.ShouldBe(LiveState.Connected);
        _hub.Joined.ShouldBe(["content:post:1"]);
        _hub.Scopes["content:post:1"].ShouldBe(["press:A"]);
        _resyncs.ShouldBe([new LiveResync("content:post:1", 5, LiveResyncReason.Subscribed)]);

        await _hub.SendAsync(Message("content:post:1", 6));
        received.Select(m => m.Sequence).ShouldBe([6L]);
    }

    [Fact]
    public async Task A_dropped_connection_reconnects_joins_every_topic_again_and_resyncs_them()
    {
        await StartAsync();
        await using var a = await _channel.SubscribeAsync("user:alice", _ => Task.CompletedTask);
        await using var b = await _channel.SubscribeAsync("content:post:1", _ => Task.CompletedTask, fromSequence: 3);
        await _hub.SendAsync(Message("content:post:1", 4));
        _hub.Joined.Clear();
        _resyncs.Clear();
        var states = new List<LiveState>();
        _channel.StateChanged += states.Add;

        _hub.FailStarts = 2; // the first two attempts fail, then the hub is back
        await _hub.DropAsync();
        await _channel.Connecting;

        _hub.Starts.ShouldBe(4); // the first start, then three attempts after the drop
        states.ShouldBe([LiveState.Reconnecting, LiveState.Connected]);
        _hub.Joined.Order().ShouldBe(["content:post:1", "user:alice"]);
        _resyncs.OrderBy(r => r.Topic).ShouldBe([
            new LiveResync("content:post:1", 4, LiveResyncReason.Reconnected),
            new LiveResync("user:alice", null, LiveResyncReason.Reconnected)]);
    }

    [Fact]
    public async Task Duplicates_are_dropped_and_a_gap_resyncs_from_the_last_sequence_seen()
    {
        await StartAsync();
        var received = new List<long?>();
        LiveResync? own = null;
        await using var subscription = await _channel.SubscribeAsync("content:post:1", m => { received.Add(m.Sequence); return Task.CompletedTask; },
            fromSequence: 10, new LiveSubscribeOptions { OnResync = r => { own = r; return Task.CompletedTask; } });
        _resyncs.Clear();

        await _hub.SendAsync(Message("content:post:1", 11));
        await _hub.SendAsync(Message("content:post:1", 11)); // duplicate
        await _hub.SendAsync(Message("content:post:1", 9));  // old
        await _hub.SendAsync(Message("content:post:1", 14)); // 12 and 13 were missed
        await _hub.SendAsync(Message("content:post:1", 15));
        await _hub.SendAsync(Message("content:post:1", null)); // no sequence: always delivered

        received.ShouldBe([11L, 15L, null]);
        _resyncs.ShouldBe([new LiveResync("content:post:1", 11, LiveResyncReason.Gap)]);
        own.ShouldBe(new LiveResync("content:post:1", 11, LiveResyncReason.Gap));
    }

    [Fact]
    public async Task A_refused_topic_throws_while_connected()
    {
        await StartAsync();
        _hub.Refuse["content:post:2"] = "forbidden";

        var error = await Should.ThrowAsync<LiveSubscriptionException>(() => _channel.SubscribeAsync("content:post:2", _ => Task.CompletedTask));
        error.Error.ShouldBe("forbidden");

        // ...and it is not joined again on the next connection.
        _hub.Joined.Clear();
        await _hub.DropAsync();
        await _channel.Connecting;
        _hub.Joined.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_topic_refused_on_connect_or_revoked_later_is_reported_to_its_subscriptions()
    {
        string? refused = null, revoked = null;
        _hub.Refuse["unit:desk"] = "forbidden";
        await using var desk = await _channel.SubscribeAsync("unit:desk", _ => Task.CompletedTask,
            options: new LiveSubscribeOptions { OnRevoked = e => { refused = e; return Task.CompletedTask; } });
        var received = 0;
        await using var post = await _channel.SubscribeAsync("content:post:1", _ => { received++; return Task.CompletedTask; },
            options: new LiveSubscribeOptions { OnRevoked = e => { revoked = e; return Task.CompletedTask; } });

        await StartAsync();
        refused.ShouldBe("forbidden");

        await _hub.RevokeAsync("content:post:1");
        revoked.ShouldBe("revoked");
        await _hub.SendAsync(Message("content:post:1", null));
        received.ShouldBe(0);
    }

    [Fact]
    public async Task Topics_are_shared_and_left_with_the_last_subscription()
    {
        await StartAsync();
        var first = await _channel.SubscribeAsync("user:alice", _ => Task.CompletedTask);
        var second = await _channel.SubscribeAsync("user:alice", _ => Task.CompletedTask);
        _hub.Joined.ShouldBe(["user:alice"]);

        await first.DisposeAsync();
        _hub.Left.ShouldBeEmpty();
        await second.DisposeAsync();
        _hub.Left.ShouldBe(["user:alice"]);
    }

    [Fact]
    public async Task Stopping_goes_offline_and_starting_again_joins_and_resyncs()
    {
        await StartAsync();
        await using var subscription = await _channel.SubscribeAsync("user:alice", _ => Task.CompletedTask, fromSequence: 1);
        _resyncs.Clear();

        await _channel.StopAsync(); // signed out
        _channel.State.ShouldBe(LiveState.Offline);
        _hub.Stops.ShouldBe(1);

        _hub.Joined.Clear();
        await StartAsync(); // signed in again
        _hub.Joined.ShouldBe(["user:alice"]);
        _resyncs.ShouldBe([new LiveResync("user:alice", 1, LiveResyncReason.Reconnected)]);
    }

    private sealed class FakeTransport : ILiveTransport
    {
        public Func<Exception?, Task>? Closed { get; set; }
        public Func<LiveMessage, Task>? Received { get; set; }
        public Func<string, Task>? Revoked { get; set; }

        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public int FailStarts { get; set; }
        public List<string> Joined { get; } = [];
        public List<string> Left { get; } = [];
        public Dictionary<string, IReadOnlyCollection<string>> Scopes { get; } = [];
        public Dictionary<string, string> Refuse { get; } = [];

        public Task StartAsync(CancellationToken ct)
        {
            Starts++;
            if (FailStarts-- > 0) throw new HttpRequestException("the hub is down");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct)
        {
            Stops++;
            return Task.CompletedTask;
        }

        public Task<LiveJoinResult> SubscribeAsync(string topic, IReadOnlyCollection<string> scopes, CancellationToken ct)
        {
            if (Refuse.TryGetValue(topic, out var error)) return Task.FromResult(new LiveJoinResult(false, error));
            Joined.Add(topic);
            Scopes[topic] = scopes;
            return Task.FromResult(new LiveJoinResult(true));
        }

        public Task UnsubscribeAsync(string topic, CancellationToken ct)
        {
            Left.Add(topic);
            return Task.CompletedTask;
        }

        public Task SendAsync(LiveMessage message) => Received!(message);

        public Task RevokeAsync(string topic) => Revoked!(topic);

        public Task DropAsync() => Closed!(new IOException("connection lost"));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

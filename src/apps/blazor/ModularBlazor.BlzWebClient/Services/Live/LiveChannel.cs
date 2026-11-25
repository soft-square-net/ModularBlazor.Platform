using Microsoft.Extensions.Logging;
using soft.square.net.ModularBlazor.Abstractions.Live;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services.Live;

/// <summary>The wire to the live hub (SignalR in the app, a fake in tests).</summary>
public interface ILiveTransport : IAsyncDisposable
{
    /// <summary>The connection dropped (not raised by a failed start).</summary>
    Func<Exception?, Task>? Closed { get; set; }

    Func<LiveMessage, Task>? Received { get; set; }

    /// <summary>The server dropped a topic: the user lost the right.</summary>
    Func<string, Task>? Revoked { get; set; }

    Task StartAsync(CancellationToken ct);

    Task StopAsync(CancellationToken ct);

    /// <returns>The server's answer: <c>Error</c> is set when it refused.</returns>
    Task<LiveJoinResult> SubscribeAsync(string topic, CancellationToken ct);

    Task UnsubscribeAsync(string topic, CancellationToken ct);
}

public sealed record LiveJoinResult(bool Ok, string? Error = null);

public sealed class LiveChannelOptions
{
    /// <summary>Waits before each connection attempt; the last one repeats.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];
}

/// <summary>
/// <see cref="ILiveChannel"/> over an <see cref="ILiveTransport"/>: subscriptions are kept here and joined on the
/// server whenever the transport connects; the connection is retried (<see cref="LiveChannelOptions.RetryDelays"/>)
/// for as long as the channel is started (the user is signed in). Sequences are tracked per subscription: duplicates
/// are dropped, a gap or a reconnect raises a resync.
/// </summary>
public sealed class LiveChannel : ILiveChannel, IAsyncDisposable
{
    private readonly ILiveTransport _transport;
    private readonly ILogger<LiveChannel> _log;
    private readonly LiveChannelOptions _options;
    private readonly Dictionary<string, TopicState> _topics = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1); // joins, leaves and (re)connects, one at a time
    private CancellationTokenSource? _run;
    private Task _loop = Task.CompletedTask;
    private bool _started;
    private bool _connected;
    private bool _everConnected;

    public LiveChannel(ILiveTransport transport, ILogger<LiveChannel> log, LiveChannelOptions? options = null)
    {
        _transport = transport;
        _log = log;
        _options = options ?? new LiveChannelOptions();
        _transport.Received = OnReceivedAsync;
        _transport.Revoked = OnRevokedAsync;
        _transport.Closed = OnClosedAsync;
    }

    public LiveState State { get; private set; } = LiveState.Offline;

    public event Action<LiveState>? StateChanged;

    public event Func<LiveResync, Task>? Resync;

    /// <summary>The current connection loop (tests wait on it).</summary>
    internal Task Connecting => _loop;

    /// <summary>Connects (and keeps reconnecting) until <see cref="StopAsync"/>. Call when the user signs in.</summary>
    public Task StartAsync()
    {
        if (_started) return Task.CompletedTask;
        _started = true;
        Connect();
        return Task.CompletedTask;
    }

    /// <summary>Disconnects and stays offline (the user signed out). Subscriptions are kept for the next start.</summary>
    public async Task StopAsync()
    {
        if (!_started) return;
        _started = false;
        _run?.Cancel();
        try { await _loop; } catch (OperationCanceledException) { }
        await _gate.WaitAsync();
        try
        {
            _connected = false;
            foreach (var topic in _topics.Values) topic.Joined = false;
            try { await _transport.StopAsync(CancellationToken.None); }
            catch (Exception e) { _log.LogDebug(e, "Stopping the live connection failed"); }
        }
        finally { _gate.Release(); }
        SetState(LiveState.Offline);
    }

    public async Task<IAsyncDisposable> SubscribeAsync(string topic, Func<LiveMessage, Task> handler, long? fromSequence = null,
        LiveSubscribeOptions? options = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = new Subscription(this, topic, handler, options ?? new LiveSubscribeOptions()) { Last = fromSequence };

        bool resync;
        await _gate.WaitAsync(ct);
        try
        {
            if (!_topics.TryGetValue(topic, out var state))
            {
                state = new TopicState();
                _topics[topic] = state;
            }
            state.Subscriptions.Add(subscription);

            if (_connected && !state.Joined)
            {
                LiveJoinResult result;
                try { result = await _transport.SubscribeAsync(topic, ct); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // The connection dropped meanwhile: the reconnect joins it.
                    _log.LogDebug(e, "Joining live topic {Topic} failed; it joins on reconnect", topic);
                    return subscription;
                }
                if (!result.Ok)
                {
                    _topics.Remove(topic);
                    throw new LiveSubscriptionException(topic, result.Error ?? "refused");
                }
                state.Joined = true;
            }

            subscription.Joined = state.Joined;
            resync = state.Joined && fromSequence is not null;
        }
        finally { _gate.Release(); }

        if (resync) await RaiseResyncAsync(subscription, LiveResyncReason.Subscribed);
        return subscription;
    }

    private void Connect()
    {
        _run?.Cancel();
        _run = new CancellationTokenSource();
        var token = _run.Token;
        _loop = Task.Run(() => ConnectLoopAsync(token), CancellationToken.None);
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        for (var attempt = 0; _started && !ct.IsCancellationRequested; attempt++)
        {
            SetState(LiveState.Reconnecting);
            var delay = _options.RetryDelays.Count == 0 ? TimeSpan.Zero : _options.RetryDelays[Math.Min(attempt, _options.RetryDelays.Count - 1)];
            try
            {
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
                await _transport.StartAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                _log.LogInformation("Live connection attempt {Attempt} failed: {Error}", attempt + 1, e.Message);
                continue;
            }

            var resyncs = new List<(Subscription, LiveResyncReason)>();
            await _gate.WaitAsync(ct);
            try
            {
                _connected = true;
                var reconnected = _everConnected;
                _everConnected = true;
                foreach (var (topic, state) in _topics.ToArray())
                {
                    if (!await JoinAsync(topic, state, ct)) continue;
                    foreach (var subscription in state.Subscriptions)
                    {
                        // Joined before: anything may have been missed while away. New: catch up from the page's sequence.
                        if (subscription.Joined || reconnected) resyncs.Add((subscription, LiveResyncReason.Reconnected));
                        else if (subscription.Last is not null) resyncs.Add((subscription, LiveResyncReason.Subscribed));
                        subscription.Joined = true;
                    }
                }
            }
            finally { _gate.Release(); }

            // Closed meanwhile: OnClosed started the next round (and cancelled this one).
            if (!_connected || ct.IsCancellationRequested) return;
            SetState(LiveState.Connected);
            foreach (var (subscription, reason) in resyncs) await RaiseResyncAsync(subscription, reason);
            return;
        }
    }

    /// <summary>Joins one topic on a fresh connection; false when the server refused it (dropped and reported).</summary>
    private async Task<bool> JoinAsync(string topic, TopicState state, CancellationToken ct)
    {
        LiveJoinResult result;
        try { result = await _transport.SubscribeAsync(topic, ct); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Dropped while joining (OnClosed starts the next round) or a server error: it stays unjoined until then.
            _log.LogInformation("Joining live topic {Topic} failed: {Error}", topic, e.Message);
            return false;
        }
        if (result.Ok)
        {
            state.Joined = true;
            return true;
        }

        _topics.Remove(topic);
        _ = ReportRevokedAsync(state, result.Error ?? "refused");
        return false;
    }

    private async Task OnClosedAsync(Exception? error)
    {
        await _gate.WaitAsync();
        try
        {
            _connected = false;
            foreach (var topic in _topics.Values) topic.Joined = false;
        }
        finally { _gate.Release(); }

        if (!_started) return;
        _log.LogInformation("Live connection closed ({Error}); reconnecting", error?.Message ?? "no error");
        Connect();
    }

    private async Task OnReceivedAsync(LiveMessage message)
    {
        if (!_topics.TryGetValue(message.Topic, out var state)) return;
        foreach (var subscription in state.Subscriptions.ToArray())
        {
            if (subscription.Disposed) continue;
            if (message.Sequence is { } sequence && subscription.Last is { } last)
            {
                if (sequence <= last) continue; // already seen
                if (sequence > last + 1)
                {
                    subscription.Last = sequence;
                    await RaiseResyncAsync(subscription, LiveResyncReason.Gap, last);
                    continue; // the catch-up from `last` includes this one
                }
            }
            if (message.Sequence is not null) subscription.Last = message.Sequence;

            try { await subscription.Handler(message); }
            catch (Exception e) { _log.LogWarning(e, "A live message handler of {Topic} failed", message.Topic); }
        }
    }

    private async Task OnRevokedAsync(string topic)
    {
        TopicState? state;
        await _gate.WaitAsync();
        try
        {
            if (!_topics.Remove(topic, out state)) return;
        }
        finally { _gate.Release(); }
        await ReportRevokedAsync(state, "revoked");
    }

    private async Task ReportRevokedAsync(TopicState state, string error)
    {
        foreach (var subscription in state.Subscriptions.ToArray())
        {
            subscription.Disposed = true;
            if (subscription.Options.OnRevoked is not { } revoked) continue;
            try { await revoked(error); }
            catch (Exception e) { _log.LogWarning(e, "A live revocation handler of {Topic} failed", subscription.Topic); }
        }
    }

    private async Task RaiseResyncAsync(Subscription subscription, LiveResyncReason reason, long? from = null)
    {
        var resync = new LiveResync(subscription.Topic, reason == LiveResyncReason.Gap ? from : subscription.Last, reason);
        try
        {
            if (subscription.Options.OnResync is { } own) await own(resync);
            if (Resync is { } all)
                foreach (var handler in all.GetInvocationList().Cast<Func<LiveResync, Task>>())
                    await handler(resync);
        }
        catch (Exception e) { _log.LogWarning(e, "A live resync handler of {Topic} failed", subscription.Topic); }
    }

    private async Task LeaveAsync(Subscription subscription)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_topics.TryGetValue(subscription.Topic, out var state) || !state.Subscriptions.Remove(subscription)) return;
            if (state.Subscriptions.Count > 0) return;
            _topics.Remove(subscription.Topic);
            if (!_connected || !state.Joined) return;
            try { await _transport.UnsubscribeAsync(subscription.Topic, CancellationToken.None); }
            catch (Exception e) { _log.LogDebug(e, "Leaving live topic {Topic} failed", subscription.Topic); }
        }
        finally { _gate.Release(); }
    }

    private void SetState(LiveState state)
    {
        if (State == state) return;
        State = state;
        try { StateChanged?.Invoke(state); }
        catch (Exception e) { _log.LogWarning(e, "A live state handler failed"); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _transport.DisposeAsync();
    }

    private sealed class TopicState
    {
        public List<Subscription> Subscriptions { get; } = [];
        public bool Joined { get; set; }
    }

    private sealed class Subscription(LiveChannel channel, string topic, Func<LiveMessage, Task> handler, LiveSubscribeOptions options) : IAsyncDisposable
    {
        public string Topic { get; } = topic;
        public Func<LiveMessage, Task> Handler { get; } = handler;
        public LiveSubscribeOptions Options { get; } = options;
        public long? Last { get; set; }

        /// <summary>Joined on the server at least once (a later join is a reconnect).</summary>
        public bool Joined { get; set; }

        public bool Disposed { get; set; }

        public async ValueTask DisposeAsync()
        {
            if (Disposed) return;
            Disposed = true;
            await channel.LeaveAsync(this);
        }
    }
}

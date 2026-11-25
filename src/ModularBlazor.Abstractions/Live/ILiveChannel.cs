using System.Text.Json;

namespace soft.square.net.ModularBlazor.Abstractions.Live;

/// <summary>Where the live connection stands.</summary>
public enum LiveState
{
    /// <summary>Not connected: signed out, or not started yet. Subscriptions wait and join when it connects.</summary>
    Offline,

    /// <summary>Connecting, or the connection dropped and the channel is trying again. Messages may be missed meanwhile.</summary>
    Reconnecting,

    /// <summary>Connected; every subscription is joined.</summary>
    Connected
}

/// <summary>Why a subscription must catch up from its module's feed.</summary>
public enum LiveResyncReason
{
    /// <summary>The subscription was just joined with a known sequence: fetch what came after it meanwhile.</summary>
    Subscribed,

    /// <summary>The connection came back: anything may have been missed while it was down.</summary>
    Reconnected,

    /// <summary>A message skipped sequences: the ones in between were missed.</summary>
    Gap
}

/// <summary>
/// A live message. <see cref="Payload"/> is small JSON (an id, a version, a status); read the data itself through the
/// module's endpoints.
/// </summary>
/// <param name="Sequence">The topic's position in the module's feed, or null when the publisher gives none.</param>
public sealed record LiveMessage(string Topic, string Type, JsonElement? Payload, long? Sequence, DateTimeOffset At)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The payload as <typeparamref name="T"/> (web defaults: camelCase), or default when there is none.</summary>
    public T? Read<T>(JsonSerializerOptions? options = null) =>
        Payload is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } payload
            ? payload.Deserialize<T>(options ?? Web)
            : default;
}

/// <summary>
/// A subscription must catch up: fetch the module's feed after <paramref name="LastSequence"/> (<c>?after=</c>), or
/// reload when it is null.
/// </summary>
public sealed record LiveResync(string Topic, long? LastSequence, LiveResyncReason Reason);

/// <summary>Options of one subscription.</summary>
public sealed class LiveSubscribeOptions
{
    /// <summary>
    /// The resource's scopes as the page knows them (its press, category, the unit's ancestors: <c>unit:{id}</c>), for
    /// topics whose permission may be held there rather than on the resource itself.
    /// </summary>
    public IReadOnlyCollection<string>? Scopes { get; init; }

    /// <summary>Called when this subscription must catch up (besides <see cref="ILiveChannel.Resync"/>).</summary>
    public Func<LiveResync, Task>? OnResync { get; init; }

    /// <summary>Called when the server refused or dropped the topic (the user lost the right), with the reason.</summary>
    public Func<string, Task>? OnRevoked { get; init; }
}

/// <summary>The server refused a subscription: <c>forbidden</c>, <c>unknown-topic</c>, <c>invalid-topic</c>, <c>too-many-topics</c>.</summary>
public sealed class LiveSubscriptionException(string topic, string error)
    : Exception($"The live topic '{topic}' was refused: {error}.")
{
    public string Topic { get; } = topic;
    public string Error { get; } = error;
}

/// <summary>
/// The platform's live channel (the WebApi's live hub, through the gateway), one per app. It connects while the user is
/// signed in, reconnects on its own and joins every subscription again; after a reconnect or a gap in the sequence it
/// raises <see cref="Resync"/> so the module catches up from its own feed.
/// <para>Topics (never naming the tenant: the token's tenant applies):</para>
/// <list type="bullet">
/// <item><c>user:{id}</c>: the signed-in user's own messages;</item>
/// <item><c>unit:{id}</c>: an organizational unit and everything below it;</item>
/// <item><c>{moduleId}:{kind}:{key}</c>: a module's resource, e.g. <c>content:post:42</c> (the module declares the kind
/// and its permission in its manifest's <c>LiveTopics</c>).</item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// _live = await Live.SubscribeAsync($"content:post:{Id}", OnMessageAsync, fromSequence: _post.Sequence,
///     new LiveSubscribeOptions { OnResync = r =&gt; CatchUpAsync(r.LastSequence) });
/// // ...and in DisposeAsync: await _live.DisposeAsync();
/// </code>
/// </example>
public interface ILiveChannel
{
    LiveState State { get; }

    /// <summary>Raised when <see cref="State"/> changes.</summary>
    event Action<LiveState>? StateChanged;

    /// <summary>Raised for every subscription that must catch up (see <see cref="LiveSubscribeOptions.OnResync"/>).</summary>
    event Func<LiveResync, Task>? Resync;

    /// <summary>
    /// Subscribes to a topic; dispose the result to leave it. Several subscriptions to a topic share one on the server.
    /// While the channel is offline the subscription waits and joins when it connects.
    /// </summary>
    /// <param name="handler">Called for each message, in order; duplicates (a sequence already seen) are dropped.</param>
    /// <param name="fromSequence">The sequence the page's data is at: the channel raises a resync from it once joined,
    /// and on a gap after it.</param>
    /// <exception cref="LiveSubscriptionException">The channel is connected and the server refused the topic.</exception>
    Task<IAsyncDisposable> SubscribeAsync(string topic, Func<LiveMessage, Task> handler, long? fromSequence = null,
        LiveSubscribeOptions? options = null, CancellationToken ct = default);
}

using System.Text.Json;

namespace soft.square.net.WebApi.Contracts.Live;

/// <summary>
/// A kind of live topic a module offers: <c>{moduleId}:{Kind}:{key}</c> (e.g. <c>content:post:42</c> for
/// <c>new LiveTopic("post", "content.view")</c> in the module "content"). Declared in
/// <see cref="ModuleManifest.LiveTopics"/>: the host reads it at install and authorizes subscriptions with it, so no
/// module type is ever involved in the check.
/// </summary>
/// <param name="Kind">Lowercase name of the topic kind (a-z, 0-9, '-').</param>
/// <param name="Permission">The permission a subscriber must hold for the resource (see <see cref="Security.IAccessScopes"/>).</param>
public sealed record LiveTopic(string Kind, string Permission)
{
    /// <summary>
    /// True (default): the scopes the client sends with the subscription (the resource's press, category, unit and its
    /// ancestors, ...) count, besides the topic's own scope <c>{Kind}:{key}</c>. They come from the client, so a module
    /// that sets this must treat live messages as hints and keep its data behind its authorized endpoints (the client
    /// re-reads them). False: only the topic scope and tenant-wide grants count.
    /// </summary>
    public bool AcceptClientScopes { get; init; } = true;
}

/// <summary>
/// Topic names. A topic is scoped to the caller's tenant by the hub; it never names the tenant.
/// <list type="bullet">
/// <item><c>user:{id}</c>: messages for one user; only that user may subscribe.</item>
/// <item><c>unit:{id}</c>: messages about an organizational unit; any permission held for the unit (or its ancestors,
/// sent by the client as scopes) allows it. Publishing to a unit path fans out to every ancestor.</item>
/// <item><c>{moduleId}:{kind}:{key}</c>: a module's resource, authorized by its <see cref="LiveTopic"/>.</item>
/// </list>
/// </summary>
public static class LiveTopics
{
    public const string UserKind = "user";
    public const string UnitKind = "unit";

    /// <summary>The separator of the ids in a unit path, from the root to the unit: <c>root/region/desk</c>.</summary>
    public const char UnitPathSeparator = '/';

    public static string User(string userId) => $"{UserKind}:{userId}";

    public static string Unit(string unitId) => $"{UnitKind}:{unitId}";

    public static string Of(string moduleId, string kind, string key) => $"{moduleId}:{kind}:{key}";

    /// <summary>The unit topics a message on <paramref name="unitPath"/> reaches: the unit, then each ancestor up to the root.</summary>
    public static IReadOnlyList<string> UnitAndAncestors(string unitPath) =>
        unitPath.Split(UnitPathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Reverse()
            .Select(Unit)
            .ToArray();
}

/// <summary>
/// Sends live messages to the clients subscribed to a topic, on every replica (RabbitMQ fanout <c>live</c>; locally
/// without a broker). Scoped: the tenant is the current scope's (a request or <c>CreateTenantScope</c>). Messages are
/// best effort: a client that missed some (offline, reconnecting) catches up from the module's own feed using the
/// sequence, so publish after the change is committed and give each topic's messages an increasing sequence.
/// </summary>
/// <remarks>
/// The payload is JSON the module has already serialized with its own options
/// (<c>JsonSerializer.SerializeToElement(value, options)</c>), so the host never sees a module type.
/// </remarks>
public interface ILiveNotifier
{
    /// <param name="topic">See <see cref="LiveTopics"/>.</param>
    /// <param name="type">What happened, e.g. <c>post.updated</c>; the client dispatches on it.</param>
    /// <param name="payload">Small: an id, a version, a status. Clients re-read data through the module's endpoints.</param>
    /// <param name="sequence">The topic's position in the module's feed (the client resyncs on a gap), or null.</param>
    Task PublishAsync(string topic, string type, JsonElement? payload = null, long? sequence = null, CancellationToken ct = default);

    /// <summary>Publishes to the unit at the end of <paramref name="unitPath"/> and to each of its ancestors.</summary>
    /// <param name="unitPath">Unit ids from the root, separated by <see cref="LiveTopics.UnitPathSeparator"/>.</param>
    Task PublishToUnitAsync(string unitPath, string type, JsonElement? payload = null, long? sequence = null, CancellationToken ct = default);
}

/// <summary>What a client receives (hub method <see cref="LiveHubMethods.Message"/>).</summary>
public sealed record LiveEnvelope(string Topic, string Type, JsonElement? Payload, long? Sequence, DateTimeOffset At);

/// <summary>The answer to a subscription.</summary>
/// <param name="Error">Why it was refused: <c>forbidden</c>, <c>unknown-topic</c> or <c>invalid-topic</c>.</param>
public sealed record LiveSubscribeResult(bool Ok, string? Error = null);

/// <summary>Names on the wire between the hub (<see cref="Path"/>) and its clients.</summary>
public static class LiveHubMethods
{
    public const string Path = "/hubs/live";

    /// <summary>Server: <c>Subscribe(string topic, string[]? scopes) → LiveSubscribeResult</c>.</summary>
    public const string Subscribe = "Subscribe";

    /// <summary>Server: <c>Unsubscribe(string topic)</c>.</summary>
    public const string Unsubscribe = "Unsubscribe";

    /// <summary>Client: <c>Message(LiveEnvelope)</c>.</summary>
    public const string Message = "Message";

    /// <summary>Client: <c>Revoked(string topic)</c>, the subscription was dropped because the caller lost the right.</summary>
    public const string Revoked = "Revoked";
}

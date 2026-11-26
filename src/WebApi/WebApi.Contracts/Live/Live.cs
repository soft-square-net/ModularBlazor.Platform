using System.Text.Json;
using System.Text.Json.Serialization;

namespace soft.square.net.WebApi.Contracts.Live;

/// <summary>
/// A kind of live topic a module offers: <c>{moduleId}:{Kind}:{key}</c> (e.g. <c>content:post:42</c> for
/// <c>new LiveTopic("post", "content.view")</c> in the module "content"). Declared in
/// <see cref="ModuleManifest.LiveTopics"/>: the host reads it at install and authorizes subscriptions with it, so no
/// module type is ever involved in the decision. A subscriber needs <paramref name="Permission"/> for the scope
/// <c>{Kind}:{key}</c>, or for one of the scopes the module's <see cref="ILiveScopeResolver"/> gives for the resource
/// (its press, category, parent units). Scopes sent by clients never count.
/// <para>The kind <see cref="LiveTopics.UnitKind"/> is special: the one active module that declares it (Organization)
/// provides the permission for the platform's <c>unit:{id}</c> topics, and its resolver gives a unit's ancestors.
/// Without such a module, <c>unit:</c> topics are refused.</para>
/// <para>A resource visible through several permissions declares them all with <see cref="AnyOf"/>: holding any one of
/// them for the resource is enough. A module declares each kind once; a manifest that declares a kind twice is refused at
/// install.</para>
/// </summary>
/// <param name="Kind">Lowercase name of the topic kind (a-z, 0-9, '-').</param>
/// <param name="Permission">The permission a subscriber must hold for the resource (see <see cref="Security.IAccessScopes"/>).</param>
public sealed record LiveTopic(string Kind, string Permission)
{
    /// <summary>
    /// Further permissions, each of which is enough on its own, besides <see cref="Permission"/>. Empty for a topic with
    /// one permission. Set it with <see cref="AnyOf"/>.
    /// </summary>
    public IReadOnlyList<string> OrPermissions { get; init; } = [];

    /// <summary>Every permission that is enough to subscribe: <see cref="Permission"/>, then <see cref="OrPermissions"/>.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Permissions => [Permission, .. OrPermissions ?? []];

    /// <summary>
    /// A topic kind a subscriber may receive with any one of <paramref name="permissions"/> for the resource, e.g.
    /// <c>LiveTopic.AnyOf("post", "content.author", "content.accept", "content.publish")</c>.
    /// </summary>
    public static LiveTopic AnyOf(string kind, params IEnumerable<string> permissions)
    {
        var all = permissions.Distinct(StringComparer.Ordinal).ToArray();
        if (all.Length == 0) throw new ArgumentException("Name at least one permission.", nameof(permissions));
        return new LiveTopic(kind, all[0]) { OrPermissions = all[1..] };
    }

    public bool Equals(LiveTopic? other) =>
        other is not null && Kind == other.Kind && Permissions.SequenceEqual(other.Permissions, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Kind, string.Join('\n', Permissions));
}

/// <summary>
/// The scopes of a live topic's resource, beyond its own <c>{kind}:{key}</c>: e.g. a post's <c>press:A</c> and
/// <c>category:sports</c>, or a unit's ancestors <c>unit:{parent}</c>... up to the root. Optional; a module registers it in
/// its own services. The host resolves it per subscription from the declaring module's container, in a scope of the
/// subscriber's tenant, and keeps nothing of it afterwards (the module stays unloadable). Without one, only the exact
/// scope counts. Unknown keys: return no scopes.
/// </summary>
public interface ILiveScopeResolver
{
    Task<IReadOnlyList<string>> ScopesAsync(string kind, string key, CancellationToken ct);
}

/// <summary>
/// Topic names. A topic is scoped to the caller's tenant by the hub; it never names the tenant.
/// <list type="bullet">
/// <item><c>user:{id}</c>: messages for one user; only that user may subscribe.</item>
/// <item><c>unit:{id}</c>: messages about an organizational unit; the permission of the <see cref="LiveTopic"/> of kind
/// <c>unit</c> its providing module declares, held for the unit or one of its ancestors (from that module's
/// <see cref="ILiveScopeResolver"/>). Publishing to a unit path fans out to every ancestor.</item>
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
/// <param name="Error">Why it was refused: <c>forbidden</c>, <c>unknown-topic</c>, <c>invalid-topic</c> or <c>too-many-topics</c>.</param>
public sealed record LiveSubscribeResult(bool Ok, string? Error = null);

/// <summary>Names on the wire between the hub (<see cref="Path"/>) and its clients.</summary>
public static class LiveHubMethods
{
    public const string Path = "/hubs/live";

    /// <summary>Server: <c>Subscribe(string topic) → LiveSubscribeResult</c>.</summary>
    public const string Subscribe = "Subscribe";

    /// <summary>Server: <c>Unsubscribe(string topic)</c>.</summary>
    public const string Unsubscribe = "Unsubscribe";

    /// <summary>Client: <c>Message(LiveEnvelope)</c>.</summary>
    public const string Message = "Message";

    /// <summary>Client: <c>Revoked(string topic)</c>, the subscription was dropped because the caller lost the right.</summary>
    public const string Revoked = "Revoked";
}

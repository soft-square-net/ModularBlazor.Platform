using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace soft.square.net.WebApi.Contracts.Messaging;

/// <summary>
/// An integration event as it travels between modules: plain data, never a module type. The body is JSON
/// (<see cref="Data"/>); each subscriber deserializes it into its own copy of the event record, so no module ever
/// shares a type with another one (and the host holds none of them).
/// </summary>
/// <param name="Id">Unique per event; the subscriber's inbox drops a second delivery of the same id.</param>
/// <param name="Type">Lowercase, dot-separated, versioned: <c>{module}.{event}.v{n}</c>, e.g. <c>shifts.checked-in.v1</c>.</param>
/// <param name="TenantId">The tenant the event happened in; it is handled in a scope of the same tenant.</param>
/// <param name="Subject">What the event is about, e.g. the aggregate id ("post:42").</param>
/// <param name="OrgId">The organization (customer), when the publisher knows it.</param>
/// <param name="UnitPath">The organization unit path ("/org/sector/dept"), when the event belongs to a unit.</param>
/// <param name="CorrelationId">Shared by an event and the events its handlers publish.</param>
/// <param name="Source">Id of the module that published it.</param>
public sealed record IntegrationEnvelope(
    Guid Id,
    string Type,
    string TenantId,
    string Subject,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string Source,
    JsonElement Data,
    string? OrgId = null,
    string? UnitPath = null)
{
    /// <summary>The JSON options of envelopes and their data: web defaults (camelCase).</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>The event's data as the subscriber's own record type.</summary>
    public T DataAs<T>() => Data.Deserialize<T>(JsonOptions)
        ?? throw new JsonException($"Integration event {Id} ({Type}) has no data.");

    public byte[] ToUtf8Bytes() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

    public static IntegrationEnvelope Parse(ReadOnlySpan<byte> utf8) =>
        JsonSerializer.Deserialize<IntegrationEnvelope>(utf8, JsonOptions)
        ?? throw new JsonException("Empty integration envelope.");

    public static IntegrationEnvelope Parse(string json) =>
        JsonSerializer.Deserialize<IntegrationEnvelope>(json, JsonOptions)
        ?? throw new JsonException("Empty integration envelope.");
}

/// <summary>
/// Publishes integration events from a module (scoped; registered by <c>AddIntegrationEvents&lt;TDbContext&gt;()</c>).
/// <see cref="Publish"/> only adds an outbox row to the module's DbContext: the event is sent once that DbContext's
/// SaveChanges (or its transaction) commits, and never when it rolls back.
/// </summary>
public interface IIntegrationEvents
{
    /// <param name="type">The event type, e.g. <c>content.post-published.v1</c> (see <see cref="IntegrationTypes"/>).</param>
    /// <param name="data">Serialized to JSON (web defaults).</param>
    /// <param name="subject">What the event is about, e.g. "post:42".</param>
    /// <param name="unitPath">The organization unit path, when the event belongs to a unit.</param>
    /// <param name="orgId">The organization (customer), when known.</param>
    /// <returns>The envelope that will be sent.</returns>
    IntegrationEnvelope Publish(string type, object data, string subject, string? unitPath = null, string? orgId = null);
}

/// <summary>
/// Handles the integration events a module subscribes to. Register with
/// <see cref="IntegrationServiceCollectionExtensions.AddIntegrationHandler{THandler}"/>; the module's manifest lists the
/// same patterns in <see cref="ModuleManifest.Subscriptions"/> so the host binds its queue without loading it.
/// <para>A handler runs in a tenant scope of the module's container, inside one transaction with the inbox row that
/// marks the event handled: changes it makes through the module's DbContext are saved by the platform (do not call
/// SaveChanges), and they are rolled back with the inbox row when the handler throws. A failed event is retried, then
/// dead-lettered.</para>
/// </summary>
public interface IIntegrationEventHandler
{
    Task HandleAsync(IntegrationEnvelope envelope, CancellationToken ct);
}

/// <summary>A handler type registered for an event type pattern (module container; data only for the host).</summary>
public sealed record IntegrationHandlerRegistration(string Pattern, Type HandlerType);

public static class IntegrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="THandler"/> (scoped) for the event types matching <paramref name="pattern"/>
    /// (see <see cref="IntegrationTypes.Matches"/>). List the pattern in the manifest's
    /// <see cref="ModuleManifest.Subscriptions"/> as well.
    /// </summary>
    public static IServiceCollection AddIntegrationHandler<THandler>(this IServiceCollection services, string pattern)
        where THandler : class, IIntegrationEventHandler
    {
        IntegrationTypes.ValidatePattern(pattern);
        services.AddScoped<THandler>();
        services.AddSingleton(new IntegrationHandlerRegistration(pattern, typeof(THandler)));
        return services;
    }
}

/// <summary>
/// Event types and subscription patterns. A type is lowercase words separated by dots (<c>org.unit-created.v1</c>). In a
/// pattern, <c>*</c> inside a word matches any characters except a dot (<c>org.unit-*.v1</c>, <c>shifts.*.v1</c>), and
/// a word that is exactly <c>#</c> matches zero or more words (<c>org.#</c>).
/// </summary>
public static partial class IntegrationTypes
{
    public static bool IsValidType(string type) => TypeText().IsMatch(type);

    public static void ValidateType(string type)
    {
        if (!IsValidType(type))
            throw new ArgumentException($"Invalid integration event type '{type}': use lowercase words separated by dots, e.g. 'shifts.checked-in.v1'.", nameof(type));
    }

    public static bool IsValidPattern(string pattern) => PatternText().IsMatch(pattern);

    public static void ValidatePattern(string pattern)
    {
        if (!IsValidPattern(pattern))
            throw new ArgumentException($"Invalid integration event pattern '{pattern}': lowercase words separated by dots, '*' inside a word, '#' as a whole word.", nameof(pattern));
    }

    /// <summary>True when <paramref name="type"/> matches <paramref name="pattern"/>.</summary>
    public static bool Matches(string pattern, string type) => Match(pattern.Split('.'), 0, type.Split('.'), 0);

    /// <summary>
    /// The broker binding key of a pattern: words with a wildcard inside become <c>*</c> (a whole word), so the binding
    /// may be wider than the pattern; the consumer filters with <see cref="Matches"/>.
    /// </summary>
    public static string ToBindingKey(string pattern) =>
        string.Join('.', pattern.Split('.').Select(word => word is "#" ? "#" : word.Contains('*') ? "*" : word));

    private static bool Match(string[] pattern, int p, string[] type, int t)
    {
        if (p == pattern.Length) return t == type.Length;
        if (pattern[p] == "#")
        {
            for (var skip = t; skip <= type.Length; skip++)
                if (Match(pattern, p + 1, type, skip)) return true;
            return false;
        }
        return t < type.Length && WordMatches(pattern[p], type[t]) && Match(pattern, p + 1, type, t + 1);
    }

    private static bool WordMatches(string pattern, string word)
    {
        if (!pattern.Contains('*')) return string.Equals(pattern, word, StringComparison.Ordinal);
        var parts = pattern.Split('*');
        if (!word.StartsWith(parts[0], StringComparison.Ordinal)) return false;
        var position = parts[0].Length;
        for (var i = 1; i < parts.Length - 1; i++)
        {
            var found = word.IndexOf(parts[i], position, StringComparison.Ordinal);
            if (found < 0) return false;
            position = found + parts[i].Length;
        }
        var last = parts[^1];
        return word.Length - position >= last.Length && word.EndsWith(last, StringComparison.Ordinal);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)*$")]
    private static partial Regex TypeText();

    [GeneratedRegex("^(#|[a-z0-9_*-]+)(\\.(#|[a-z0-9_*-]+))*$")]
    private static partial Regex PatternText();
}

/// <summary>Names the host and modules agree on (broker topology, headers).</summary>
public static class IntegrationNames
{
    /// <summary>Topic exchange every integration event is published to; the routing key is the event type.</summary>
    public const string Exchange = "integration";

    /// <summary>Direct exchange that receives the events a module gave up on (routing key: the module's queue).</summary>
    public const string DeadLetterExchange = "integration.dlx";

    /// <summary>Message header with the event's tenant (same name as other tenant-bearing messages).</summary>
    public const string TenantHeader = Tenancy.TenancyNames.MessageHeader;

    /// <summary>Message header with the delivery attempt (1 on the first retry).</summary>
    public const string AttemptHeader = "x-integration-attempt";

    /// <summary>Message header with the last error of a retried or dead-lettered event.</summary>
    public const string ErrorHeader = "x-integration-error";

    /// <summary>The durable queue of a module in a pool; the pool's replicas are its competing consumers.</summary>
    public static string Queue(string moduleId, string pool) => $"integration.{moduleId}.{pool}";

    public static string RetryQueue(string queue) => queue + ".retry";

    public static string DeadQueue(string queue) => queue + ".dead";
}

/// <summary>An outgoing integration event, as bytes for the broker.</summary>
public sealed record IntegrationMessage(Guid Id, string Type, string TenantId, ReadOnlyMemory<byte> Body);

/// <summary>
/// The host's broker publisher (a host singleton): publishes to the topic exchange <see cref="IntegrationNames.Exchange"/>
/// with the event type as routing key and the tenant in a header. Without a broker, an in-process bus delivers to the
/// subscribers loaded on the same process. Modules use <see cref="IIntegrationEvents"/>; this is what the outbox calls.
/// </summary>
public interface IBrokerPublisher
{
    /// <summary>Completes once the broker has the message (publisher confirm); throws when it could not take it.</summary>
    Task PublishAsync(IntegrationMessage message, CancellationToken ct);
}

public enum IntegrationProcessResult
{
    /// <summary>The handlers ran and the inbox row was written.</summary>
    Handled,
    /// <summary>The inbox already had the event: nothing ran.</summary>
    Duplicate,
    /// <summary>No handler of the module matches the type; the event is marked handled.</summary>
    NoHandler
}

/// <summary>
/// The module side of delivery (registered by <c>AddIntegrationEvents</c>, resolved by the host from a tenant scope of
/// the module's container for each message): checks the inbox, runs the matching handlers and saves the inbox row with
/// their changes.
/// </summary>
public interface IIntegrationMessageProcessor
{
    Task<IntegrationProcessResult> ProcessAsync(IntegrationEnvelope envelope, CancellationToken ct);
}

/// <summary>The event being handled in the current scope (scoped); events published while handling it share its correlation.</summary>
public interface IIntegrationEventContext
{
    IntegrationEnvelope? Current { get; }
}

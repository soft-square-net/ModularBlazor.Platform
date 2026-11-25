using System.Collections.Concurrent;
using Carter;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Contracts.Security;

namespace soft.square.net.WebApi.Messaging;

/// <summary>A module's dead-letter queue in this replica's pool.</summary>
public sealed record DeadLetterQueue(string ModuleId, string Queue, long Count);

/// <summary>An event a module gave up on.</summary>
public sealed record DeadLetter(Guid? Id, string? Type, string? TenantId, DateTimeOffset? OccurredAt, int Attempts, string? Error);

/// <summary>The events subscriber modules gave up on, for this replica's pool.</summary>
internal interface IDeadLetters
{
    Task<IReadOnlyList<DeadLetterQueue>> ListAsync(CancellationToken ct);

    /// <summary>The first <paramref name="take"/> dead letters of a module, left in place.</summary>
    Task<IReadOnlyList<DeadLetter>> PeekAsync(string moduleId, int take, CancellationToken ct);

    /// <summary>Delivers every dead letter of the module again, with a fresh attempt count. Returns how many.</summary>
    Task<int> ReplayAsync(string moduleId, CancellationToken ct);
}

/// <summary>The <c>….dead</c> queues of the modules whose queues this replica declared.</summary>
internal sealed class RabbitMqDeadLetters(IConnection connection, IntegrationTopology topology) : IDeadLetters
{
    public async Task<IReadOnlyList<DeadLetterQueue>> ListAsync(CancellationToken ct)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        var queues = new List<DeadLetterQueue>();
        foreach (var module in topology.Modules)
        {
            var dead = IntegrationNames.DeadQueue(topology.QueueOf(module));
            var info = await channel.QueueDeclarePassiveAsync(dead, ct);
            queues.Add(new DeadLetterQueue(module, dead, info.MessageCount));
        }
        return queues;
    }

    public async Task<IReadOnlyList<DeadLetter>> PeekAsync(string moduleId, int take, CancellationToken ct)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        var dead = IntegrationNames.DeadQueue(topology.QueueOf(moduleId));
        var letters = new List<DeadLetter>();
        ulong last = 0;
        while (letters.Count < take && await channel.BasicGetAsync(dead, autoAck: false, ct) is { } message)
        {
            last = message.DeliveryTag;
            letters.Add(Describe(message.Body.Span, message.BasicProperties.Headers));
        }
        if (last > 0) await channel.BasicNackAsync(last, multiple: true, requeue: true, ct); // put them back, in order
        return letters;
    }

    public async Task<int> ReplayAsync(string moduleId, CancellationToken ct)
    {
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        var queue = topology.QueueOf(moduleId);
        var dead = IntegrationNames.DeadQueue(queue);
        var count = (await channel.QueueDeclarePassiveAsync(dead, ct)).MessageCount; // not the ones that fail again meanwhile
        var replayed = 0;
        while (replayed < count && await channel.BasicGetAsync(dead, autoAck: false, ct) is { } message)
        {
            var headers = message.BasicProperties.Headers is { } existing ? new Dictionary<string, object?>(existing) : [];
            headers.Remove(IntegrationNames.AttemptHeader);
            headers.Remove("x-death");
            var properties = new BasicProperties(message.BasicProperties) { Headers = headers, Persistent = true, Expiration = null };
            await channel.BasicPublishAsync("", queue, mandatory: false, properties, message.Body.ToArray(), ct);
            await channel.BasicAckAsync(message.DeliveryTag, multiple: false, ct);
            replayed++;
        }
        return replayed;
    }

    private static DeadLetter Describe(ReadOnlySpan<byte> body, IDictionary<string, object?>? headers)
    {
        IntegrationEnvelope? envelope = null;
        try { envelope = IntegrationEnvelope.Parse(body); }
        catch (System.Text.Json.JsonException) { }
        return new DeadLetter(envelope?.Id, envelope?.Type, envelope?.TenantId, envelope?.OccurredAt,
            IntegrationHeaders.Attempt(headers), IntegrationHeaders.Text(headers, IntegrationNames.ErrorHeader));
    }
}

/// <summary>Dead letters of the in-process bus (no broker): in memory, the most recent 1000 per module.</summary>
internal sealed class LocalDeadLetters(IServiceProvider services, IOptions<Modularity.ModuleRuntimeOptions> runtime) : IDeadLetters
{
    private const int Capacity = 1000;
    private readonly ConcurrentDictionary<string, List<(IntegrationEnvelope Envelope, DeadLetter Letter)>> _letters = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string moduleId, IntegrationEnvelope envelope, int attempts, string error)
    {
        var list = _letters.GetOrAdd(moduleId, _ => []);
        lock (list)
        {
            list.Add((envelope, new DeadLetter(envelope.Id, envelope.Type, envelope.TenantId, envelope.OccurredAt, attempts, error)));
            if (list.Count > Capacity) list.RemoveAt(0);
        }
    }

    public Task<IReadOnlyList<DeadLetterQueue>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DeadLetterQueue>>(_letters
            .Select(p => { lock (p.Value) return new DeadLetterQueue(p.Key, IntegrationNames.DeadQueue(IntegrationNames.Queue(p.Key, runtime.Value.Pool)), p.Value.Count); })
            .OrderBy(q => q.ModuleId).ToArray());

    public Task<IReadOnlyList<DeadLetter>> PeekAsync(string moduleId, int take, CancellationToken ct)
    {
        if (!_letters.TryGetValue(moduleId, out var list)) return Task.FromResult<IReadOnlyList<DeadLetter>>([]);
        lock (list) return Task.FromResult<IReadOnlyList<DeadLetter>>(list.Take(take).Select(l => l.Letter).ToArray());
    }

    public Task<int> ReplayAsync(string moduleId, CancellationToken ct)
    {
        var module = services.GetRequiredService<IntegrationConsumers>().Subscribers
            .FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (module is null || !_letters.TryGetValue(moduleId, out var list)) return Task.FromResult(0);

        (IntegrationEnvelope Envelope, DeadLetter Letter)[] letters;
        lock (list)
        {
            letters = list.ToArray();
            list.Clear();
        }
        var bus = services.GetRequiredService<LocalIntegrationBus>();
        foreach (var (envelope, _) in letters) _ = Task.Run(() => bus.DeliverAsync(module, envelope, attempt: 0));
        return Task.FromResult(letters.Length);
    }
}

/// <summary>
/// Admin API for dead letters (platform operators): per pool, since each pool has its own queues; reach another pool
/// through its own route. Replay moves a module's dead letters back to its queue with a fresh attempt count.
/// </summary>
public sealed class IntegrationAdminEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var dead = app.MapGroup("/api/admin/integration/dead-letters")
            .WithTags("Integration events")
            .RequirePermission(PlatformPermissions.ModulesManage);

        dead.MapGet("/", (IServiceProvider services, CancellationToken ct) => services.GetRequiredService<IDeadLetters>().ListAsync(ct))
            .WithName("DeadLetters_List")
            .WithSummary("Dead-letter queues of this pool's subscriber modules, with their message counts.");

        dead.MapGet("/{moduleId}", (string moduleId, IServiceProvider services, CancellationToken ct, int take = 20) =>
                services.GetRequiredService<IDeadLetters>().PeekAsync(moduleId, Math.Clamp(take, 1, 200), ct))
            .WithName("DeadLetters_Peek")
            .WithSummary("The first dead letters of a module (left in place).");

        dead.MapPost("/{moduleId}/replay", async (string moduleId, IServiceProvider services, CancellationToken ct) =>
                new { replayed = await services.GetRequiredService<IDeadLetters>().ReplayAsync(moduleId, ct) })
            .WithName("DeadLetters_Replay")
            .WithSummary("Deliver every dead letter of the module again, with a fresh attempt count.");
    }
}

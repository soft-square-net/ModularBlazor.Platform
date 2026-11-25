using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using soft.square.net.WebApi.Contracts.Messaging;

namespace soft.square.net.WebApi.Messaging;

/// <summary>
/// Handles integration events in the host itself (a host type, resolved from the host's root container), for example
/// "seeds changed: run the reconciler now". Register with
/// <see cref="HostIntegrationServiceCollectionExtensions.AddHostIntegrationHandler{THandler}"/>.
/// <para>Host subscriptions differ from module subscriptions:</para>
/// <list type="bullet">
/// <item><b>Every replica</b> receives the event (each one binds its own exclusive queue), not one replica of a pool.</item>
/// <item><b>No tenant:</b> the handler runs outside any tenant scope, and the envelope's tenant may be empty or unknown.</item>
/// <item><b>Not durable:</b> a replica that is down misses the event. Use host handlers for signals whose work is also
/// done on a timer or on start (like the seed reconciler); durable work belongs in a module with its inbox.</item>
/// <item><b>Idempotency:</b> a replica runs a handler once per event id (an in-memory inbox of recent ids), and retries
/// a failing handler <see cref="IntegrationOptions.MaxAttempts"/> times with <see cref="IntegrationOptions.RetryDelay"/>,
/// then logs the event as dropped.</item>
/// </list>
/// </summary>
public interface IHostIntegrationHandler
{
    Task HandleAsync(IntegrationEnvelope envelope, CancellationToken ct);
}

/// <summary>A host handler type registered for an event type pattern.</summary>
public sealed record HostIntegrationSubscription(string Pattern, Type HandlerType);

public static class HostIntegrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="THandler"/> (a host singleton) for the event types matching
    /// <paramref name="pattern"/> (see <see cref="IntegrationTypes.Matches"/>). The order relative to
    /// <c>AddPlatformIntegrationEvents</c> does not matter.
    /// </summary>
    public static IServiceCollection AddHostIntegrationHandler<THandler>(this IServiceCollection services, string pattern)
        where THandler : class, IHostIntegrationHandler
    {
        IntegrationTypes.ValidatePattern(pattern);
        services.AddSingleton<THandler>();
        services.AddSingleton(new HostIntegrationSubscription(pattern, typeof(THandler)));
        return services;
    }
}

/// <summary>
/// Runs the host's integration handlers on this replica (both transports call it): once per event id and handler,
/// with retries. Never throws; an event that fails every attempt is logged and dropped.
/// </summary>
internal sealed class HostIntegrationHandlers(
    IEnumerable<HostIntegrationSubscription> subscriptions, IServiceProvider services, IOptions<IntegrationOptions> options,
    ILogger<HostIntegrationHandlers> log) : IDisposable
{
    private const int Remembered = 4096;

    private readonly HostIntegrationSubscription[] _subscriptions = subscriptions.ToArray();
    private readonly CancellationTokenSource _stopping = new();
    private readonly HashSet<(Guid, Type)> _handled = [];
    private readonly Queue<(Guid, Type)> _order = new();

    public IReadOnlyList<HostIntegrationSubscription> Subscriptions => _subscriptions;

    public bool Matches(string type) => _subscriptions.Any(s => IntegrationTypes.Matches(s.Pattern, type));

    /// <summary>Stopped when the host's container is disposed.</summary>
    public CancellationToken Stopping => _stopping.Token;

    public async Task DispatchAsync(IntegrationEnvelope envelope, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopping.Token);
        foreach (var subscription in _subscriptions.Where(s => IntegrationTypes.Matches(s.Pattern, envelope.Type)))
        {
            var key = (envelope.Id, subscription.HandlerType);
            lock (_handled)
                if (_handled.Contains(key)) continue; // a second delivery of the same event

            if (await RunAsync(subscription, envelope, linked.Token)) Remember(key);
        }
    }

    private async Task<bool> RunAsync(HostIntegrationSubscription subscription, IntegrationEnvelope envelope, CancellationToken ct)
    {
        var handler = (IHostIntegrationHandler)services.GetRequiredService(subscription.HandlerType);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await handler.HandleAsync(envelope, ct);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return false; }
            catch (Exception e)
            {
                if (attempt >= options.Value.MaxAttempts)
                {
                    log.LogError(e, "Host integration handler {Handler} failed {Attempt} times on event {Id} ({Type}); dropped",
                        subscription.HandlerType.Name, attempt, envelope.Id, envelope.Type);
                    return false;
                }
                log.LogWarning(e, "Host integration handler {Handler} failed on event {Id} ({Type}) (attempt {Attempt}); retrying in {Delay}",
                    subscription.HandlerType.Name, envelope.Id, envelope.Type, attempt, options.Value.RetryDelay);
            }

            try { await Task.Delay(options.Value.RetryDelay, ct); }
            catch (OperationCanceledException) { return false; }
        }
    }

    private void Remember((Guid, Type) key)
    {
        lock (_handled)
        {
            if (!_handled.Add(key)) return;
            _order.Enqueue(key);
            if (_order.Count > Remembered) _handled.Remove(_order.Dequeue());
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }
}

/// <summary>
/// The broker side of host subscriptions: this replica's own exclusive, auto-delete queue, bound to the exchange
/// <see cref="IntegrationNames.Exchange"/> with the binding keys of the host's patterns. Does nothing when the host has
/// no handler. Reconnects when the channel closes.
/// </summary>
internal sealed class RabbitMqHostIntegrationSubscriber(
    IConnection connection, HostIntegrationHandlers handlers, ILogger<RabbitMqHostIntegrationSubscriber> log) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (handlers.Subscriptions.Count == 0) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ConsumeAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogWarning(e, "The host integration subscription stopped; reconnecting"); }

            try { await Task.Delay(Retry, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await IntegrationTopology.DeclareExchangesAsync(channel, ct);
        var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: ct);
        var keys = handlers.Subscriptions.Select(s => IntegrationTypes.ToBindingKey(s.Pattern)).Distinct(StringComparer.Ordinal);
        foreach (var key in keys) await channel.QueueBindAsync(queue.QueueName, IntegrationNames.Exchange, key, cancellationToken: ct);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, message) =>
        {
            IntegrationEnvelope envelope;
            try { envelope = IntegrationEnvelope.Parse(message.Body.Span); }
            catch (JsonException e)
            {
                log.LogWarning(e, "Ignored a malformed integration event {MessageId}", message.BasicProperties.MessageId);
                return;
            }
            if (handlers.Matches(envelope.Type)) await handlers.DispatchAsync(envelope, ct);
        };
        // Auto-ack: the queue is this replica's own and goes away with it, so a redelivery could only come back here.
        await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer, ct);
        log.LogInformation("Host subscribes to integration events {Patterns}",
            string.Join(", ", handlers.Subscriptions.Select(s => s.Pattern).Distinct(StringComparer.Ordinal)));

        await closed.Task.WaitAsync(ct);
    }
}

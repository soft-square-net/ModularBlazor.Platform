using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Modularity;

namespace soft.square.net.WebApi.Messaging;

/// <summary>
/// Publishes integration events to the topic exchange <see cref="IntegrationNames.Exchange"/> (routing key: the event
/// type; tenant in a header), persistent, and waits for the broker's confirm, so a row is marked sent only once the
/// broker has it.
/// </summary>
internal sealed class RabbitMqBrokerPublisher(IConnection connection, ILogger<RabbitMqBrokerPublisher> log) : IBrokerPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1); // a channel is not safe for concurrent publishing
    private IChannel? _channel;

    public async Task PublishAsync(IntegrationMessage message, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_channel is not { IsOpen: true })
            {
                _channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
                await IntegrationTopology.DeclareExchangesAsync(_channel, ct);
            }
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                MessageId = message.Id.ToString("n"),
                Type = message.Type,
                Persistent = true,
                Headers = new Dictionary<string, object?> { [IntegrationNames.TenantHeader] = message.TenantId }
            };
            await _channel.BasicPublishAsync(IntegrationNames.Exchange, message.Type, mandatory: false, properties, message.Body, ct);
            log.LogDebug("Published integration event {Id} ({Type}) of tenant {Tenant}", message.Id, message.Type, message.TenantId);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _channel = null; // reopened on the next publish
            throw;
        }
        finally { _lock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
    }
}

/// <summary>
/// The broker topology of integration events, per module and pool:
/// <list type="bullet">
/// <item><c>integration.{module}.{pool}</c>: durable, bound to the exchange <c>integration</c> with the module's
/// subscription patterns; the pool's replicas are its competing consumers. Rejected messages dead-letter to
/// <c>integration.dlx</c>.</item>
/// <item><c>….retry</c>: failed events wait there (per-message expiry) and then dead-letter back to the module's queue.</item>
/// <item><c>….dead</c>: bound to <c>integration.dlx</c>; events that failed every attempt, kept for replay.</item>
/// </list>
/// Declared when the catalog has the module and this pool may run it (at install, before it is ever loaded) and again
/// when it is loaded; kept while the module is unloaded, so events wait; deleted when it is uninstalled.
/// </summary>
internal sealed class IntegrationTopology(IConnection connection, IOptions<ModuleRuntimeOptions> runtime, ILogger<IntegrationTopology> log)
    : IModuleCatalogObserver, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, string[]> _declared = new(StringComparer.OrdinalIgnoreCase); // module -> binding keys
    private IChannel? _channel;

    public string Pool => runtime.Value.Pool;

    public string QueueOf(string moduleId) => IntegrationNames.Queue(moduleId, Pool);

    /// <summary>Modules whose queues this replica declared.</summary>
    public IReadOnlyList<string> Modules
    {
        get { lock (_declared) return _declared.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray(); }
    }

    public static async Task DeclareExchangesAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(IntegrationNames.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(IntegrationNames.DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);
    }

    /// <summary>Declares the module's queues and binds them to <paramref name="patterns"/> (unbinding keys it no longer has).</summary>
    public async Task DeclareModuleAsync(IChannel channel, string moduleId, IReadOnlyList<string> patterns, CancellationToken ct)
    {
        var queue = QueueOf(moduleId);
        await DeclareExchangesAsync(channel, ct);
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = IntegrationNames.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = queue
        }, cancellationToken: ct);
        await channel.QueueDeclareAsync(IntegrationNames.RetryQueue(queue), durable: true, exclusive: false, autoDelete: false, new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = "",
            ["x-dead-letter-routing-key"] = queue
        }, cancellationToken: ct);
        var dead = IntegrationNames.DeadQueue(queue);
        await channel.QueueDeclareAsync(dead, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(dead, IntegrationNames.DeadLetterExchange, queue, cancellationToken: ct);

        var keys = patterns.Select(IntegrationTypes.ToBindingKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var key in keys) await channel.QueueBindAsync(queue, IntegrationNames.Exchange, key, cancellationToken: ct);

        string[]? previous;
        lock (_declared)
        {
            _declared.TryGetValue(moduleId, out previous);
            _declared[moduleId] = keys;
        }
        foreach (var stale in (previous ?? []).Except(keys, StringComparer.Ordinal))
            await channel.QueueUnbindAsync(queue, IntegrationNames.Exchange, stale, cancellationToken: ct);
        if (previous is null || !previous.SequenceEqual(keys))
            log.LogInformation("Integration queue {Queue} bound to {Keys}", queue, string.Join(", ", keys));
    }

    /// <summary>
    /// After every sync: queues for every module of the catalog this pool may run (installed, inactive or active), and
    /// none for modules that left the catalog (uninstalled).
    /// </summary>
    public async Task CatalogSyncedAsync(IReadOnlyList<ModuleRecord> records, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_channel is not { IsOpen: true }) _channel = await connection.CreateChannelAsync(cancellationToken: ct);

            var present = records.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var record in records.Where(r => r.Subscriptions.Count > 0 && runtime.Value.IsEnabled(r.Id)))
            {
                bool same;
                lock (_declared)
                    same = _declared.TryGetValue(record.Id, out var keys)
                           && keys.SequenceEqual(record.Subscriptions.Select(IntegrationTypes.ToBindingKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
                if (!same) await DeclareModuleAsync(_channel, record.Id, record.Subscriptions, ct);
            }

            string[] removed;
            lock (_declared) removed = _declared.Keys.Where(id => !present.Contains(id)).ToArray();
            foreach (var id in removed)
            {
                var queue = QueueOf(id);
                await _channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: false, cancellationToken: ct);
                await _channel.QueueDeleteAsync(IntegrationNames.RetryQueue(queue), ifUnused: false, ifEmpty: false, cancellationToken: ct);
                await _channel.QueueDeleteAsync(IntegrationNames.DeadQueue(queue), ifUnused: false, ifEmpty: false, cancellationToken: ct);
                lock (_declared) _declared.Remove(id);
                log.LogInformation("Module {Module} was uninstalled: deleted integration queue {Queue}", id, queue);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _channel = null;
            log.LogWarning(e, "Could not apply the integration queues of pool {Pool}; retried on the next sync", Pool);
        }
        finally { _lock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
    }
}

/// <summary>
/// Consumes a module's queue on this replica (manual acks, prefetch <see cref="IntegrationOptions.Prefetch"/>). Each
/// event is delivered through <see cref="IntegrationDelivery"/>; on failure it goes to the retry queue (attempt header +1)
/// and, after <see cref="IntegrationOptions.MaxAttempts"/>, to the dead-letter exchange with its last error. When the
/// module is released, the event in progress is not acknowledged, so the broker delivers it again.
/// </summary>
internal sealed class RabbitMqIntegrationTransport(
    IConnection connection, IntegrationTopology topology, IntegrationDelivery delivery, IOptions<IntegrationOptions> options,
    ILogger<RabbitMqIntegrationTransport> log) : IIntegrationTransport
{
    public async Task RunAsync(SubscriberModule module)
    {
        var ct = module.Stopping.Token;
        while (!ct.IsCancellationRequested)
        {
            try { await ConsumeAsync(module, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogWarning(e, "The integration consumer of module {Module} stopped; reconnecting", module.ModuleId); }

            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ConsumeAsync(SubscriberModule module, CancellationToken ct)
    {
        var queue = topology.QueueOf(module.ModuleId);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await using var publish = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await topology.DeclareModuleAsync(channel, module.ModuleId, module.Patterns, ct);
        await channel.BasicQosAsync(0, options.Value.Prefetch, global: false, ct);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, message) => OnReceivedAsync(module, channel, publish, queue, message);
        await channel.BasicConsumeAsync(queue, autoAck: false, consumer, ct);
        log.LogInformation("Consuming integration queue {Queue} for module {Module}", queue, module.ModuleId);

        await closed.Task.WaitAsync(ct);
    }

    private async Task OnReceivedAsync(SubscriberModule module, IChannel channel, IChannel publish, string queue, BasicDeliverEventArgs message)
    {
        var ct = module.Stopping.Token;
        try { await module.Processing.WaitAsync(ct); }
        catch (OperationCanceledException) { return; } // released: the broker redelivers it
        catch (ObjectDisposedException) { return; }

        var body = message.Body.ToArray();
        try
        {
            IntegrationEnvelope envelope;
            try { envelope = IntegrationEnvelope.Parse(body); }
            catch (JsonException e)
            {
                log.LogError(e, "Malformed integration event {MessageId} on {Queue}; dead-lettered", message.BasicProperties.MessageId, queue);
                await channel.BasicNackAsync(message.DeliveryTag, multiple: false, requeue: false, ct);
                return;
            }

            if (!module.Matches(envelope.Type)) // the binding key is wider than the pattern
            {
                await channel.BasicAckAsync(message.DeliveryTag, multiple: false, ct);
                return;
            }

            try
            {
                var result = await delivery.DeliverAsync(module, envelope, ct);
                await channel.BasicAckAsync(message.DeliveryTag, multiple: false, ct);
                log.LogDebug("Integration event {Id} ({Type}) → {Module}: {Result}", envelope.Id, envelope.Type, module.ModuleId, result);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Released while handling: not acknowledged, so the broker delivers it again (to this pool's next consumer).
            }
            catch (Exception e)
            {
                var headers = message.BasicProperties.Headers is { } existing ? new Dictionary<string, object?>(existing) : [];
                var attempt = IntegrationHeaders.Attempt(headers) + 1;
                headers[IntegrationNames.AttemptHeader] = attempt;
                headers[IntegrationNames.ErrorHeader] = IntegrationHeaders.Trim($"{e.GetType().Name}: {e.Message}");
                var properties = new BasicProperties(message.BasicProperties) { Headers = headers, Persistent = true };

                if (attempt < options.Value.MaxAttempts)
                {
                    properties.Expiration = ((long)options.Value.RetryDelay.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    await publish.BasicPublishAsync("", IntegrationNames.RetryQueue(queue), mandatory: false, properties, body, ct);
                    log.LogWarning(e, "Integration event {Id} ({Type}) failed in module {Module} (attempt {Attempt} of {Max}); retrying in {Delay}",
                        envelope.Id, envelope.Type, module.ModuleId, attempt, options.Value.MaxAttempts, options.Value.RetryDelay);
                }
                else
                {
                    properties.Expiration = null;
                    await publish.BasicPublishAsync(IntegrationNames.DeadLetterExchange, queue, mandatory: false, properties, body, ct);
                    log.LogError(e, "Integration event {Id} ({Type}) failed in module {Module} {Attempt} times; dead-lettered to {Queue}",
                        envelope.Id, envelope.Type, module.ModuleId, attempt, IntegrationNames.DeadQueue(queue));
                }
                await channel.BasicAckAsync(message.DeliveryTag, multiple: false, ct);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Integration event {MessageId} on {Queue} was not acknowledged; the broker delivers it again", message.BasicProperties.MessageId, queue);
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { module.Processing.Release(); }
            catch (ObjectDisposedException) { }
        }
    }
}

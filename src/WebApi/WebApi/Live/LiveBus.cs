using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modularity;

namespace soft.square.net.WebApi.Live;

/// <summary>
/// A live message between replicas: the tenant, the topics it goes to (one, or a unit and its ancestors) and the
/// sequence, which only the first topic carries (the ancestors' feeds are not this unit's).
/// </summary>
internal sealed record LiveMessage(string TenantId, IReadOnlyList<string> Topics, string Type, JsonElement? Payload, long? Sequence,
    DateTimeOffset At, string Origin);

/// <summary>This replica's id on the live bus (unique per host, also when tests run several in one process).</summary>
internal sealed class LiveReplica
{
    public string Id { get; } = $"{ModuleInstance.Id}:{Guid.NewGuid():n}";
}

/// <summary>Sends a message to this replica's connections in the message's groups.</summary>
internal sealed class LiveDispatcher(IHubContext<LiveHub> hub)
{
    public async Task DeliverAsync(LiveMessage message, CancellationToken ct)
    {
        for (var i = 0; i < message.Topics.Count; i++)
        {
            var topic = message.Topics[i];
            var envelope = new LiveEnvelope(topic, message.Type, message.Payload, i == 0 ? message.Sequence : null, message.At);
            await hub.Clients.Group(LiveGroups.Of(message.TenantId, topic)).SendAsync(LiveHubMethods.Message, envelope, ct);
        }
    }
}

internal interface ILiveBus
{
    Task PublishAsync(LiveMessage message, CancellationToken ct);
}

/// <summary>Single instance without a broker: this replica's connections are all there is.</summary>
internal sealed class LocalLiveBus(LiveDispatcher dispatcher) : ILiveBus
{
    public Task PublishAsync(LiveMessage message, CancellationToken ct) => dispatcher.DeliverAsync(message, ct);
}

/// <summary>
/// RabbitMQ fanout exchange "live" (like "modules.state"): every replica binds its own exclusive, auto-delete queue and
/// sends what it receives to its own connections. The publishing replica delivers to its connections first, without the
/// broker round trip, and skips its own message when it comes back. Not durable: a client that was not connected
/// catches up from the module's feed.
/// </summary>
internal sealed class RabbitMqLiveBus(IConnection connection, LiveDispatcher dispatcher, ILogger<RabbitMqLiveBus> log) : ILiveBus, IAsyncDisposable
{
    public const string Exchange = "live";

    private readonly SemaphoreSlim _lock = new(1, 1); // a channel is not safe for concurrent publishing
    private IChannel? _channel;

    public static Task DeclareAsync(IChannel channel, CancellationToken ct) =>
        channel.ExchangeDeclareAsync(Exchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: ct);

    public async Task PublishAsync(LiveMessage message, CancellationToken ct)
    {
        await dispatcher.DeliverAsync(message, ct);

        await _lock.WaitAsync(ct);
        try
        {
            if (_channel is not { IsOpen: true })
            {
                _channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await DeclareAsync(_channel, ct);
            }
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                MessageId = Guid.NewGuid().ToString("n"),
                Type = message.Type,
                Headers = new Dictionary<string, object?> { [TenancyNames.MessageHeader] = message.TenantId }
            };
            await _channel.BasicPublishAsync(Exchange, routingKey: string.Empty, mandatory: false, properties,
                JsonSerializer.SerializeToUtf8Bytes(message), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Live messages are hints: other replicas' clients resync from the module feed on their next message or reconnect.
            log.LogWarning(e, "Could not publish live message {Type} to {Topic}", message.Type, message.Topics[0]);
            _channel = null;
        }
        finally { _lock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
    }
}

/// <summary>
/// Subscribes this replica to "live" and delivers every message published elsewhere to its connections. The queue is
/// bound while the host starts, so a replica never serves hub connections it would not deliver to; if the broker is
/// not reachable then, it keeps trying in the background.
/// </summary>
internal sealed class LiveSubscriber(IConnection connection, LiveDispatcher dispatcher, LiveReplica replica, ILogger<LiveSubscriber> log) : BackgroundService
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);
    private IChannel? _channel;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try { await SubscribeAsync(cancellationToken); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Could not subscribe to live messages yet; retrying"); }
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (_channel is null && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Retry, stoppingToken);
                await SubscribeAsync(stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { log.LogWarning(e, "Could not subscribe to live messages; retrying"); }
        }

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }
    }

    private async Task SubscribeAsync(CancellationToken ct)
    {
        var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        try
        {
            await RabbitMqLiveBus.DeclareAsync(channel, ct);
            var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: ct);
            await channel.QueueBindAsync(queue.QueueName, RabbitMqLiveBus.Exchange, routingKey: string.Empty, cancellationToken: ct);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, delivery) =>
            {
                LiveMessage? message;
                try { message = JsonSerializer.Deserialize<LiveMessage>(delivery.Body.Span); }
                catch (JsonException e) { log.LogWarning(e, "Ignored a malformed live message"); return; }
                if (message is null || message.Topics.Count == 0 || message.Origin == replica.Id) return; // delivered when published

                try { await dispatcher.DeliverAsync(message, CancellationToken.None); }
                catch (Exception e) { log.LogWarning(e, "Could not deliver live message {Type}", message.Type); }
            };
            await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer, ct);
            _channel = channel;
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null) await _channel.DisposeAsync();
    }
}

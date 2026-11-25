using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Contracts.Messaging;

namespace soft.square.net.WebApi.Messaging;

/// <summary>
/// Integration events without a broker (a single local instance): a publish is delivered in the background to every
/// subscriber module loaded on this process, with the same retries, and kept in memory as a dead letter when every
/// attempt failed. Nothing is durable: an event for a module that is not loaded at that moment is not delivered.
/// </summary>
internal sealed class LocalIntegrationBus(
    IntegrationConsumers consumers, IntegrationDelivery delivery, LocalDeadLetters deadLetters, IOptions<IntegrationOptions> options,
    ILogger<LocalIntegrationBus> log) : IBrokerPublisher, IIntegrationTransport
{
    public Task PublishAsync(IntegrationMessage message, CancellationToken ct)
    {
        var envelope = IntegrationEnvelope.Parse(message.Body.Span);
        foreach (var module in consumers.Subscribers.Where(m => m.Matches(envelope.Type)))
            _ = Task.Run(() => DeliverAsync(module, envelope, attempt: 0));
        return Task.CompletedTask;
    }

    /// <summary>Consumption is driven by <see cref="PublishAsync"/>; nothing runs per module.</summary>
    public Task RunAsync(SubscriberModule module) => Task.CompletedTask;

    /// <summary>Delivers to one module (also used to replay its dead letters).</summary>
    public async Task DeliverAsync(SubscriberModule module, IntegrationEnvelope envelope, int attempt)
    {
        var ct = module.Stopping.Token;
        while (true)
        {
            try { await module.Processing.WaitAsync(ct); }
            catch (OperationCanceledException) { return; }
            try
            {
                await delivery.DeliverAsync(module, envelope, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                attempt++;
                if (attempt >= options.Value.MaxAttempts)
                {
                    log.LogError(e, "Integration event {Id} ({Type}) failed in module {Module} {Attempt} times; kept as a dead letter",
                        envelope.Id, envelope.Type, module.ModuleId, attempt);
                    deadLetters.Add(module.ModuleId, envelope, attempt, IntegrationHeaders.Trim($"{e.GetType().Name}: {e.Message}"));
                    return;
                }
                log.LogWarning(e, "Integration event {Id} ({Type}) failed in module {Module} (attempt {Attempt}); retrying in {Delay}",
                    envelope.Id, envelope.Type, module.ModuleId, attempt, options.Value.RetryDelay);
            }
            finally { module.Processing.Release(); }

            try { await Task.Delay(options.Value.RetryDelay, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}

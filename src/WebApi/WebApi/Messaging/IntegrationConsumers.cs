using System.Collections.Concurrent;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Modularity;

namespace soft.square.net.WebApi.Messaging;

/// <summary>How events reach the subscriber modules loaded on this replica: the broker, or the in-process bus.</summary>
internal interface IIntegrationTransport
{
    /// <summary>Consumes for <paramref name="module"/> until its <see cref="SubscriberModule.Stopping"/> is cancelled.</summary>
    Task RunAsync(SubscriberModule module);
}

/// <summary>
/// One consumer per loaded subscriber module on this replica (a lifecycle observer): started when the runtime loads a
/// module whose manifest has <see cref="Contracts.ModuleManifest.Subscriptions"/>, stopped before the copy is released.
/// The release cancels the consumer, waits for the event in progress, and drops every reference to the module's
/// container, so the copy can be collected; unacknowledged events go back to the module's durable queue.
/// </summary>
internal sealed class IntegrationConsumers(ModuleSubscriptions subscriptions, IServiceProvider services, ILogger<IntegrationConsumers> log)
    : IModuleLifecycleObserver
{
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<IServiceProvider, SubscriberModule> _modules = new(ReferenceEqualityComparer.Instance);
    private IIntegrationTransport? _transport;

    private IIntegrationTransport Transport => _transport ??= services.GetRequiredService<IIntegrationTransport>();

    /// <summary>The subscriber modules loaded on this replica now.</summary>
    public IReadOnlyCollection<SubscriberModule> Subscribers => _modules.Values.ToArray();

    public void Loaded(string moduleId, IServiceProvider moduleServices)
    {
        var patterns = subscriptions.Of(moduleId);
        if (patterns.Count == 0) return;

        var module = new SubscriberModule(moduleId, moduleServices, patterns);
        _modules[moduleServices] = module;
        module.Running = Task.Run(() => Transport.RunAsync(module));
        log.LogInformation("Module {Module} subscribes to integration events {Patterns}", moduleId, string.Join(", ", patterns));
    }

    public async Task ReleasingAsync(string moduleId, IServiceProvider moduleServices)
    {
        if (!_modules.TryRemove(moduleServices, out var module)) return;
        await module.Stopping.CancelAsync();
        try
        {
            // The event in progress (if any) ends first; nothing starts after this.
            await module.Processing.WaitAsync(ReleaseTimeout);
            await (module.Running ?? Task.CompletedTask).WaitAsync(ReleaseTimeout);
        }
        catch (Exception e) { log.LogWarning(e, "The integration consumer of module {Module} did not stop cleanly", moduleId); }
        module.Running = null;

    }

    public bool AnyMatches(string type) => _modules.Values.Any(m => m.Matches(type));
}

/// <summary>Reads the delivery attempt and error headers of a broker message.</summary>
internal static class IntegrationHeaders
{
    public static int Attempt(IDictionary<string, object?>? headers) =>
        headers?.TryGetValue(IntegrationNames.AttemptHeader, out var value) == true ? value switch
        {
            int i => i,
            long l => (int)l,
            byte[] bytes when int.TryParse(System.Text.Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => 0
        } : 0;

    public static string? Text(IDictionary<string, object?>? headers, string name) =>
        headers?.TryGetValue(name, out var value) == true ? value switch
        {
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            string text => text,
            null => null,
            var other => other.ToString()
        } : null;

    public static string Trim(string error) => error.Length > 500 ? error[..500] : error;
}

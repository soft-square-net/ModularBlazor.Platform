using System.Collections.Concurrent;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modularity;
using soft.square.net.WebApi.Tenancy;

namespace soft.square.net.WebApi.Messaging;

/// <summary>Host settings of integration events (configuration section "Integration").</summary>
public sealed class IntegrationOptions
{
    /// <summary>Deliveries of one event to one module before it is dead-lettered (the first one included).</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Wait before a failed event is delivered again.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Unacknowledged events a module's consumer holds at once on a replica.</summary>
    public ushort Prefetch { get; set; } = 16;
}

/// <summary>
/// The subscription patterns of the module copies loaded on this replica, set by the runtime from the manifest it
/// just read (strings only).
/// </summary>
internal sealed class ModuleSubscriptions
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _byModule = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string moduleId, IReadOnlyList<string> patterns) => _byModule[moduleId] = patterns.ToArray();

    public IReadOnlyList<string> Of(string moduleId) => _byModule.GetValueOrDefault(moduleId) ?? [];
}

/// <summary>
/// One loaded module copy that subscribes to integration events. Holds the module's container only until the runtime
/// releases the copy (<see cref="IntegrationConsumers.ReleasingAsync"/>).
/// </summary>
internal sealed class SubscriberModule(string moduleId, IServiceProvider services, IReadOnlyList<string> patterns)
{
    public string ModuleId { get; } = moduleId;
    public IServiceProvider Services { get; } = services;
    public IReadOnlyList<string> Patterns { get; } = patterns;
    public CancellationTokenSource Stopping { get; } = new();

    /// <summary>Held while one event is handled; taken by the release so no handler runs on a disposed container.</summary>
    public SemaphoreSlim Processing { get; } = new(1, 1);

    public Task? Running { get; set; }

    public bool Matches(string type) => Patterns.Any(p => IntegrationTypes.Matches(p, type));
}

/// <summary>
/// Delivers one event to one module on this replica: a tenant scope of the module's own container (the host never
/// holds a module type), after the module is initialized for that tenant (migrations), then the module's inbox runs
/// the handlers.
/// </summary>
internal sealed class IntegrationDelivery(TenantDirectory directory, ModuleTenantInitializer initializer)
{
    public async Task<IntegrationProcessResult> DeliverAsync(SubscriberModule module, IntegrationEnvelope envelope, CancellationToken ct)
    {
        var tenant = directory.Find(envelope.TenantId)
                     ?? throw new InvalidOperationException($"Tenant '{envelope.TenantId}' of integration event {envelope.Id} is unknown.");
        if (!tenant.IsActive)
            throw new InvalidOperationException($"Tenant '{tenant.Id}' of integration event {envelope.Id} is not active.");

        await initializer.ReadyAsync(module.ModuleId, tenant).WaitAsync(ct);
        await using var scope = module.Services.CreateTenantScope(tenant);
        var processor = scope.ServiceProvider.GetService<IIntegrationMessageProcessor>()
                        ?? throw new InvalidOperationException(
                            $"Module '{module.ModuleId}' subscribes to integration events but does not register them (services.AddIntegrationEvents<TDbContext>()).");
        return await processor.ProcessAsync(envelope, ct);
    }
}

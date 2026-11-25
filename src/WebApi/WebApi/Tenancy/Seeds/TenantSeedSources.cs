using System.Collections.Concurrent;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modularity;

namespace soft.square.net.WebApi.Tenancy.Seeds;

/// <summary>
/// The loaded modules that serve tenant seeds (<see cref="ITenantSeedSource"/>). It keeps only their containers, and
/// resolves the source from the module's container for each use, inside a gate: <see cref="ReleasingAsync"/> cancels a
/// running use and waits for it, so nothing of the module is referenced once the runtime disposes its container.
/// </summary>
internal sealed class TenantSeedSources : IModuleLifecycleObserver
{
    private readonly ConcurrentDictionary<IServiceProvider, Entry> _entries = new(ReferenceEqualityComparer.Instance);

    /// <summary>Raised when a module that serves seeds is loaded (the reconciler runs a cycle at once).</summary>
    public event Action? SourceLoaded;

    public bool Any => !_entries.IsEmpty;

    public void Loaded(string moduleId, IServiceProvider moduleServices)
    {
        // Ask without creating anything; a container that cannot tell is asked again on each cycle.
        if (moduleServices.GetService<IServiceProviderIsService>() is { } isService && !isService.IsService(typeof(ITenantSeedSource)))
            return;
        _entries[moduleServices] = new Entry(moduleId, moduleServices);
        SourceLoaded?.Invoke();
    }

    public async Task ReleasingAsync(string moduleId, IServiceProvider moduleServices)
    {
        if (!_entries.TryRemove(moduleServices, out var entry)) return;
        await entry.Stopping.CancelAsync();
        try { await entry.Gate.WaitAsync(TimeSpan.FromSeconds(30)); } // a running cycle notices the cancellation
        catch (ObjectDisposedException) { }
        entry.Stopping.Dispose();
    }

    /// <summary>
    /// Runs <paramref name="use"/> once for each seed source of each loaded module, one module at a time, with a token
    /// that is also cancelled when that module is released. The source must not outlive the call.
    /// </summary>
    public async Task ForEachAsync(Func<string, ITenantSeedSource, CancellationToken, Task> use, CancellationToken ct)
    {
        foreach (var entry in _entries.Values.ToArray())
        {
            await entry.Gate.WaitAsync(ct);
            try
            {
                if (entry.Stopping.IsCancellationRequested) continue;
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, entry.Stopping.Token);
                await using var scope = entry.Services.CreateAsyncScope();
                foreach (var source in scope.ServiceProvider.GetServices<ITenantSeedSource>())
                    await use(entry.ModuleId, source, linked.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { } // the module is being released
            finally { entry.Gate.Release(); }
        }
    }

    private sealed class Entry(string moduleId, IServiceProvider services)
    {
        public string ModuleId { get; } = moduleId;
        public IServiceProvider Services { get; } = services;
        public CancellationTokenSource Stopping { get; } = new();
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
}

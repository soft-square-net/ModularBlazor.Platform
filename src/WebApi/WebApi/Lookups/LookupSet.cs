using System.Collections.Concurrent;
using System.Reflection;
using soft.square.net.WebApi.Contracts.Lookups;
using soft.square.net.WebApi.Modularity;

namespace soft.square.net.WebApi.Lookups;

/// <summary>
/// The lookups of one loaded module copy. Registered as a singleton in the module's own container, so it (and the
/// definitions with their expressions over module types) is disposed with the copy, never kept by the host.
/// </summary>
internal sealed class LookupSet(string moduleId, string routePrefix, IReadOnlyDictionary<string, ILookupDefinition> lookups)
{
    public string ModuleId { get; } = moduleId;
    public string RoutePrefix { get; } = routePrefix;
    public IReadOnlyDictionary<string, ILookupDefinition> Lookups { get; } = lookups;

    public ILookupDefinition Get(string name) =>
        Lookups.TryGetValue(name, out var lookup) ? lookup : throw new KeyNotFoundException($"Lookup '{RoutePrefix}.{name}' does not exist.");

    /// <summary>
    /// Runs every <see cref="ILookupModule"/> of the module's entry assembly. Null when it has none. Throws
    /// <see cref="ModuleException"/> for an invalid definition or an undeclared permission, so install and activation fail
    /// with the reason instead of the first request.
    /// </summary>
    /// <param name="only">Tests: this ILookupModule type instead of every one in the assembly.</param>
    public static LookupSet? Discover(Assembly assembly, string moduleId, string routePrefix, IReadOnlySet<string>? declaredPermissions, Type? only = null)
    {
        var types = only is not null
            ? [only]
            : Types(assembly)
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ILookupModule).IsAssignableFrom(t))
                .ToArray();
        if (types.Length == 0) return null;

        var builder = new Builder();
        try
        {
            foreach (var type in types) ((ILookupModule)Activator.CreateInstance(type)!).Register(builder);
            builder.Complete();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or MissingMethodException)
        {
            throw new ModuleException($"Module '{moduleId}' has an invalid lookup: {e.Message}");
        }

        if (declaredPermissions is not null)
        {
            var undeclared = builder.Definitions.Values
                .Where(d => d.RequiredPermission is { } p && !declaredPermissions.Contains(p) && !Contracts.Security.PlatformPermissions.IsPlatform(p))
                .Select(d => $"lookup '{d.Name}' requires '{d.RequiredPermission}', which is not declared in the manifest")
                .ToArray();
            if (undeclared.Length > 0) throw new ModuleException($"Module '{moduleId}' cannot be activated: {string.Join("; ", undeclared)}.");
        }
        return new LookupSet(moduleId, routePrefix.Trim('/'), builder.Definitions);
    }

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>(); }
    }

    private sealed class Builder : ILookupBuilder
    {
        private readonly List<Action> _complete = [];
        public Dictionary<string, ILookupDefinition> Definitions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ILookupDefinitionBuilder<T> For<T>(string name) where T : class
        {
            if (Definitions.ContainsKey(name)) throw new ArgumentException($"Lookup '{name}' is registered twice.");
            var definition = new LookupDefinition<T>(name);
            Definitions[name] = definition;
            _complete.Add(definition.Complete);
            return definition;
        }

        public void Complete()
        {
            foreach (var complete in _complete) complete();
        }
    }
}

/// <summary>
/// Which lookups this replica can serve in process: one entry per loaded module copy that has lookups. Holds the copy's
/// root scope and strings only, and lets go in <see cref="ReleasingAsync"/> after the calls in progress end, so the
/// module can unload. In a blue/green swap the new copy is loaded before the old one is released, so entries are
/// keyed by container and only the releasing copy's entry is removed.
/// </summary>
internal sealed class LookupRegistry : IModuleLifecycleObserver
{
    private readonly ConcurrentDictionary<IServiceProvider, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<string, Entry> _current = new(StringComparer.OrdinalIgnoreCase);

    public void Loaded(string moduleId, IServiceProvider moduleServices)
    {
        if (moduleServices.GetService(typeof(LookupSet)) is not LookupSet set) return;
        var entry = new Entry(moduleId, set.RoutePrefix, moduleServices);
        _entries[moduleServices] = entry;
        _current[set.RoutePrefix] = entry;
    }

    public async Task ReleasingAsync(string moduleId, IServiceProvider moduleServices)
    {
        if (!_entries.TryRemove(moduleServices, out var entry)) return;
        _current.TryRemove(new KeyValuePair<string, Entry>(entry.RoutePrefix, entry));
        await entry.CloseAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>A lease on the module copy that serves <paramref name="routePrefix"/>, or null when none is loaded here.</summary>
    public Lease? Acquire(string routePrefix) =>
        _current.TryGetValue(routePrefix, out var entry) ? entry.TryAcquire() : null;

    internal sealed class Entry(string moduleId, string routePrefix, IServiceProvider services)
    {
        private readonly Lock _gate = new();
        private int _running;
        private bool _closed;
        private TaskCompletionSource? _drained;

        public string ModuleId { get; } = moduleId;
        public string RoutePrefix { get; } = routePrefix;
        public IServiceProvider? Services { get; private set; } = services;

        public Lease? TryAcquire()
        {
            lock (_gate)
            {
                if (_closed || Services is null) return null;
                _running++;
                return new Lease(this, Services);
            }
        }

        public void Release()
        {
            lock (_gate)
            {
                if (--_running == 0) _drained?.TrySetResult();
            }
        }

        public async Task CloseAsync(TimeSpan timeout)
        {
            Task wait;
            lock (_gate)
            {
                _closed = true;
                if (_running == 0) { Services = null; return; }
                _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wait = _drained.Task;
            }
            try { await wait.WaitAsync(timeout); }
            catch (TimeoutException) { } // a stuck call only delays the unload
            lock (_gate) Services = null;
        }
    }

    internal sealed class Lease(Entry entry, IServiceProvider services) : IDisposable
    {
        private int _disposed;
        public string ModuleId => entry.ModuleId;
        public IServiceProvider Services { get; } = services;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) entry.Release();
        }
    }
}

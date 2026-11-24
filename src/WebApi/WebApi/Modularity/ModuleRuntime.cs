using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Carter;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Contracts;

namespace soft.square.net.WebApi.Modularity;

public sealed class ModuleRuntimeOptions
{
    /// <summary>Module store folder (packages + modules.json), relative to the content root.</summary>
    public string Path { get; set; } = "App_Data/modules";

    /// <summary>Base route; a module with RoutePrefix "greeting" is served under /api/greeting.</summary>
    public string RoutePrefix { get; set; } = "/api";

    /// <summary>Name of the pool this instance belongs to (same image, different configuration per pool).</summary>
    public string Pool { get; set; } = "all";

    /// <summary>Api: modules get HTTP routes. Worker: modules are loaded (services, consumers) but get no routes.</summary>
    public ModuleRole Role { get; set; } = ModuleRole.Api;

    /// <summary>Module ids this pool may activate; empty = every module.</summary>
    public string[] Enabled { get; set; } = [];

    /// <summary>Core module packages shipped with this release (one folder per module), relative to the app base directory.</summary>
    public string CorePath { get; set; } = "CoreModules";

    /// <summary>
    /// Development only (ignored in other environments): modules loaded from a build output folder, by module id, e.g.
    /// <c>Modules:Local:content = ../Module.Content/src/.../bin/Debug/net10.0</c>. On every start the folder is installed
    /// like an upload (and activated the first time) when its files changed, so rebuilding the module and restarting the
    /// WebApi shows the change without packing or uploading. Used by the module repos' playgrounds.
    /// </summary>
    public Dictionary<string, string> Local { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Modules:Settings:{id}:{key} (read by ModuleRuntime when it loads a module, not bound here): the host's overrides of
    // a module's settings, e.g. Modules__Settings__client-modules__ClientModules__Path=/data/client-modules. They win over
    // the catalog's settings (manifest defaults and the admin's values) on this instance and never reach modules.json.

    /// <summary>How long a module's hosted services may take to start, and (all together) to stop.</summary>
    public TimeSpan HostedServiceTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public bool IsEnabled(string moduleId) =>
        Enabled.Length == 0 || Enabled.Contains(moduleId, StringComparer.OrdinalIgnoreCase);
}

public enum ModuleRole { Api, Worker }

/// <summary>The host's IServiceCollection, kept so module containers can reuse its open-generic registrations.</summary>
internal sealed class HostServices(IServiceCollection services)
{
    public IServiceCollection Services => services;
}

/// <summary>
/// Module lifecycle: Install (store package, read manifest, register) → Activate (load assembly, build services,
/// map routes) → Deactivate (remove routes) → Unload (dispose services, unload the load context) → Uninstall.
/// <para>
/// The catalog (modules.json + packages) is shared by every replica of every pool. A lifecycle call changes the
/// module's desired state in the catalog under a cross-process lock, applies it on this replica, then publishes
/// <see cref="ModuleStateChanged"/>; every replica re-reads the catalog and converges in <see cref="SyncAsync"/>:
/// Active → loaded with routes (only where the pool enables the module), Inactive → no routes, Installed → unloaded,
/// removed or re-installed → unloaded (and re-applied).
/// </para>
/// </summary>
internal sealed partial class ModuleRuntime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, LoadedModule> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ModuleRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WeakReference> _unloading = new();
    private readonly ModuleRegistryStore _store;
    private readonly string _root;
    private readonly string _routeBase;
    private readonly ModuleEndpointDataSource _endpoints;
    private readonly HostServices _host;
    private readonly IServiceProvider _hostRoot;
    private readonly IModuleStateBus _bus;
    private readonly ILogger<ModuleRuntime> _log;
    private readonly ModuleRuntimeOptions _options;
    private readonly IHostEnvironment _environment;

    public ModuleRuntime(IOptions<ModuleRuntimeOptions> options, IHostEnvironment environment, ModuleEndpointDataSource endpoints,
        HostServices host, IServiceProvider hostRoot, IModuleStateBus bus, ILogger<ModuleRuntime> log)
    {
        _options = options.Value;
        _environment = environment;
        _root = Path.GetFullPath(options.Value.Path, environment.ContentRootPath);
        _routeBase = "/" + options.Value.RoutePrefix.Trim('/');
        Directory.CreateDirectory(_root);
        _store = new ModuleRegistryStore(Path.Combine(_root, "modules.json"));
        _endpoints = endpoints;
        _host = host;
        _hostRoot = hostRoot;
        _bus = bus;
        _log = log;
    }

    public async Task<IReadOnlyList<ModuleStatus>> ListAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using (await _store.LockAsync(ct)) ReloadRecords();
            return _records.Values.OrderBy(r => r.Id).Select(Status).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<ModuleRecord> InstallAsync(Stream content, string fileName, CancellationToken ct)
    {
        var staging = Path.Combine(_root, ".staging", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(staging);
        ModuleRecord record;
        try
        {
            await ExtractAsync(content, fileName, staging, ct);
            var (entry, manifest) = Inspect(staging);
            Validate(manifest);

            await _gate.WaitAsync(ct);
            try
            {
                using (await _store.LockAsync(ct))
                {
                    ReloadRecords();
                    if (_records.TryGetValue(manifest.Id, out var existing) && existing.Core)
                        throw new ModuleException($"'{manifest.Id}' is a core module; it is upgraded only by a WebApi release.", StatusCodes.Status409Conflict);
                    var clash = _records.Values.FirstOrDefault(r => r.Id != manifest.Id && r.RoutePrefix.Equals(manifest.RoutePrefix.Trim('/'), StringComparison.OrdinalIgnoreCase));
                    if (clash is not null)
                        throw new ModuleException($"Route prefix '{manifest.RoutePrefix}' is already used by module '{clash.Id}'.", StatusCodes.Status409Conflict);

                    // Replicas load packages from bytes, so replacing the files of a loaded module is safe; every replica
                    // sees the new InstalledAt on its next sync, unloads the old version and re-applies the desired state.
                    var moduleFolder = Path.Combine(_root, manifest.Id);
                    if (Directory.Exists(moduleFolder)) Directory.Delete(moduleFolder, recursive: true);
                    Directory.CreateDirectory(moduleFolder);
                    Directory.Move(staging, Path.Combine(moduleFolder, manifest.Version));

                    // Manifest defaults, keeping the admin's overrides for keys the new version still has.
                    var settings = new Dictionary<string, string?>(manifest.DefaultSettings);
                    _records.TryGetValue(manifest.Id, out var previous);
                    if (previous is not null)
                        foreach (var (key, value) in previous.Settings)
                            if (settings.ContainsKey(key)) settings[key] = value;

                    record = new ModuleRecord
                    {
                        Id = manifest.Id,
                        Name = manifest.Name,
                        Version = manifest.Version,
                        RoutePrefix = manifest.RoutePrefix.Trim('/'),
                        Description = manifest.Description,
                        HotUnload = manifest.HotUnload,
                        EntryAssembly = entry,
                        Settings = settings,
                        DesiredState = previous?.DesiredState ?? ModuleState.Installed, // an upgrade keeps the module's state
                        Permissions = manifest.Permissions.ToList(),
                        InstalledAt = DateTimeOffset.UtcNow
                    };
                    _records[record.Id] = record;
                    _store.Save(_records.Values);
                }
            }
            finally { _gate.Release(); }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }

        _log.LogInformation("Installed module {Id} {Version} from {File}", record.Id, record.Version, fileName);
        await ApplyAndPublishAsync(record.Id, "installed", ct);
        return record;
    }

    public async Task<ModuleStatus> ActivateAsync(string id, CancellationToken ct)
    {
        var record = await SetDesiredStateAsync(id, ModuleState.Active, ct);
        var result = await ApplyAndPublishAsync(record.Id, "activated", ct);
        if (result.Errors.TryGetValue(record.Id, out var error)) throw error;
        return await StatusAsync(record.Id, ct);
    }

    public async Task<ModuleStatus> DeactivateAsync(string id, CancellationToken ct)
    {
        var record = await SetDesiredStateAsync(id, ModuleState.Inactive, ct);
        await ApplyAndPublishAsync(record.Id, "deactivated", ct);
        return await StatusAsync(record.Id, ct);
    }

    /// <summary>Unloads the module on every replica; the result describes this replica.</summary>
    /// <param name="deferred">Required for a module with HotUnload = false: routes go now, the loaded copies at the next restart.</param>
    public async Task<UnloadResult> UnloadAsync(string id, bool deferred, CancellationToken ct)
    {
        var record = await SetDesiredStateAsync(id, ModuleState.Installed, ct, deferred);
        var result = await ApplyAndPublishAsync(record.Id, "unloaded", ct);
        return result.Unloaded.TryGetValue(record.Id, out var unload)
            ? unload
            : new UnloadResult(record.Id, Unloaded: true, GcPasses: 0, Note: "The module was not loaded on this replica.");
    }

    /// <summary>Load contexts that were unloaded but not yet collected; runs GCs and drops the collected ones.</summary>
    public Task<IReadOnlyList<string>> PendingUnloadsAsync(CancellationToken ct)
    {
        CollectPending();
        lock (_unloading) return Task.FromResult<IReadOnlyList<string>>(_unloading.Keys.ToArray());
    }

    public async Task<UnloadResult> UninstallAsync(string id, bool deferred, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        string moduleId;
        try
        {
            using (await _store.LockAsync(ct))
            {
                ReloadRecords();
                var record = Get(id);
                if (record.Core)
                    throw new ModuleException($"'{record.Id}' is a core module and ships with the release; deactivate or unload it instead.", StatusCodes.Status409Conflict);
                EnsureHotUnload(record, deferred, "uninstalled");
                moduleId = record.Id;
                _records.Remove(moduleId);
                _store.Save(_records.Values);
            }
        }
        finally { _gate.Release(); }

        var result = await ApplyAndPublishAsync(moduleId, "uninstalled", ct);
        var folder = Path.Combine(_root, moduleId);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); // every replica loaded it from bytes
        return result.Unloaded.TryGetValue(moduleId, out var unload)
            ? unload
            : new UnloadResult(moduleId, Unloaded: true, GcPasses: 0, Note: "The module was not loaded on this replica.");
    }

    /// <summary>Every replica that has the module loaded swaps to a copy configured with the new settings.</summary>
    public async Task<ModuleRecord> UpdateSettingsAsync(string id, Dictionary<string, string?> settings, CancellationToken ct)
    {
        ModuleRecord record;
        await _gate.WaitAsync(ct);
        try
        {
            using (await _store.LockAsync(ct))
            {
                ReloadRecords();
                record = Get(id);
                record.Settings = new Dictionary<string, string?>(settings);
                _store.Save(_records.Values);
            }
        }
        finally { _gate.Release(); }

        var result = await ApplyAndPublishAsync(record.Id, "configured", ct);
        if (result.Errors.TryGetValue(record.Id, out var error)) throw error;
        return record;
    }

    /// <summary>
    /// Registers the core modules shipped with this release (<see cref="ModuleRuntimeOptions.CorePath"/>) in the catalog.
    /// Runs on startup before the first sync, under the store lock, so concurrent replicas seed each module once.
    /// <list type="bullet">
    /// <item>Not in the catalog: installed as <see cref="ModuleState.Active"/>.</item>
    /// <item>In the catalog with another package hash and a version not newer than the shipped one: upgraded in place,
    /// keeping the admin's desired state and settings (like a re-install).</item>
    /// <item>The catalog holds a newer version (an older replica during a rolling release): left alone.</item>
    /// </list>
    /// Returns the ids that changed; the caller broadcasts so running replicas converge.
    /// </summary>
    public async Task<IReadOnlyList<string>> SeedCoreModulesAsync(CancellationToken ct)
    {
        var source = Path.GetFullPath(_options.CorePath, AppContext.BaseDirectory);
        if (!Directory.Exists(source)) return [];

        var changed = new List<string>();
        foreach (var folder in Directory.EnumerateDirectories(source).Order(StringComparer.OrdinalIgnoreCase))
            if (await SeedFolderAsync(folder, core: true, ct) is { } id) changed.Add(id);
        return changed;
    }

    /// <summary>
    /// Development only: installs each <see cref="ModuleRuntimeOptions.Local"/> build output folder like an upload when its
    /// files differ from the catalog's copy (first time: Active; afterwards the admin's state and settings are kept).
    /// Runs on startup next to <see cref="SeedCoreModulesAsync"/>. Returns the ids that changed.
    /// </summary>
    public async Task<IReadOnlyList<string>> SeedLocalModulesAsync(CancellationToken ct)
    {
        if (_options.Local.Count == 0) return [];
        if (!_environment.IsDevelopment())
        {
            _log.LogWarning("Modules:Local is ignored: local modules load only in the Development environment");
            return [];
        }

        var changed = new List<string>();
        foreach (var (name, path) in _options.Local.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var folder = Path.GetFullPath(path, _environment.ContentRootPath);
            if (!Directory.Exists(folder))
            {
                _log.LogError("Local module {Name}: folder {Folder} does not exist (build the module first)", name, folder);
                continue;
            }
            if (await SeedFolderAsync(folder, core: false, ct) is { } id) changed.Add(id);
        }
        return changed;
    }

    /// <summary>
    /// Copies one module folder (a core package or a local build output) into the catalog when its files differ from the
    /// catalog's copy, under the store lock. Returns the module id when the catalog changed.
    /// </summary>
    private async Task<string?> SeedFolderAsync(string folder, bool core, CancellationToken ct)
    {
        var kind = core ? "Core" : "Local";
        string entry;
        ModuleManifest manifest;
        try
        {
            (entry, manifest) = Inspect(folder);
            Validate(manifest);
        }
        catch (Exception e) when (e is ModuleException or IOException or BadImageFormatException or TypeLoadException)
        {
            _log.LogError(e, "{Kind} module package {Folder} is invalid; skipped", kind, core ? Path.GetFileName(folder) : folder);
            return null;
        }
        var hash = HashFolder(folder);

        await _gate.WaitAsync(ct);
        try
        {
            using (await _store.LockAsync(ct))
            {
                ReloadRecords();
                _records.TryGetValue(manifest.Id, out var previous);
                if (previous is not null && previous.Core == core && previous.PackageHash == hash) return null;
                if (core && previous is { Core: true } && CompareVersions(manifest.Version, previous.Version) < 0)
                {
                    _log.LogInformation("Core module {Id}: catalog has {Catalog}, this release ships {Shipped}; keeping the newer one",
                        manifest.Id, previous.Version, manifest.Version);
                    return null;
                }
                if (!core && previous is { Core: true })
                {
                    _log.LogError("Local module {Id} not installed: it is a core module, upgraded only by a WebApi release", manifest.Id);
                    return null;
                }
                if (core && previous is { Core: false })
                    _log.LogWarning("Uploaded module {Id} is replaced by the core module this release ships", manifest.Id);

                var prefix = manifest.RoutePrefix.Trim('/');
                var clash = _records.Values.FirstOrDefault(r => r.Id != manifest.Id && r.RoutePrefix.Equals(prefix, StringComparison.OrdinalIgnoreCase));
                if (clash is not null)
                {
                    _log.LogError("{Kind} module {Id} not installed: route prefix '{Prefix}' is used by module {Other}", kind, manifest.Id, prefix, clash.Id);
                    return null;
                }

                var moduleFolder = Path.Combine(_root, manifest.Id);
                if (Directory.Exists(moduleFolder)) Directory.Delete(moduleFolder, recursive: true); // replicas load from bytes
                CopyFolder(folder, Path.Combine(moduleFolder, manifest.Version));

                var settings = new Dictionary<string, string?>(manifest.DefaultSettings);
                if (previous is not null)
                    foreach (var (key, value) in previous.Settings)
                        if (settings.ContainsKey(key)) settings[key] = value;

                _records[manifest.Id] = new ModuleRecord
                {
                    Id = manifest.Id,
                    Name = manifest.Name,
                    Version = manifest.Version,
                    RoutePrefix = prefix,
                    Description = manifest.Description,
                    HotUnload = manifest.HotUnload,
                    EntryAssembly = entry,
                    Settings = settings,
                    DesiredState = previous?.DesiredState ?? ModuleState.Active,
                    InstalledAt = DateTimeOffset.UtcNow,
                    Core = core,
                    PackageHash = hash,
                    Permissions = manifest.Permissions.ToList()
                };
                _store.Save(_records.Values);
                _log.LogInformation("Installed {Kind} module {Id} {Version} ({Mode})", kind.ToLowerInvariant(), manifest.Id, manifest.Version,
                    previous is null ? "new" : "upgrade");
                return manifest.Id;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Tells every other replica that core modules were seeded (startup, after the first sync).</summary>
    public Task PublishCoreSeededAsync(IReadOnlyList<string> ids, CancellationToken ct) =>
        _bus.PublishAsync(new ModuleStateChanged(string.Join(',', ids), "core-seeded", _options.Pool, ModuleInstance.Id, DateTimeOffset.UtcNow), ct);

    /// <summary>
    /// Converges this replica on the catalog. Runs on startup, after each local lifecycle call and whenever another
    /// replica publishes <see cref="ModuleStateChanged"/>. Idempotent.
    /// </summary>
    public async Task<SyncResult> SyncAsync(CancellationToken ct)
    {
        var result = await ConvergeAsync(ct);
        // Released copies are waited on only now: ConvergeAsync's state machine (finished, so the runtime has cleared it)
        // held the copies in its hoisted locals (the foreach over _loaded), and its callers' frames would too, so a GC
        // loop inside it could never collect them.
        await CollectReleasedAsync(result);
        return result;
    }

    private async Task<SyncResult> ConvergeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using (await _store.LockAsync(ct)) ReloadRecords();

            var result = new SyncResult();
            foreach (var (id, module) in _loaded.ToArray())
            {
                _records.TryGetValue(id, out var record);
                var wanted = record is not null && record.DesiredState != ModuleState.Installed && _options.IsEnabled(id);
                if (!wanted)
                {
                    if (KeptUntilRestart(id, module, "unload")) // HotUnload = false: routes go, the copy stays
                    {
                        RemoveRoutes(id);
                        result.Unloaded[id] = new UnloadResult(id, Unloaded: false, GcPasses: 0,
                            Note: "HotUnload is false: routes removed; the loaded copy keeps running until the next restart (restartRequired).");
                        continue;
                    }
                    await UnloadLocalAsync(result, id, record?.Version ?? module.Version);
                }
                else if (module.InstalledAt != record!.InstalledAt || module.Settings != SettingsFingerprint(record))
                {
                    if (KeptUntilRestart(id, module, "swap")) continue; // the running copy keeps serving

                    // Re-installed or re-configured: swap to a freshly loaded copy, then release the old one.
                    try { result.Released.Add(await SwapAsync(record, module)); }
                    catch (Exception e) when (e is ModuleException or IOException or BadImageFormatException or TypeLoadException or InvalidOperationException)
                    {
                        _log.LogError(e, "Module {Id} could not be reloaded in pool {Pool}; keeping the running copy", id, _options.Pool);
                        result.Errors[id] = e;
                    }
                }
            }

            foreach (var record in _records.Values.Where(r => _options.IsEnabled(r.Id)))
            {
                try
                {
                    if (record.DesiredState == ModuleState.Active) await ActivateLocalAsync(record);
                    else RemoveRoutes(record.Id);
                }
                catch (Exception e) when (e is ModuleException or IOException or BadImageFormatException or TypeLoadException or InvalidOperationException)
                {
                    _log.LogError(e, "Module {Id} could not reach its desired state {State} in pool {Pool}", record.Id, record.DesiredState, _options.Pool);
                    result.Errors[record.Id] = e;
                }
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    private async Task<ModuleRecord> SetDesiredStateAsync(string id, ModuleState state, CancellationToken ct, bool deferred = false)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using (await _store.LockAsync(ct))
            {
                ReloadRecords();
                var record = Get(id);
                if (state == ModuleState.Installed) EnsureHotUnload(record, deferred, "unloaded");
                record.DesiredState = state;
                _store.Save(_records.Values);
                return record;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Applies the catalog here first, then tells every other replica to do the same.</summary>
    private async Task<SyncResult> ApplyAndPublishAsync(string moduleId, string action, CancellationToken ct)
    {
        var result = await SyncAsync(ct);
        await _bus.PublishAsync(new ModuleStateChanged(moduleId, action, _options.Pool, ModuleInstance.Id, DateTimeOffset.UtcNow), ct);
        return result;
    }

    private async Task ActivateLocalAsync(ModuleRecord record)
    {
        if (!_loaded.TryGetValue(record.Id, out var module))
        {
            _loaded[record.Id] = module = Load(record);
            _log.LogInformation("Loaded module {Id} {Version} in pool {Pool} ({Role})", record.Id, record.Version, _options.Pool, _options.Role);
            await module.HostedServices.StartAsync(_options.HostedServiceTimeout); // every role; failures are only reported
        }
        if (_options.Role != ModuleRole.Api || module.Routed) return;

        module.Endpoints ??= BuildEndpoints(record, module);
        _endpoints.Set(record.Id, module.Endpoints);
        module.Routed = true;
        _log.LogInformation("Mapped {Count} endpoints of module {Id} in pool {Pool}", module.Endpoints.Count, record.Id, _options.Pool);
    }

    private void RemoveRoutes(string id)
    {
        if (!_loaded.TryGetValue(id, out var module) || !module.Routed) return;
        _endpoints.Remove(id);
        module.Routed = false;
    }

    /// <summary>
    /// Blue/green on one replica: the new copy is loaded and its routes replace the old ones in a single data source
    /// update, so requests never see a 404 window; then the old copy is released.
    /// </summary>
    private async Task<ReleasedCopy> SwapAsync(ModuleRecord record, LoadedModule old)
    {
        var fresh = Load(record);
        try
        {
            if (_options.Role == ModuleRole.Api && record.DesiredState == ModuleState.Active)
            {
                fresh.Endpoints = BuildEndpoints(record, fresh);
                _endpoints.Set(record.Id, fresh.Endpoints); // replaces the old endpoints atomically
                fresh.Routed = true;
            }
            else
            {
                _endpoints.Remove(record.Id);
            }
        }
        catch
        {
            await ReleaseAsync(fresh, _hostRoot, _host.Services, _options.HostedServiceTimeout);
            throw;
        }

        _loaded[record.Id] = fresh;
        _log.LogInformation("Swapped module {Id} to {Version} ({InstalledAt}) in pool {Pool}", record.Id, record.Version, record.InstalledAt, _options.Pool);
        // Hosted services: the old copy's stop before the new copy's start, so two copies never consume or schedule at once.
        var released = await ReleaseCopyAsync(record.Id, old.Version, old);
        await fresh.HostedServices.StartAsync(_options.HostedServiceTimeout);
        return released;
    }

    private async Task UnloadLocalAsync(SyncResult result, string id, string version)
    {
        _endpoints.Remove(id);
        if (_loaded.Remove(id, out var module)) result.Released.Add(await ReleaseCopyAsync(id, version, module));
        else result.Unloaded[id] = new UnloadResult(id, Unloaded: true, GcPasses: 0, Note: "The module was not loaded on this replica.");
    }

    /// <summary>
    /// Disposes a module copy that no longer has routes and unloads its load context. Collection is waited on by
    /// <see cref="CollectReleasedAsync"/>, once the frames of the sync, which still reference the copy, are gone.
    /// </summary>
    private async Task<ReleasedCopy> ReleaseCopyAsync(string id, string version, LoadedModule? module)
    {
        // ApiExplorer keeps the last computed descriptions (module request/response types) until it is read
        // again; the routes are gone now, so recompute it without them.
        _ = _hostRoot.GetService<IApiDescriptionGroupCollectionProvider>()?.ApiDescriptionGroups;

        var (context, stopNote) = await ReleaseAsync(module!, _hostRoot, _host.Services, _options.HostedServiceTimeout);
        module = null; // the async state machine would otherwise keep the module alive
        return new ReleasedCopy(id, version, context, stopNote);
    }

    /// <summary>
    /// Waits for the released copies' load contexts to be collected and records the result per module. Collection
    /// needs several GCs (the loader allocator is freed by finalizers) and other threads, e.g. a request to the module
    /// that is still finishing, can briefly hold the last references, so it waits between passes instead of spinning.
    /// A copy still referenced after that is tracked under <see cref="PendingUnloadsAsync"/> and re-checked in the background.
    /// </summary>
    private async Task CollectReleasedAsync(SyncResult result)
    {
        if (result.Released.Count == 0) return;

        var passes = 0;
        for (; result.Released.Any(r => r.Context.IsAlive) && passes < 20; passes++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (result.Released.Any(r => r.Context.IsAlive)) await Task.Delay(50, CancellationToken.None);
        }

        var pending = false;
        foreach (var copy in result.Released)
        {
            var unloaded = !copy.Context.IsAlive;
            if (!unloaded)
            {
                lock (_unloading) _unloading[$"{copy.Id}@{copy.Version}#{Guid.NewGuid():n}"] = copy.Context;
                pending = true;
            }
            _log.LogInformation("Unloaded module {Id} {Version} in pool {Pool}: {Result} after {Passes} GC passes",
                copy.Id, copy.Version, _options.Pool, unloaded ? "collected" : "still referenced", passes);
            var note = unloaded ? null : "Not collected yet; tracked under GET /api/admin/modules/unloading and re-checked in the background. If it never clears, something still references the module's types.";
            result.Unloaded[copy.Id] = new UnloadResult(copy.Id, unloaded, passes, copy.StopNote is null ? note : $"{copy.StopNote} {note}".Trim());
        }
        result.Released.Clear();
        if (pending) StartUnloadRecheck();
    }

    /// <summary>When copies that were not collected in the request are re-checked (with GCs): an idle host runs no GC on its own.</summary>
    private static readonly TimeSpan[] UnloadRecheckDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    private int _recheckRunning;

    private void StartUnloadRecheck()
    {
        if (Interlocked.Exchange(ref _recheckRunning, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            var remaining = 0;
            try
            {
                foreach (var delay in UnloadRecheckDelays)
                {
                    await Task.Delay(delay);
                    remaining = CollectPending();
                    if (remaining == 0) break;
                }
                if (remaining > 0) _log.LogWarning("{Count} unloaded module copies are still referenced in pool {Pool}; see GET /api/admin/modules/unloading", remaining, _options.Pool);
            }
            catch (Exception e) { _log.LogError(e, "Re-checking unloaded module copies failed in pool {Pool}", _options.Pool); }
            finally { Volatile.Write(ref _recheckRunning, 0); }

            // A copy added while this loop was finishing found it still running and did not start another one.
            bool added;
            lock (_unloading) added = remaining == 0 && _unloading.Count > 0;
            if (added) StartUnloadRecheck();
        });
    }

    /// <summary>Runs a few GCs and drops the collected copies from <see cref="_unloading"/>; returns how many are left.</summary>
    private int CollectPending()
    {
        lock (_unloading) if (_unloading.Count == 0) return 0;
        for (var pass = 0; pass < 3; pass++) // the loader allocator needs more than one GC: finalizers free it
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        lock (_unloading)
        {
            foreach (var (key, context) in _unloading.ToArray())
            {
                if (context.IsAlive) continue;
                _unloading.Remove(key);
                _log.LogInformation("Unloaded module copy {Copy} was collected in pool {Pool}", key, _options.Pool);
            }
            return _unloading.Count;
        }
    }

    /// <summary>
    /// Host shutdown: stops every loaded module's hosted services (awaited, with the timeout) and disposes its
    /// services, so module consumers and schedulers end cleanly instead of being abandoned.
    /// </summary>
    public async Task StopAllAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var (id, module) in _loaded.ToArray())
            {
                _endpoints.Remove(id);
                _loaded.Remove(id);
                try { await ReleaseAsync(module, _hostRoot, _host.Services, _options.HostedServiceTimeout); }
                catch (Exception e) { _log.LogError(e, "Module {Id} did not shut down cleanly", id); }
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>HotUnload = false refuses unload/uninstall (409) unless the caller defers it to the next restart.</summary>
    private static void EnsureHotUnload(ModuleRecord record, bool deferred, string action)
    {
        if (!record.HotUnload && !deferred)
            throw new ModuleException($"Module '{record.Id}' cannot be {action} while the host runs (HotUnload is false). " +
                                      "Deactivate it, or repeat with ?deferred=true: routes go now, the loaded copies at the next restart.",
                StatusCodes.Status409Conflict);
    }

    /// <summary>A loaded copy with HotUnload = false is never released in place; it is kept until the next restart.</summary>
    private bool KeptUntilRestart(string id, LoadedModule module, string change)
    {
        if (module.HotUnload) return false;
        if (!module.RestartLogged)
            _log.LogWarning("Module {Id} has HotUnload = false: {Change} deferred, the running copy {Version} stays until the next restart", id, change, module.Version);
        module.RestartLogged = true;
        return true;
    }

    private async Task<ModuleStatus> StatusAsync(string id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return Status(Get(id)); }
        finally { _gate.Release(); }
    }

    /// <summary>Settings the module was configured with; a change in the catalog makes replicas reload it.</summary>
    private static string SettingsFingerprint(ModuleRecord record) =>
        string.Join('\n', record.Settings.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{s.Key}={s.Value}"));

    /// <summary>Caller holds the store lock.</summary>
    private void ReloadRecords() =>
        _records = _store.Load().ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

    private LoadedModule Load(ModuleRecord record)
    {
        var entry = Path.Combine(_root, record.Id, record.Version, record.EntryAssembly);
        if (!File.Exists(entry))
            throw new ModuleException($"The package files of '{record.Id}' are missing.", StatusCodes.Status500InternalServerError);

        var context = new ModuleLoadContext($"module:{record.Id}@{record.Version}", entry);
        try
        {
            var assembly = context.LoadWithoutLock(entry);
            var type = FindModuleType(assembly)
                ?? throw new ModuleException($"{record.EntryAssembly} no longer contains an IRuntimeModule.", StatusCodes.Status500InternalServerError);
            var module = (IRuntimeModule)Activator.CreateInstance(type)!;

            var services = new ServiceCollection();
            var moduleSettings = new Dictionary<string, string?>(record.Settings, StringComparer.OrdinalIgnoreCase);
            var hostConfiguration = _hostRoot.GetRequiredService<IConfiguration>();

            // The host's own overrides (Modules:Settings:{id}:{key}): applied over the catalog's settings on this
            // instance only, never written to modules.json. For what differs per deployment, e.g. a folder.
            var overrides = hostConfiguration.GetSection($"Modules:Settings:{record.Id}")
                .AsEnumerable(makePathsRelative: true).Where(p => p.Value is not null).ToList();
            foreach (var (key, value) in overrides) moduleSettings[key] = value;
            if (overrides.Count > 0)
                _log.LogInformation("Module {Id}: host configuration overrides {Keys}", record.Id, string.Join(", ", overrides.Select(p => p.Key)));

            if (record.Id.Equals("elsa", StringComparison.OrdinalIgnoreCase))
            {
                // Elsa is the only module allowed these runtime secrets. They are injected from the host's secret
                // provider into this module's private configuration and never copied into modules.json.
                moduleSettings["ConnectionStrings:elsa"] = hostConfiguration.GetConnectionString("elsa");
                moduleSettings["Identity:ElsaClient:ClientId"] = hostConfiguration["Identity:ElsaClient:ClientId"];
                moduleSettings["Identity:ElsaClient:ClientSecret"] = hostConfiguration["Identity:ElsaClient:ClientSecret"];
                moduleSettings["Elsa:IdentityAuthority"] = hostConfiguration["Elsa:IdentityAuthority"];
                moduleSettings["Elsa:PlatformApiBaseUrl"] = hostConfiguration["Elsa:PlatformApiBaseUrl"];
            }
            var settings = new ConfigurationBuilder().AddInMemoryCollection(moduleSettings).Build();
            module.ConfigureServices(services, settings);
            if (module is IModulePipeline) ModulePipelines.AddPipelineServices(services, _hostRoot, _log);

            // The module's own JsonOptions, configured exactly like the host's. Minimal API caches JSON metadata for
            // the module's request/response types inside these options; in the host's singleton they would pin the
            // module's assemblies forever.
            var json = new OptionsFactory<JsonOptions>(
                    _hostRoot.GetServices<IConfigureOptions<JsonOptions>>(),
                    _hostRoot.GetServices<IPostConfigureOptions<JsonOptions>>())
                .Create(Options.DefaultName);
            services.AddSingleton<IOptions<JsonOptions>>(Options.Create(json));

            var container = new ModuleContainer(services, _host.Services, _hostRoot, context);
            var routeModules = LoadableTypes(assembly)
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ICarterModule).IsAssignableFrom(t))
                .ToArray();
            var loaded = new LoadedModule(context, container, routeModules, record)
            {
                // Plain strings only: nothing of the module instance is kept.
                DeclaredPermissions = module.Manifest.Permissions.Select(p => p.Name).ToHashSet(StringComparer.Ordinal),
                HotUnload = module.Manifest.HotUnload,
                Pipeline = module as IModulePipeline,
                HostedServices = new ModuleHostedServices(record.Id, container, _log)
            };
            // Tenancy (per-tenant initialization) and other host observers; they let go in ReleaseAsync.
            foreach (var observer in _hostRoot.GetServices<IModuleLifecycleObserver>()) observer.Loaded(record.Id, container.Root);
            return loaded;
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    private IReadOnlyList<Endpoint> BuildEndpoints(ModuleRecord record, LoadedModule module)
    {
        var builder = new ModuleRouteBuilder(module.Container.Root);
        var group = builder.MapGroup($"{_routeBase}/{record.RoutePrefix}")
            .WithTags(record.Name)
            .WithMetadata(new RuntimeModuleMetadata(record.Id, record.Version));

        foreach (var type in module.RouteModules)
            ((ICarterModule)ActivatorUtilities.CreateInstance(module.Container.Root, type)).AddRoutes(group);
        if (module.Pipeline is { } pipeline) // catch-all /api/{prefix}/{**path}, validated like any module endpoint
            ModulePipelines.Map(group, pipeline, module.Container.Root, $"{_routeBase}/{record.RoutePrefix}", record.Id);

        var endpoints = builder.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        ModuleAuthorization.Validate(record.Id, module.DeclaredPermissions, endpoints, module.Context);
        return endpoints.Select(endpoint => WithModuleServices(endpoint, module.Container)).ToArray();
    }

    /// <summary>Runs the endpoint with RequestServices swapped for a module scope layered over the request scope.</summary>
    private static RouteEndpoint WithModuleServices(RouteEndpoint endpoint, ModuleContainer container)
    {
        var inner = endpoint.RequestDelegate ?? throw new InvalidOperationException($"Endpoint '{endpoint.DisplayName}' has no request delegate.");
        return new RouteEndpoint(async context =>
        {
            var hostServices = context.RequestServices;
            await using var scope = container.CreateScope(hostServices);
            context.RequestServices = scope;
            try { await inner(context); }
            finally { context.RequestServices = hostServices; }
        }, endpoint.RoutePattern, endpoint.Order, endpoint.Metadata, endpoint.DisplayName);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Context, string? StopNote)> ReleaseAsync(LoadedModule module, IServiceProvider hostRoot, IServiceCollection hostServices, TimeSpan hostedServiceTimeout)
    {
        var stopNote = await module.HostedServices.StopAsync(hostedServiceTimeout); // before anything is disposed
        foreach (var observer in hostRoot.GetServices<IModuleLifecycleObserver>())
            await observer.ReleasingAsync(module.Id, module.Container.Root);
        await module.Container.DisposeAsync();
        FrameworkCaches.Evict(module.Context, hostRoot, hostServices);
        var context = new WeakReference(module.Context);
        module.Context.Unload();
        return (context, stopNote);
    }

    /// <summary>Reads the manifest in a throw-away load context, so nothing stays loaded after install.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (string Entry, ModuleManifest Manifest) Inspect(string folder)
    {
        foreach (var dll in Directory.EnumerateFiles(folder, "*.dll"))
        {
            if (ModuleLoadContext.IsShared(Path.GetFileNameWithoutExtension(dll))) continue;
            var context = new ModuleLoadContext("inspect:" + Path.GetFileName(dll), dll);
            try
            {
                var type = FindModuleType(context.LoadWithoutLock(dll));
                if (type is null) continue;
                var manifest = ((IRuntimeModule)Activator.CreateInstance(type)!).Manifest;
                return (Path.GetFileName(dll), manifest with { DefaultSettings = new Dictionary<string, string?>(manifest.DefaultSettings) });
            }
            catch (BadImageFormatException) { } // native or non-.NET file
            finally { context.Unload(); }
        }
        throw new ModuleException("The package has no public, non-abstract IRuntimeModule implementation.");
    }

    private static async Task ExtractAsync(Stream content, string fileName, string folder, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".dll")
        {
            await using var file = File.Create(Path.Combine(folder, Path.GetFileName(fileName)));
            await content.CopyToAsync(file, ct);
            return;
        }
        if (extension is not (".zip" or ".nupkg"))
            throw new ModuleException("Upload a .dll, a .zip of the module's build output, or a .nupkg.");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        if (extension == ".zip")
        {
            ZipFile.ExtractToDirectory(buffer, folder); // rejects entries that escape the folder
            return;
        }

        // .nupkg: take lib/<best framework>/; package dependencies are NOT restored, ship them in the .zip form instead.
        using var package = new ZipArchive(buffer, ZipArchiveMode.Read);
        var frameworks = package.Entries
            .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) && e.Name.Length > 0)
            .GroupBy(e => e.FullName.Split('/')[1], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var framework = frameworks.Keys
                .Where(k => k.StartsWith("net", StringComparison.OrdinalIgnoreCase) && Version.TryParse(k[3..], out _))
                .OrderByDescending(k => Version.Parse(k[3..]))
                .FirstOrDefault()
            ?? throw new ModuleException("The .nupkg has no lib/netX.Y folder.");

        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        foreach (var entry in frameworks[framework])
        {
            var target = Path.GetFullPath(Path.Combine(folder, entry.FullName[$"lib/{framework}/".Length..]));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ModuleException("Invalid path in package.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void Validate(ModuleManifest manifest)
    {
        if (!Slug().IsMatch(manifest.Id)) throw new ModuleException($"Invalid module id '{manifest.Id}'.");
        if (!Slug().IsMatch(manifest.RoutePrefix.Trim('/'))) throw new ModuleException($"Invalid route prefix '{manifest.RoutePrefix}'.");
        if (!VersionText().IsMatch(manifest.Version)) throw new ModuleException($"Invalid version '{manifest.Version}'.");
    }

    /// <summary>SHA-256 over every file's relative path and content, in a stable order.</summary>
    private static string HashFolder(string folder)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                     .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
                     .Order(StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(file + "\n"));
            hash.AppendData(File.ReadAllBytes(Path.Combine(folder, file)));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void CopyFolder(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    /// <summary>Numeric comparison of the leading x.y.z part ("1.10.0" &gt; "1.9.0"); falls back to ordinal text.</summary>
    private static int CompareVersions(string left, string right)
    {
        static Version? Parse(string text) => Version.TryParse(text.Split('-', '+')[0], out var version) ? version : null;
        return Parse(left) is { } l && Parse(right) is { } r ? l.CompareTo(r) : string.CompareOrdinal(left, right);
    }

    private static Type? FindModuleType(Assembly assembly)
    {
        var types = LoadableTypes(assembly)
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(IRuntimeModule).IsAssignableFrom(t))
            .ToArray();
        return types.Length switch
        {
            0 => null,
            1 => types[0],
            _ => throw new ModuleException($"{assembly.GetName().Name} has more than one IRuntimeModule implementation.")
        };
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>(); }
    }

    private ModuleRecord Get(string id) =>
        _records.TryGetValue(id, out var record) ? record : throw new ModuleException($"Module '{id}' is not installed.", StatusCodes.Status404NotFound);

    private ModuleStatus Status(ModuleRecord record)
    {
        _loaded.TryGetValue(record.Id, out var module);
        // HotUnload = false: a change that needs a fresh copy (unload, upgrade, settings, pool) waits for a restart.
        var restartRequired = module is { HotUnload: false }
            && (module.InstalledAt != record.InstalledAt || module.Settings != SettingsFingerprint(record)
                || record.DesiredState == ModuleState.Installed || !_options.IsEnabled(record.Id));
        return new(record, _options.Pool, ModuleInstance.Id, _options.IsEnabled(record.Id), module is not null, _endpoints.Describe(record.Id))
        {
            LoadedVersion = module?.Version,
            RestartRequired = restartRequired,
            HostedServices = module?.HostedServices.Describe() ?? []
        };
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$")]
    private static partial Regex Slug();

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$")]
    private static partial Regex VersionText();

    private sealed class LoadedModule(ModuleLoadContext context, ModuleContainer container, Type[] routeModules, ModuleRecord record)
    {
        public string Id { get; } = record.Id;
        public ModuleLoadContext Context { get; } = context;
        public ModuleContainer Container { get; } = container;
        public Type[] RouteModules { get; } = routeModules;
        public IReadOnlyList<Endpoint>? Endpoints { get; set; }
        public bool Routed { get; set; }
        public IReadOnlySet<string> DeclaredPermissions { get; init; } = new HashSet<string>();
        public bool HotUnload { get; init; } = true;
        public bool RestartLogged { get; set; }
        /// <summary>The module instance, when it has a private pipeline; dropped with the copy.</summary>
        public IModulePipeline? Pipeline { get; init; }
        public required ModuleHostedServices HostedServices { get; init; }

        /// <summary>Which catalog entry this was loaded from; a re-install changes it and triggers a reload.</summary>
        public DateTimeOffset InstalledAt { get; } = record.InstalledAt;
        public string Version { get; } = record.Version;
        public string Settings { get; } = SettingsFingerprint(record);
    }
}

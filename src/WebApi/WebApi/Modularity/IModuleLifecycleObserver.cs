namespace soft.square.net.WebApi.Modularity;

/// <summary>
/// Host services told when a module copy is loaded on this replica and before it is released (unload, swap, failed load).
/// An observer must drop every reference to <c>moduleServices</c>, and finish any work using it, in
/// <see cref="ReleasingAsync"/>; otherwise the module's load context cannot be collected.
/// </summary>
internal interface IModuleLifecycleObserver
{
    void Loaded(string moduleId, IServiceProvider moduleServices);

    Task ReleasingAsync(string moduleId, IServiceProvider moduleServices);
}

/// <summary>
/// Host services told after every sync of this replica with the catalog, with the catalog's modules (data only, nothing
/// loaded), e.g. to keep broker queues for modules that are installed but not loaded.
/// </summary>
internal interface IModuleCatalogObserver
{
    Task CatalogSyncedAsync(IReadOnlyList<ModuleRecord> records, CancellationToken ct);
}

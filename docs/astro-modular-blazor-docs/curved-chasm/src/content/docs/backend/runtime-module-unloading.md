---
title: Unloading runtime modules
description: What keeps an unloaded module in memory, how each holder was found with heap dumps, bisecting and a reflection walk, and how it was fixed.
sidebar:
  order: 9
---

Removing a module's routes is easy. **Freeing its memory** means the runtime must collect its
`AssemblyLoadContext`, which only happens when nothing anywhere references any of its types. ASP.NET Core keeps
several process-wide caches that do exactly that.

## How the holders were found

1. Activate the sample module, optionally call it or generate its OpenAPI document, then unload it.
2. The unload checks a `WeakReference` to the load context after several garbage collections.
3. If it is still alive, take a heap dump and ask what keeps the module's `LoaderAllocator` alive:

```bash
dotnet-dump collect -p <pid> --type Heap -o module.dmp
dotnet-dump analyze module.dmp -c "dumpheap -type LoaderAllocator" -c "gcroot <address>"
```

4. If `gcroot` reports nothing useful, **bisect**: call one framework component at a time until the pin appears, then
   **walk the statics by reflection** from the suspect type. `gcroot` can miss a holder entirely (see the
   `TypeDescriptor` case below).
5. Fix the holder, repeat.

## What held modules, and the fixes

| Holder | Fix |
| --- | --- |
| The host's `JsonOptions.SerializerOptions` caches JSON metadata for the module's request and response types, created when endpoints are built | Each module gets its **own** `JsonOptions`, configured by the host's own configure actions, so the cache dies with the module |
| The host DI container caches every type it is asked about | Types that mention a module type are never resolved by the host (see the [overlay container](/backend/runtime-modules/#2-the-di-container-is-frozen-after-startup)) |
| `ParameterBindingMethodCache`, a static cache of Minimal API parameter binding. It is shared source: Http.Extensions, ApiExplorer and OpenApi each compile their own copy | Module types evicted from every copy, by reflection |
| `TypeDescriptor` caches | The framework's hot-reload `[MetadataUpdateHandler]` `ClearCache` hooks, plus eviction of `s_defaultProviderInitialized` |
| `ReflectTypeDescriptionProvider._typeData`: per-type data of `TypeDescriptor`'s default provider, reached through `TypeDescriptor.s_providerTable` → node → provider. The hot-reload hook does not clear it | Evicted by walking the provider chain (`FrameworkCaches.EvictTypeDescriptionProviders`), plus that type's static Type-keyed tables |
| The DI-level `EndpointDataSource` never refreshed, so it kept old endpoints (and served stale OpenAPI) | Subscribe to it once at startup with `GetChangeToken()` |
| ApiExplorer keeps the last computed descriptions | Recompute them right after the module's routes are removed |
| OpenAPI singletons cache schema ids by type and operation contexts by id | Their caches are cleared on unload; the next document request rebuilds them |
| EF Core's static internal service-provider cache (model cache keyed by the module's DbContext type) | Avoided: each module has its own EF internal provider (`TenantEfServices` in `WebApi.Data`), disposed with the module |
| OpenTelemetry `LogRecordSharedPool` (2048 slots): idle records keep their last exception, state and attributes. Found after EF logged a `DbUpdateException` (→ change tracker → module DbContext) | Idle records' data cleared on unload (`FrameworkCaches.ClearIdleLogRecords`) |
| System.Linq.Expressions interpreter: `InstructionList.s_loadFields` (by `FieldInfo`), filled when EF evaluates a closure sub-expression such as `Where(i => i.Sku == sku.Trim())`; it kept the specification's closure field, hence the module. Related: `Expression.s_lambdaFactories`, `s_lambdaDelegateCache`, `CallInstruction.s_cache`, `DelegateHelpers._DelegateCache`. `gcroot` showed only the module's own handles; found by bisecting request kinds, then a reflection walk of every static | All cleared on unload, under their own locks where they have them (`FrameworkCaches.ClearExpressionCaches`) |

### The OpenAPI pin: found by bisecting

For a long time a module stayed in memory after any OpenAPI document had been generated while it was active, and the
heap dump showed "no managed roots". Bisecting found the cause:

1. Calling ApiExplorer's `EndpointMetadataApiDescriptionProvider` once, without any OpenAPI code and without its cache,
   was enough to pin the module.
2. Inside it, `TypeDescriptor.GetConverter(moduleType)` alone pinned it. `ModelMetadata.IsComplexType` makes that call
   for every parameter and response type.
3. The holder is the default `ReflectTypeDescriptionProvider`'s instance dictionary `_typeData`, reached through
   `TypeDescriptor.s_providerTable` → node → provider.
4. `dotnet-dump gcroot` never reported this path: it showed only weak handles. A reflection walk of the statics found it.

Unload, re-activate, regenerate the document and unload again now works repeatedly.

### The unload request held the module itself

Until 2026-11-23 every unload, even of the trivial sample, reported `unloaded=false` after its 20 GC passes, and the
copy was collected only by a later request. A heap dump taken *during* those passes showed a single root:
`ModuleRuntime.SyncAsync`'s async state machine → `LoadedModule` → the module's load context. The variables of its
`foreach` over the loaded modules are hoisted into the state machine (they live across an `await`), and the GC loop
ran inside that call, so it could never succeed. (The routing data sources had already dropped the module's endpoints.)

The sync now runs in two steps: `ConvergeAsync` releases the copies and keeps only weak references to their load
contexts, then `CollectReleasedAsync` runs the GC passes once `ConvergeAsync` has finished and its state is gone.

Collection still needs a few passes: the loader allocator is freed by finalizers, so the unload waits between passes
instead of spinning. It now takes about 3 passes. A copy that something else still holds briefly (a request to the
module that is still finishing) is listed under `GET /api/admin/modules/unloading` and **re-checked in the
background** after 1 s, 5 s, 30 s, 2 min and 10 min, since an idle host runs no GC on its own. Reading `/unloading`
also runs GC passes, so the list is accurate whenever it is read.

## Results with the sample module

Re-measured on 2026-11-11 (Greeting, fresh process per run, two runs each):

| Generated while the module was active, then unload | Before the `TypeDescriptor` fix | Now |
| --- | --- | --- |
| Nothing (activate, call GET and POST, unload) | Collected by +5 s | Collected by +5 s |
| The host document (`/openapi/webapi.json`) | Not collected after 20 s | **Collected by +5 s** |
| The module's own document (`/openapi/modules/greeting.json`) | Not collected after 20 s | **Collected by +5 s** |

| Other scenario | Result |
| --- | --- |
| Repeated unload and activate cycles | Collected every time |
| Layered `inventory` module with EF Core, specifications and tenant databases on four engines (2 core replicas, [lab](/backend/hybrid-lab/)) | Collected on both replicas within about 10 s, in repeated cycles, after the OpenTelemetry and expression-cache fixes. Without them, every copy that logged an EF error or ran the SKU specification stayed loaded |
| A module protected with `RequirePermission` (403, then 200 after a grant) | Collected; the authorization pipeline holds no module types |
| Upgrades and settings changes across 8 replicas ([lab](/backend/hybrid-lab/)) | Before the 2026-11-23 fix, swapped-out copies often stayed under `/unloading` for a while; later heap dumps showed only the active copy in each replica |

Re-measured on 2026-11-23, after the `SyncAsync` fix (single Development host):

| Scenario | Result of the unload request |
| --- | --- |
| Greeting: calls, its OpenAPI document, then unload (Debug and Release host) | `unloaded=true` after 3 GC passes |
| Greeting: settings change (swap) | Old copy collected after 4 GC passes |
| Storage: EF Core migrations and queries on PostgreSQL, the AWS SDK, its OpenAPI document | `unloaded=true` after 3 GC passes |
| A copy held for 3 s on purpose | `unloaded=false`, then collected by the background re-check; `/unloading` empty |

The unload result reports what really happened (`unloaded`, `gcPasses`), so the admin is never told memory was
freed when it was not.

## Caveats

- Most fixes reach into **framework internals** by reflection. Re-verify them after every .NET upgrade; the unload
  result and the `/unloading` list show at once when one breaks.
- A module with **static state, threads or timers** that it does not stop on dispose will never unload. Modules
  must release everything in `IDisposable` / `IAsyncDisposable` services.

## Recommendation

| Operation | Treat as |
| --- | --- |
| Install, activate, deactivate | Fully live: no restart |
| Upgrade, settings change | Live swap with no 404 window; the old copy's memory is reclaimed within the request |
| Unload | Routes gone immediately; memory reclaimed within the request (`unloaded=true`), or by the background re-check, confirmed by `/unloading` |
| Guaranteed memory reclamation | A rolling restart, which the [replicated deployment](/backend/deployment-scenarios/) does anyway |

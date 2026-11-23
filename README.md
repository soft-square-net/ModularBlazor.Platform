# ModularBlazor.Platform

The ModularBlazor platform: the WebApi host and its core modules, the identity service, the Blazor WebAssembly client
(BlzWebClient), the Aspire lab, and the NuGet packages plus Module SDK that modules build against. It replaces the
earlier `soft-square-net/ModularBlazor` repository.

Modules live in their own repositories and build against the packages (see
[Modules outside this repo](#modules-outside-this-repo-the-platform-packages)):

| Repository | Holds |
| --- | --- |
| [ModularBlazor.ModuleTemplate](https://github.com/soft-square-net/ModularBlazor.ModuleTemplate) | the `dotnet new` template for a new module |
| [ModularBlazor.Samples](https://github.com/soft-square-net/ModularBlazor.Samples) | small demo modules |
| [ModularBlazor.Module.Content](https://github.com/soft-square-net/ModularBlazor.Module.Content) | newsroom posts, taxonomy, review and publishing |
| [ModularBlazor.Module.Shifts](https://github.com/soft-square-net/ModularBlazor.Module.Shifts) | desks, shifts and rota |
| [ModularBlazor.Module.Storage](https://github.com/soft-square-net/ModularBlazor.Module.Storage) | file storage |
| [ModularBlazor.Module.Elsa](https://github.com/soft-square-net/ModularBlazor.Module.Elsa) | the Elsa 3 workflow engine |

## Blazor WebAssembly client (`src/apps/blazor/ModularBlazor.BlzWebClient`)

A Blazor WebAssembly app that ships no features of its own: it loads them as **client modules** (`.dll`, `.nupkg` or
`.zip`) at runtime, and its **layouts** are modules too. It signs in with the identity service (OpenID Connect,
Authorization Code + PKCE) and talks to the WebApi through the YARP gateway with the user's access token.

| Phase | When | What happens |
|---|---|---|
| 1. Startup | before the first render | `ModuleManager.LoadStartupModulesAsync` fetches the client manifest from the WebApi (`GET api/client-modules/manifest`; anonymous before sign-in) and loads every startup module (layouts, the MudTail theme). If the WebApi cannot be reached, the last anonymous manifest this browser received is used, with the module files it cached. |
| 2. Sign-in, sign-out, now and then | `RefreshAsync` | The user's manifest: the server applies roles and tenant. No module code is downloaded yet; the menu is built from it. An unchanged manifest costs a 304 (ETag). |
| 3. On navigation | `EnsureLoadedAsync`, from the router | The URL is matched against each module's route prefixes. A module that isn't loaded is downloaded with its dependencies and its assemblies are added to the router. Modules marked `preload` load in the background. |

## Run it

Run the whole platform with the Aspire lab (see [Server: WebApi](#server-webapi)); the client is at
http://localhost:5070. The client's own settings (identity service, gateway) are in
`src/apps/blazor/ModularBlazor.BlzWebClient/wwwroot/appsettings.json`.

Target framework is set once in `Directory.Build.props` (net10.0). If you use another version, change it there and the
`10.0.*` package versions (for example in `modules/client/Directory.Build.props`).

## Layout

```
src/ModularBlazor.Abstractions          client-module contracts: [ClientModule], [LayoutKey], [UseLayout], IModuleNavigation
src/apps/blazor/ModularBlazor.BlzWebClient  the client: ModuleManager, ActiveModuleService, App.razor (Router), ModuleRouteView
src/WebApi                              the server: WebApi host, contracts, data layer, core modules, identity service
src/Aspire/Hybird                       Aspire lab: gateway, WebApi pools, identity, client, PostgreSQL, Redis, RabbitMQ
modules/client/Layouts.Default          startup   - sidebar layout, MudTail runtime   [LayoutKey("default")]
modules/client/Layouts.Admin            lazy      - admin top-bar layout (dependency of Module.Admin) [LayoutKey("admin")]
modules/client/Module.Admin             lazy      - /admin, /admin/members (role: platform-admin)
modules/client/Module.Tenants           lazy      - tenant management, with the Tenancy API client
```

The client modules in `modules/client` ship with every WebApi release: the WebApi builds them into
`CoreClientModules/` and its client-modules core module seeds them into its catalog (see `src/WebApi/WebApi/WebApi.csproj`).
Any other client module is uploaded in the admin area (`/client-modules`), usually from its own repository.

## Writing a module

A module is a Razor class library. In this repo it lives at `modules/client/<Name>/<Name>.csproj`, whose folder's
`Directory.Build.props` references `ModularBlazor.Abstractions`; in its own repository it uses the Module SDK (see
[Modules outside this repo](#modules-outside-this-repo-the-platform-packages)). Pages use `@page` as usual. The
Dashboard sample in [ModularBlazor.Samples](https://github.com/soft-square-net/ModularBlazor.Samples) is the smallest
example.

* **Settings:** `[assembly: ClientModule(...)]` describes the module to the catalog: name, `LoadOnStartup`, `Layout`,
  `RoutePrefixes`, `Dependencies` (other client modules, loaded first), `HostDependencies` (WebApi modules it needs),
  `RequiredRole` / `AvailableToRoles`, `Styles`, `Scripts` and the menu entry (`NavTitle`, `NavHref`, `NavOrder`). An
  admin can change them after upload.
* **Menus:** the client has three menus (zones): `main`, `secondary` and `user`. A module puts links and groups
  (submenus) in them with assembly attributes, read from its metadata on upload like `[ClientModule]`:

  ```csharp
  [assembly: MenuGroup("admin", "Administration", Href = "admin", Zone = "secondary", Order = 90)]
  [assembly: MenuLink("members", "Members", "admin/members", Parent = "admin")]
  // another module joins that group by its full id, "{moduleId}:{id}":
  [assembly: MenuLink("tenants", "Tenants", "tenants", Zone = "secondary", Parent = "Module.Admin:admin")]
  ```

  The links are in the menu before the module is downloaded and leave it when the module leaves the user's catalog
  (deactivated, deleted, no longer for their roles or tenant). A link goes to one of the module's own pages (under its
  route prefixes); the upload warns about and leaves out anything else (external links, other modules' pages). Titles
  are English and translated by the module's `i18n/*.json`. A child shows in its parent's zone; if the parent's module
  is not there, at the top of its own zone; a group with nothing in it and no `Href` is hidden. A module without menu
  attributes keeps its `NavTitle` entry, as a link in `main`. Layouts show a zone with
  `<MenuZone Zone="@MenuZones.Main" />` or build their own from `IMenuService.Zone(...)`.

  Administrators change the menus on the client's **Menus** page (`/menus`), kept by the WebApi's `menus` core module
  (`/api/menus`): hide, rename (per language), move to another menu or group and reorder any entry by its id, and add
  links and groups of their own, to pages of the app or to external `http(s)` addresses (opened in a new tab), for
  everyone, signed-in users or visitors only. There are two layers: the platform's (every tenant; platform admins,
  without a tenant) and each tenant's on top of it (the tenant's `menus.manage` permission, or a platform admin naming
  the tenant). The module never reads the modules' entries: it stores changes by id and the client applies them, so an
  entry whose module leaves the catalog simply has nothing to change. Added entries are known as `@platform:{id}` and
  `@tenant:{id}`; a save sends the version it read (409 when someone saved meanwhile).

  An entry can show a **badge** (a count, "new") from the module's own API:

  ```csharp
  [assembly: MenuLink("tenants", "Tenants", "tenants", Zone = "secondary", Parent = "Module.Admin:admin",
      Badge = "api/tenancy/subscriptions/attention", BadgeRefresh = 300)]
  ```

  The client asks the url (relative to the WebApi, it must start with `api/`) with the user's token when the entry is
  in the menu, then every `BadgeRefresh` seconds (15-3600; default 60). The answer is a number, an array (its length)
  or `{"count": 3, "tone": "warning", "label": "..."}` (or `"text"` instead of `count`, up to 12 characters); tones
  are `neutral`, `info`, `success`, `warning` and `error`. 0, an empty array or 204 shows no badge; an error (403, 404)
  shows none and is asked again after ten minutes. Entries with the same url share one request. A loaded module's code
  injects `IMenuBadges`: `RefreshAsync(entryId)` after it changed what is counted, `Set(entryId, badge)` for an entry
  without a url. Layouts style `.menu-badge` and `.menu-badge--{tone}`.
* **Layout module:** a `LayoutComponentBase` with `@attribute [LayoutKey("my-layout")]`. Any module can ship one; a
  module's pages choose it with `Layout` in `[ClientModule]`, or a page can pick with `@attribute [UseLayout("my-layout")]`.
  Layout precedence: page `@layout` > `[UseLayout]` > the module's `Layout` > `default`.
* **Services:** inject `IModuleNavigation` and the other contracts from `ModularBlazor.Abstractions` (provided by the
  client). Don't reference the client project.
* **.dll:** upload just the module's `.dll`. **.nupkg:** `dotnet pack`; the loader takes `lib/<best tfm>/*.dll` and
  everything under `staticwebassets/`. **.zip:** the module's assembly plus its private assemblies (a generated API
  client, for example).
* **Static files (css/js):** every file under a module's `wwwroot` is embedded in its assembly
  (`modules/client/Directory.Build.targets`, or the Module SDK), so a `.dll` module needs nothing else. Reference them
  relative to `wwwroot` (`css/a.css`, `js/module.js`; `wwwroot/` and `_content/<asm>/` prefixes are ignored).
* **Module script:** an ES module whose `default` export (or `init`) runs on activation with `{ moduleId }`, and
  whose `dispose()` (on the module or on the object it returns) runs on deactivation. `Layouts.Admin/node-app`
  builds one with vite (`dotnet build -p:BuildNodeApp=true`). `*.lib.module.js` initializers never run for modules
  loaded at runtime; the client runs `js/module.js` itself.

### Modules outside this repo: the platform packages

A module can live in its own repository (for example `soft-square-net/ModularBlazor.Module.<Name>`) and build against
NuGet packages instead of this repo's source. `build/pack-platform.ps1` packs them, all in one version
(`build/Platform.Packaging.props`), and `.github/workflows/platform-packages.yml` publishes them to GitHub Packages on a
`v*` tag or a manual run:

| Package | For |
| --- | --- |
| `soft.square.net.WebApi.Contracts` | `IRuntimeModule`, `ModuleManifest`, tenancy and permission contracts |
| `soft.square.net.WebApi.SharedKernel` | entities, `ITenantOwned`, repositories, specifications |
| `soft.square.net.WebApi.Data` | `TenantDbContext`, `EfRepository`, EF Core with the four providers |
| `soft.square.net.ModularBlazor.Abstractions` (+ `Share.All`) | client modules: `[ClientModule]`, MudTail components, `HttpClientNames` |
| `soft.square.net.ModularBlazor.Module.Sdk` | the build setup: picks the references above per project, ships none of them |

A module project adds one line, and pins the SDK version (which is also the platform version) once in `global.json`:

```xml
<Project Sdk="Microsoft.NET.Sdk">            <!-- or Microsoft.NET.Sdk.Razor for a client module -->
  <Sdk Name="soft.square.net.ModularBlazor.Module.Sdk" />
```

```json
{ "msbuild-sdks": { "soft.square.net.ModularBlazor.Module.Sdk": "1.0.0" } }
```

The SDK reads the project kind from its name (`*.Domain`, `*.Application`, `*.Infrastructure`, any other server
project, or a Razor project for a client module), the same rules as `src/WebApi/Modules/Directory.Build.props` here.
The build output holds only the module's own assemblies and its private dependencies. GitHub Packages needs a token
with `read:packages` to restore, even for public packages, so add the feed with credentials to the module's `NuGet.Config`.

## Things to know

* **Trimming is disabled** in the host (`PublishTrimmed=false`). Modules are unknown at publish time and may use any
  framework API; a trimmed host would throw `MissingMethodException`. Keep it off.
* **Load once, activate per page.** `ModuleManager` loads a module's code once. `ActiveModuleService` follows
  navigation: startup modules (layouts, login) stay active, the current route's module (plus its `dependencies`)
  gets its styles/scripts applied before the page renders, and they are removed when you leave. Scripts re-run
  on every activation, so write them to be disposed.
* **Unloading.** `ModuleManager.Unload(id)` removes a module's routes, layouts, assets, styles and scripts, and
  `ReloadAsync(id)` swaps in a new file. Each module has its own load context; the assembly *memory* is only freed
  when the runtime supports collectible contexts (`ModuleOptions.CollectibleLoadContexts`, off by default and
  experimental on WebAssembly). Logging out removes the user's metadata and menu, and `ModuleRouteView`
  re-checks auth and the module's `RequiredRole` on every page render.
* **NuGet dependencies are not resolved.** Only the assemblies inside the package are loaded. Bundle private
  dependencies in the package's `lib/<tfm>/` folder; anything already loaded in the host is skipped.
* **Version alignment.** Modules compile against the host's framework version; keep them on the same major.
* **Security.** Loading code is code execution. Serve modules from a trusted origin, check the hash the catalog records for each upload, and enforce authorization on the server too: client-side checks are UX, not protection.
* **Sub-path hosting.** Set `<base href>` in `wwwroot/index.html`; all module URLs and routes are base-relative.

## Server: WebApi

The server is the WebApi host (`src/WebApi/WebApi`): runtime modules (installed, activated, unloaded while it runs),
core modules (identity, tenancy, client-modules), tenants with their own databases, and module pools behind a YARP
gateway. Its README is [src/WebApi/WebApi/README.md](src/WebApi/WebApi/README.md). The identity service is
`src/WebApi/Identity/WebApi.Identity.Server`.

Run the whole platform (gateway, WebApi pools, identity service, Blazor client, PostgreSQL, Redis, RabbitMQ) with the
Aspire lab, [src/Aspire/Hybird](src/Aspire/Hybird/README.md):

```bash
dotnet run --project src/Aspire/Hybird --launch-profile http
```

## Naming

All assemblies, root namespaces and namespaces are prefixed with `soft.square.net` (e.g. `soft.square.net.WebApi.Contracts`,
`soft.square.net.Module.Admin`). The prefix is the `SoftSquarePrefix` property in the root `Directory.Build.props`, which also sets
`AssemblyName` and `RootNamespace` for every project. Client module **ids** (`Module.Admin`, ...) stay short and
logical; only the files carry the prefix (`soft.square.net.Module.Admin.dll`).

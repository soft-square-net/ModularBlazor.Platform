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

## Modular Blazor WebAssembly client

A standalone Blazor WebAssembly app that loads its features as **modules** (`.dll` or `.nupkg`) at runtime,
with pluggable **layouts** that are modules too.

| Phase | When | What happens |
|---|---|---|
| 1. Startup | before first render | the core manifest comes from the API's Catalog (`GET /api/modules/core`, public; `modules/core.json` is only the offline fallback); every module with `loadOnStartup` is downloaded (default layout, login page + auth layout). |
| 2. After login (and after a page reload while signed in) | `JwtAuthenticationService.LoginAsync`, then the login page | The user's module **metadata** is fetched from the Catalog (`GET /api/me/modules`; the `role` claims in the token pick the modules). No module code is downloaded yet; the menu is built from the metadata. |
| 3. On navigation | `Router.OnNavigateAsync` | The URL is matched against each module's `routePrefixes`. If the module isn't loaded, it (and its `dependencies`) is downloaded, its assemblies are added to `Router.AdditionalAssemblies`, and routing continues. |

## Run it

```bash
./build-modules.sh            # or .\build-modules.ps1  - builds every module in modules/client into src/apps/blazor/ModularBlazor.Client/wwwroot/modules
dotnet run --project src/apps/blazor/ModularBlazor.Client
```

Sign in with any user/password. User name `admin` gets the admin modules (and the lazily loaded admin layout);
anything else gets Dashboard + Reports. The Home page shows each module's state as you navigate.

Target framework is set once in `Directory.Build.props` (net10.0). If you use another version, change it there,
the `10.0.*` package versions in `src/ModularBlazor.Client/*.csproj` and `modules/client/Directory.Build.props`.

## Layout

```
src/ModularBlazor.Abstractions   contracts: ModuleDescriptor, [LayoutKey], [UseLayout], IAuthService, IModuleNavigation
src/ModularBlazor.Client         host: ModuleManager, NuPkgReader, App.razor (Router), ModuleRouteView
src/api/_Framework/Core          shared kernel (BaseEntity, IAggregateRoot, IRepository, IDbInitializer, DatabaseProvider); carries the MediatR, FluentValidation and Ardalis.Specification packages
src/api/_Framework/Infrastructure  multi-tenancy, EF Core providers, ModuleDbContext, Ardalis repository, MediatR/validation wiring (AddHostModules)
modules/host/Auth, Catalog, Todo API host modules, each as Domain / Application / Infrastructure projects (see below)
modules/client/Layouts.Default          dll, startup   - sidebar layout            [LayoutKey("default")]
modules/client/Layouts.Admin            dll, lazy      - admin top-bar layout (dependency of Module.Admin) [LayoutKey("admin")]; WebApi client
modules/client/Module.Dashboard         dll, lazy      - /dashboard
modules/client/Module.Reports           nupkg, lazy    - /reports, /reports/{id}, ships reports.css
modules/client/Module.Admin             dll, lazy      - /admin, /admin/members (role: platform-admin); WebApi client (BlzWebClient)
```

**`modules/client` is the default folder for client modules.** `build-modules.ps1` / `build-modules.sh` build every
`<Name>/<Name>.csproj` in it, so there is no module list to keep up to date. A module whose csproj sets `<PackageId>` is packed
as a `.nupkg`; any other module ships as its `.dll`. To build another folder, run `.\build-modules.ps1 -ModulesDir <dir>`
(sh: `./build-modules.sh Release <dir>`). A module created there also picks up `modules/client/Directory.Build.props` /
`.targets` (the Abstractions reference and the embedded `wwwroot` assets). Add it to the `/modules/client/` solution folder
of `ModularBlazor.slnx`.

## Module descriptor (JSON)

```json
{
  "id": "Module.Reports", "name": "Reports", "version": "1.0.0",
  "kind": "nupkg",                       // "dll" | "nupkg"
  "url": "modules/Module.Reports.1.0.0.nupkg",
  "sha256": "optional hex digest of the downloaded file",
  "loadOnStartup": false,
  "requiresAuth": true, "requiredRole": "admin",
  "routePrefixes": ["reports"],          // base-relative, no leading slash
  "layout": "default",                   // key of a layout registered with [LayoutKey]
  "dependencies": ["Layouts.Admin"],     // loaded first
  "extraAssemblies": [],                 // dll only: private dependency dlls
  "styles": ["css/reports.css"],         // path under the module's wwwroot (packaged in the module), or a url
  "scripts": ["js/module.js"],           // optional; defaults to js/module.js when the module has one
  "hidden": false,
  "nav": [{ "title": "Reports", "href": "reports", "order": 20 }]
}
```

## Writing a module

A module is a Razor class library at `modules/client/<Name>/<Name>.csproj` that references `ModularBlazor.Abstractions`
(the folder's `Directory.Build.props` adds the reference). Pages use `@page` as usual. `Module.Dashboard` is the smallest example.

* **Layout module:** a `LayoutComponentBase` with `@attribute [LayoutKey("my-layout")]`. Any module can ship one; a
  module's pages choose it via `"layout"` in the descriptor, or a page can pick with `@attribute [UseLayout("my-layout")]`.
  Layout precedence: page `@layout` > `[UseLayout]` > descriptor `layout` > `default`.
* **Services:** inject `IAuthService` / `IModuleNavigation` (provided by the host). Don't reference the host project.
* **.dll:** build and publish just `Module.X.dll`. **.nupkg:** `dotnet pack`; the loader takes `lib/<best tfm>/*.dll`
  and everything under `staticwebassets/`.
* **Static files (css/js):** every file under a module's `wwwroot` is embedded in its assembly
  (`modules/client/Directory.Build.targets`), so a `.dll` module needs nothing else. Reference them in the
  descriptor relative to `wwwroot` (`css/a.css`, `js/module.js`; `wwwroot/` and `_content/<asm>/` prefixes are ignored).
* **Module script:** an ES module whose `default` export (or `init`) runs on activation with `{ moduleId }`, and
  whose `dispose()` (on the module or on the object it returns) runs on deactivation. `Layouts.Admin/node-app`
  builds one with vite (`dotnet build -p:BuildNodeApp=true`). `*.lib.module.js` initializers never run for modules
  loaded at runtime; the host runs `js/module.js` itself.

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
  re-checks auth and `requiredRole` on every page render.
* **NuGet dependencies are not resolved.** Only the assemblies inside the package are loaded. Bundle private
  dependencies in the package's `lib/<tfm>/` folder; anything already loaded in the host is skipped.
* **Version alignment.** Modules compile against the host's framework version; keep them on the same major.
* **Security.** Loading code is code execution. Serve modules from a trusted origin, set `sha256` in descriptors that
  come from your API, and enforce authorization on the server too: client-side checks are UX, not protection.
* **Hosting `.dll` files.** Some static hosts (IIS, some CDNs) refuse to serve `.dll`. Rename the files and change
  `url` in the descriptor; the loader reads the assembly name from the file's metadata, not from the file name.
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

All assemblies, root namespaces and namespaces are prefixed with `soft.square.net` (e.g. `soft.square.net.ModularBlazor.Core`,
`soft.square.net.Module.Dashboard`). The prefix is the `SoftSquarePrefix` property in the root `Directory.Build.props`, which also sets
`AssemblyName` and `RootNamespace` for every project. Module **ids** in the manifests (`Module.Dashboard`, ...) stay short and
logical; only the served files carry the prefix (`modules/soft.square.net.Module.Dashboard.dll`,
`soft.square.net.Module.Reports.1.0.0.nupkg`).

### Client authentication (JWT)

* `ModularBlazor.Infrastructure` (`src/apps/blazor`): `JwtAuthenticationService` is the `AuthenticationStateProvider`,
  `IAuthenticationService` and `IAccessTokenProvider` in one singleton. Tokens live in local storage
  (`authToken`, `refreshToken`); the user, roles, tenant and permissions are the **claims of the access token**
  (`sub`, `name`, `role`, `tenant`, `permission`), so there is no extra permissions call after sign-in.
* Two API clients: `IApiClient` sends `Authorization: Bearer` and `X-Tenant` (the token's tenant); the keyed
  `ApiClientNames.Anonymous` client sends nothing (sign-in, refresh, the public core manifest). The token handler
  never redirects before a call; a 401 on a call that carried a token sends the user to `login`.
* The token is refreshed a minute before it expires. If the refresh token is rejected the session ends.
* Modules read the user through `ICurrentUserService` (`UserInfo`: name, roles, tenant), never the provider.
* An unkeyed `HttpClient` is the framework's: this site's origin. The API is only reachable through the clients above.

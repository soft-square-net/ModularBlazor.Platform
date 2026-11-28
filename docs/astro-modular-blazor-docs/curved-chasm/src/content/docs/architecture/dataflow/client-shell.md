---
title: Client shell (MudTail, layouts, admin)
description: Level-1 dataflow of the core client modules UI.MudTail, Layouts.Default, Layouts.Public, Layouts.Admin, Module.Admin and Module.Tenants inside the Blazor WebAssembly client.
sidebar:
  order: 4
---

These core client modules live in Platform `modules/client`. They run in the browser, and other client modules
contribute to them through contribution points.

```mermaid
flowchart LR
  User(["User"]):::ext

  subgraph Browser["Browser: ModularBlazor.BlzWebClient"]
    direction TB
    H1("4.1 Module loader"):::client
    H2("4.2 Auth + HttpClients"):::client
    H3("4.3 LiveChannel"):::client
    MT("UI.MudTail"):::client
    LD("Layouts.Default"):::client
    LP("Layouts.Public"):::client
    LA("Layouts.Admin"):::client
    MA("Module.Admin"):::client
    MTn("Module.Tenants"):::client
    Feat("Feature client modules"):::client
  end

  GW{{"Gateway"}}:::ext
  IdP(["Identity service"]):::ext

  User <--> LD & LP
  H1 <-->|F1| GW
  H1 --> MT & LD & LP & LA & MA & MTn & Feat
  LD -.- MT
  LP -.- MT
  MA -.- LA
  Feat -->|F2| LD
  LD <-->|F3| GW
  MTn <-->|F4| GW
  MA <-->|F5| IdP
  H2 <-->|F6| IdP
  H3 <-->|F7| GW

  classDef client fill:#173b35,stroke:#45c2a1,color:#e5fff5
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
```

| Flow | Route or channel | Payload |
|---|---|---|
| F1 | `GET api/client-modules/manifest`, `files/{id}/{version}/...` | → `ClientModuleManifest`; then each module's assemblies and `Styles` |
| F2 | contribution points: menus (`MenuGroup`, `MenuLink`), dock panels, dialogs, studio panels; `Layout = default` or `public` | Razor components, `MenuItem` |
| F3 | `api/identity/me`, `api/menus`, `api/branding` | → `CurrentPrincipal`, menus, `BrandLayer` |
| F4 | `api/tenancy/tenants`, `subscriptions/attention` (`Core.Tenancy.Client`) | → `TenantInfo`, count |
| F5 | `api/admin/tenants/{tenant}/members` on the identity service | invite (email, role, displayName), suspend, activate, remove |
| F6 | OIDC sign-in and token refresh; named clients `WebApiClient`, `WebApiPublicClient`, `IdentityClient` | tokens |
| F7 | SignalR `/hubs/live` (`HubLiveTransport`) | `Subscribe(topic)`, `LiveMessage` |

| Client module | Manifest (`[assembly: ClientModule]`) |
|---|---|
| `UI.MudTail` | LoadOnStartup, Hidden, RequiresAuth = false, `css/mudtail.min.css` |
| `Layouts.Default` | LoadOnStartup, Hidden, `Dependencies = UI.MudTail` |
| `Layouts.Public` | Hidden, RequiresAuth = false, `Dependencies = UI.MudTail` |
| `Layouts.Admin` | Hidden, AvailableToRoles = platform-admin |
| `Module.Admin` | RequiredRole = platform-admin, Layout = admin, `Dependencies = Layouts.Admin`, menu group `admin` |
| `Module.Tenants` | RequiredRole = platform-admin, Layout = default, a menu link under `Module.Admin:admin` with the badge `api/tenancy/subscriptions/attention` |

The dotted lines are the manifest `Dependencies`.

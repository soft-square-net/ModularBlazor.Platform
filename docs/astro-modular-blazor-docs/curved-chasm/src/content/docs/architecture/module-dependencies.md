---
title: Module dependencies
description: Every server and client module, core and custom, with their package references, runtime dependencies, broker events and pools, read from the csproj files, manifests and gateway routes.
sidebar:
  order: 2
---

This page shows how the modules depend on each other. Each diagram shows **one kind** of coupling, so no diagram
has to draw them all at once. Shapes and lines follow the [legend](../).

## The modules

| Module (id) | Repository | Server | Client modules | Kind |
|---|---|---|---|---|
| `identity` | Platform `src/WebApi/Modules/Core` | `IdentityModule` (`/api/identity`) | | core |
| `tenancy` | Platform | `TenancyModule` (`/api/tenancy`) | `Module.Tenants` | core |
| `client-modules` | Platform | `ClientModulesModule` (`/api/client-modules`) | | core |
| `menus` | Platform | `MenusModule` (`/api/menus`) | | core |
| `branding` | Platform | `BrandingModule` (`/api/branding`) | | core |
| Identity service | Platform `src/WebApi/Identity` | OpenIddict server: `/connect/*`, `/api/admin/*`, `/api/service/*` | | core (its own process) |
| Shell | Platform `modules/client` | | `UI.MudTail`, `Layouts.Default`, `Layouts.Public`, `Layouts.Admin`, `Module.Admin` | core client |
| `organization` | Module.Organization | central API + host connector (`/api/organization`) | `Module.Organization` | custom, optional |
| `shifts` | Module.Shifts | `/api/shifts` | `Module.Shifts` | custom |
| `content` | Module.Content | `/api/content` | `Module.Content` | custom |
| `newsroom` | Module.NewsRoom | `/api/newsroom` | `Module.NewsRoom` | custom |
| `elsa` | Module.Elsa | Elsa 3.9 engine (`/api/elsa`) | | custom (workflow pool) |
| `dynamicfields` | Module.DynamicFields | `/api/dynamicfields` | `Module.DynamicFields` | custom |
| `contentdesigner` | Module.ContentDesigner | `/api/contentdesigner` | `Module.ContentDesigner` | custom |
| `storage` | Module.Storage | `/api/storage` | `Module.Storage`, `Module.Storage.Shares` | custom |
| `taxonomies` | Module.Taxonomies | `/api/taxonomies` | `Module.Taxonomies` | custom |
| `keywords` | Module.Keywords | `/api/keywords` | `Module.Keywords` | custom |
| `ai` | Module.Ai (local only) | `/api/ai` | `Module.Ai` | custom |
| `identity-admin` | Module.Admin.OpenIddict (local only) | `/api/identity-admin` | `Module.IdentityAdmin` | custom |
| `greeting`, `inventory`, `pulse` | Samples | sample modules | `Module.Dashboard`, `Module.Reports` | sample |
| `modulename` | ModuleTemplate | the Clean Architecture template (`notes`) | `Module.ModuleName` | template |

Every custom module also ships a hidden `WebApi.Modules.{Name}.Client` client module. It holds the generated typed API
client, and the module's own client module lists it in `Dependencies`.

## 1. Package and project references (build time)

These references are fixed at build time. The Module SDK (`ModularBlazor.Module.Sdk`, `Sdk.props`) adds the
platform packages to every module project. Only the cross-module contracts are added by hand.

```mermaid
flowchart LR
  subgraph Custom["Custom server modules"]
    direction TB
    Nr["newsroom"]:::custom
    Sh["shifts"]:::custom
    Co["content"]:::custom
    Cd["contentdesigner"]:::custom
    Org["organization"]:::custom
    Df["dynamicfields"]:::custom
    St["storage"]:::custom
    Tx["taxonomies"]:::custom
    Kw["keywords"]:::custom
    Ai["ai"]:::custom
    Ia["identity-admin"]:::custom
    El["elsa"]:::custom
  end

  subgraph ModulePkgs["Module contracts packages"]
    direction TB
    NrC["NewsRoom.Contracts<br/>NewsRoomEventTypes"]:::contract
    ShC["Shifts.Contracts<br/>ShiftsEvents"]:::contract
    ContC["Content.Contracts<br/>ContentEventTypes, ContentLiveTopic"]:::contract
    OrgCl["Organization.Client<br/>generated HTTP client"]:::contract
    OrgC["Organization.Contracts<br/>IOrganizationClient, OrgEvents, DTOs"]:::contract
  end

  subgraph PlatformPkgs["Platform packages (soft.square.net.*, v1.0.0-preview.8)"]
    direction TB
    Sdk["ModularBlazor.Module.Sdk"]:::contract
    Contracts["WebApi.Contracts<br/>IRuntimeModule, ModuleManifest,<br/>IIntegrationEvents, ILiveNotifier,<br/>WorkItems, Lookups"]:::contract
    Kernel["WebApi.SharedKernel"]:::contract
    Data["WebApi.Data<br/>TenantDbContext, outbox, inbox"]:::contract
    Abs["ModularBlazor.Abstractions<br/>ILiveChannel, shell, dialogs"]:::contract
  end

  subgraph Third["Third-party packages"]
    direction TB
    ElsaPkgs(["Elsa 3.9: Workflows.Api,<br/>Runtime.Distributed,<br/>Persistence.EFCore.PostgreSql"]):::ext
    S3(["AWSSDK.S3"]):::ext
    Ollama(["OllamaSharp"]):::ext
  end

  Nr --> NrC & ContC & OrgC & ShC
  Sh --> ShC & OrgCl & OrgC
  Co --> ContC & OrgC
  Cd --> OrgC
  Org --> OrgC
  Custom -->|"every module, via the SDK"| Sdk
  Sdk --> Contracts & Kernel & Data & Abs
  El --> ElsaPkgs
  St --> S3
  Ai --> Ollama

  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef contract fill:#342d16,stroke:#d1a94d,color:#fff2c6
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
```

The host and the core modules reference the platform projects directly, not through the packages:

```mermaid
flowchart LR
  subgraph Host["Platform host (src/WebApi)"]
    WebApi["WebApi host<br/>(module runtime, gateway target)"]:::core
    Id["identity"]:::core
    Te["tenancy"]:::core
    Cm["client-modules"]:::core
    Me["menus"]:::core
    Br["branding"]:::core
  end
  IdSrv["Identity service<br/>(OpenIddict)"]:::core
  WebApi --> ContractsP["WebApi.Contracts"]:::contract
  WebApi --> DataP["WebApi.Data"]:::contract
  WebApi -->|"CoreModuleProject<br/>(copied to CoreModules, not referenced)"| Id & Te & Cm & Me & Br
  subgraph Browser["Blazor WASM client (src/apps/blazor)"]
    Blz["ModularBlazor.BlzWebClient"]:::client
    CmC["Core.ClientModules.Client"]:::client
    MeC["Core.Menus.Client"]:::client
    TeC["Core.Tenancy.Client"]:::client
    Tenants["Module.Tenants"]:::client
  end
  Blz --> AbsP["ModularBlazor.Abstractions"]:::contract
  Blz --> WClient["WebApi.Client"]:::contract
  Blz --> CmC & MeC
  Tenants --> TeC
  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef client fill:#173b35,stroke:#45c2a1,color:#e5fff5
  classDef contract fill:#342d16,stroke:#d1a94d,color:#fff2c6
```

## 2. Runtime dependencies

### Client modules: manifest `Dependencies` and `Layout`

The browser loads a client module only after the modules it lists in `Dependencies`. A module's `Layout` names the
layout module that hosts its pages.

```mermaid
flowchart BT
  MT["UI.MudTail<br/>LoadOnStartup"]:::client
  LD["Layouts.Default<br/>LoadOnStartup, layout 'default'"]:::client
  LP["Layouts.Public<br/>layout 'public'"]:::client
  LA["Layouts.Admin<br/>layout 'admin', platform-admin"]:::client
  LD ==> MT
  LP ==> MT

  MA["Module.Admin"]:::client ==>|Dependencies| LA
  MTen["Module.Tenants"]:::client ==>|Layout| LD

  subgraph Feature["Custom client modules (Layout = default)"]
    direction LR
    COrg["Module.Organization"]:::client
    CSh["Module.Shifts"]:::client
    CCo["Module.Content"]:::client
    CNr["Module.NewsRoom"]:::client
    CCd["Module.ContentDesigner"]:::client
    CDf["Module.DynamicFields"]:::client
    CSt["Module.Storage"]:::client
    CTx["Module.Taxonomies"]:::client
    CKw["Module.Keywords"]:::client
    CAi["Module.Ai"]:::client
    CIa["Module.IdentityAdmin"]:::client
  end
  Feature ==>|Layout| LD
  Shares["Module.Storage.Shares<br/>RequiresAuth = false"]:::client ==>|Layout| LP
  Feature ==>|"Dependencies: own API client"| ApiCl["WebApi.Modules.{Name}.Client<br/>(hidden, generated I{Name}Client)"]:::client
  Shares ==> ApiCl
  classDef client fill:#173b35,stroke:#45c2a1,color:#e5fff5
```

### Server modules: HTTP calls through the gateway

These calls are made at run time, from a module's `Infrastructure` layer to another module's `/api/{prefix}/...`.
Each call carries the caller's token, or the module's service token. Without its target, a call degrades or
fails, but the module still loads. The table gives the routes the calling code uses.

```mermaid
flowchart LR
  subgraph Callers[" "]
    direction TB
    El["elsa"]:::custom
    Cd["contentdesigner"]:::custom
    Nr["newsroom"]:::custom
    Ai["ai"]:::custom
    Ia["identity-admin"]:::custom
  end
  subgraph Mid[" "]
    direction TB
    Co["content"]:::custom
    Sh["shifts"]:::custom
  end
  subgraph Targets[" "]
    direction TB
    Df["dynamicfields"]:::custom
    Org["organization<br/>(host connector)"]:::custom
    OrgC["organization<br/>(central service)"]:::custom
    IdSrv["Identity service"]:::core
    Search["search<br/>(planned, S1)"]:::planned
  end

  Co ==>|H1| Df
  Co ==>|H2| Org
  Nr ==>|H3| Co
  Nr ==>|H4| Df
  Nr ==>|H2| Org
  Cd ==>|H5| Co
  Cd ==>|H6| Df
  Cd ==>|H2| Org
  Sh ==>|H7| OrgC
  El ==>|H8| Co
  El ==>|H9| Nr
  El ==>|H10| Org
  El ==>|H11| Sh
  Ai ==>|H12| Co
  Ai ==>|H13| Df
  Ia ==>|H14| IdSrv
  Org ==>|H15| OrgC
  Nr ==>|H16| Sh
  Ai -.-o|planned| Search

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef planned fill:#16161d,stroke:#9a9bb3,stroke-dasharray:6 4,color:#b8b8c8
  style Callers fill:none,stroke:none
  style Mid fill:none,stroke:none
  style Targets fill:none,stroke:none
```

| Call | Caller → target | Routes |
|---|---|---|
| H1 | content → dynamicfields | `catalog/root-post-fields`, `fields/{key}/versions/{n}`, `content/validate`, `content/validate-node` |
| H2 | content, newsroom, contentdesigner → organization | `local/units...` |
| H3 | newsroom → content | `posts`, `categories`, `contents?assigneeId` (workload reconcile) |
| H4 | newsroom → dynamicfields | `catalog/root-post-fields`, `fields/...`, `content/validate-node` |
| H5 | contentdesigner → content | `categories`, `contents`, `posts/{id}`, `posts/{id}/events`, `public/{publicId}` |
| H6 | contentdesigner → dynamicfields | `catalog/root-post-fields`, `fields/{key}/versions/{n}`, `content/jsonld`, `content/validate` |
| H7 | shifts → central organization | the central API as the caller (`IOrganizationClient`) |
| H8 | elsa → content | `posts/{id}`, `unit`, `assign`, `accept`, `transfer`, `schedule`, `publish`, `contents/{id}`, `contents/{id}/assign`, `contents/{id}/unit`; the poller: `automation/*`, `posts/publish-due` |
| H9 | elsa → newsroom | `bindings/resolve`, `projects/{id}`, `projects/{id}/workflow`, `projects/{id}/state`, `routing/route`, `workload/query` |
| H10 | elsa → organization | `local/units/{id}`, `members`, `categories` |
| H11 | elsa → shifts | `shifts?from&to`, `shifts/{id}`; the poller: `shifts/ended`, `overdue` |
| H12 | ai → content | `posts/{id}` |
| H13 | ai → dynamicfields | `fields/{key}/versions/{n}`, `content/validate-node` |
| H14 | identity-admin → identity service | `api/service/*` (client credentials) |
| H16 | newsroom → shifts | `shifts?from&to` (workload reconcile) |
| H15 | organization connector → central service | `seeds`, `events?after=`, `links` (`Organization:BaseUrl`) |

`taxonomies`, `keywords` and `storage` call no other module. They are leaves: others reach them only from the
browser, through pickers and lookups.

## 3. Event-only coupling (RabbitMQ `integration`)

The publisher only writes an outbox row. The subscriber lists the pattern in its manifest `Subscriptions`, and the
host binds `integration.{module}.{pool}` to it, so events wait while the subscriber is unloaded. Neither side
references the other.

```mermaid
flowchart LR
  Org["organization<br/>(replays the central feed)"]:::custom
  Sh["shifts"]:::custom
  Co["content"]:::custom
  Nr["newsroom"]:::custom
  El["elsa"]:::custom
  Host["Platform host<br/>TenantSeedReconciler"]:::core
  Hooks["webhooks<br/>(planned)"]:::planned

  Org -.->|E1| Sh
  Org -.->|E2| Co
  Org -.->|E3| Nr
  Org -.->|E4| Host
  Sh -.->|E5| Co
  Sh -.->|E6| El
  Co -.->|E7| Nr
  Co -.->|E8| El
  Nr -.->|E9| El
  Sh -.->|E10| Nr
  Org -.-o|planned| Hooks

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef planned fill:#16161d,stroke:#9a9bb3,stroke-dasharray:6 4,color:#b8b8c8
```

| Flow | Event types |
|---|---|
| E1 | `org.unit-*.v1`, `org.membership-changed.v1` |
| E2 | `org.unit-*.v1` |
| E3 | `org.unit-moved.v1` |
| E4 | `tenancy.seed-changed.v1` (a host subscription) |
| E5 | `shifts.section-moved.v1` |
| E6 | `shifts.published`, `member-added`, `checked-in`, `checked-out`, `handover`, `ended` (`.v1`) |
| E7 | `content.post-state-changed.v1`, `content.post-categories-changed.v1`, `content.work-item-*.v1`, `content.content-deleted.v1`, `content.content-archived.v1` |
| E8 | `content.post-*.v1`, `content.work-item-*.v1` |
| E9 | `newsroom.project-approved.v1`, `newsroom.project-reapproved.v1`, `newsroom.project-state-changed.v1`, `newsroom.project-binding-available.v1` |
| E10 | `shifts.published`, `updated`, `cancelled`, `checked-in`, `checked-out`, `ended`, `section-moved` (`.v1`) |

**No subscriber yet.** These events are published, but no module subscribes to them:

- Organization: `org.person-changed`, `org.position-changed`, `org.tenant-seed-changed`, `org.tenant-link-changed`
  (the planned Webhooks module will act on tenant links)
- Shifts: `shifts.member-removed`
- Content: `content.content-attached`, `detached`, `created`, `changed`, `state-changed`, `assigned`, and `content.validation-failed`
- NewsRoom: no other module takes `newsroom.proposal-*`, `newsroom.project-details-changed` or `newsroom.work-item-*`; NewsRoom consumes them itself, for its live boards and its workload model

The `*.work-item-assigned.v1` and `*.work-item-state-changed.v1` events of Content and NewsRoom come from the shared
work-item contract in `WebApi.Contracts/WorkItems` (`PublishWorkItemAssigned`, `PublishWorkItemStateChanged`).

**Live updates.** Content, Shifts, Organization, Elsa and Ai also push live messages through `ILiveNotifier`. They travel
over the `live` fanout to the hub at `/hubs/live`, and the browser reads them through `ILiveChannel`. This is not
module-to-module coupling: the [context diagram](../context-dataflow/) shows it.

## 4. Pools: who runs where

The pools come from `src/Aspire/Hybird/AppHost.cs`: `Modules__Pool`, `Modules__Role`, `Modules__Enabled__n` and
`Modules__Excluded__n`, plus the YARP routes. Every replica of every pool runs the same WebApi image.

```mermaid
flowchart TB
  Browser(["Browser: Blazor WASM client :5070"]):::ext
  IdSrv["Identity service :5090<br/>OpenIddict, identity DB"]:::core
  GW{{"YARP gateway :5080"}}:::ext
  Browser -->|"OIDC sign-in"| IdSrv
  Browser ==>|"Bearer token"| GW

  GW ==>|"/api/sales, /api/greeting,<br/>/api/shifts, /api/content"| Sales
  GW ==>|"/api/elsa"| Workflow
  GW ==>|"/hubs/live and the catch-all"| Core

  subgraph Core["core pool: Api, 2 replicas (every module except the excluded ones)"]
    direction LR
    c1["identity, tenancy, client-modules, menus, branding"]:::core
    c2["organization, newsroom, dynamicfields, contentdesigner,<br/>storage, taxonomies, keywords, ai, identity-admin,<br/>inventory, pulse, and any newly uploaded module"]:::custom
  end
  subgraph Sales["sales pool: Api, 4 replicas"]
    s1["shifts, content, greeting, sales"]:::custom
  end
  subgraph Worker["worker pool: Worker, 2 replicas, no public routes"]
    w1["sales, greeting (consumers and jobs)"]:::custom
  end
  subgraph Workflow["workflow pool: Api, 2 replicas"]
    f1["elsa (+ ConnectionStrings:elsa)"]:::custom
  end
  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
```

All pools share RabbitMQ, Redis and the PostgreSQL server `tenant-data`. That server holds the tenant store
(`HybirdTenants`, schema `platform`) and one database per tenant. The identity service has its own `identity`
database, and Elsa has `ElsaWorkflows`. The central Organization service needs its own central database
(`CentralDatabaseMigrator`). The lab runs it inside the core pool.

---
title: Tenancy, client modules, menus and branding
description: Level-1 dataflow of the core modules tenancy, client-modules, menus and branding, with their stores and payloads.
sidebar:
  order: 3
---

Four core modules in every pool. They serve the browser's shell: the tenants and their subscriptions, the client
module manifest and files, the menus, and the branding.

```mermaid
flowchart LR
  Browser(["Browser shell"]):::ext
  PAdmin(["Platform admin"]):::ext
  TAdmin(["Tenant admin"]):::ext

  subgraph Core["core modules"]
    direction TB
    T1("3.1 tenancy: tenants"):::core
    T3("3.2 tenancy: provision"):::core
    T4("3.3 tenancy: subscriptions"):::core
    C1("3.4 client-modules:<br/>manifest + files"):::core
    C3("3.5 client-modules: admin"):::core
    C4("3.6 LocalClientModuleWatcher"):::core
    M1("3.7 menus"):::core
    B1("3.8 branding"):::core
  end

  TSt[("platform.tenants,<br/>platform.tenant_subscriptions")]:::store
  TDb[("tenant databases")]:::store
  CCat[("client module catalog<br/>files")]:::store
  MSt[("MenuStore JSON")]:::store
  BSt[("BrandingStore JSON + images")]:::store
  MQ[("RabbitMQ")]:::store

  Browser <-->|F1| T1
  PAdmin <-->|F2| T1
  PAdmin <-->|F3| T3
  PAdmin <-->|F4| T4
  Browser <-->|F5| T4
  Browser <-->|F6| C1
  PAdmin <-->|F7| C3
  Browser <-->|F8| C4
  Browser <-->|F9| M1
  TAdmin <-->|F10| M1
  Browser <-->|F11| B1
  TAdmin <-->|F12| B1
  T1 & T4 <--> TSt
  T1 -->|F13| MQ
  T3 -->|F14| TDb
  C1 & C3 & C4 <--> CCat
  M1 <--> MSt
  B1 <--> BSt

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
```

| Flow | Route or channel | Payload |
|---|---|---|
| F1 | `GET /api/tenancy/tenants`, `tenants/{id}`, `current` | → `TenantInfo` |
| F2 | `POST tenants`, `PATCH tenants/{id}`, `{id}/activate`, `suspend` | `CreateTenantRequest`, `UpdateTenantRequest` → `TenantInfo` |
| F3 | `POST tenants/{id}/provision` | → the tenant database is created, and each module's migrations run |
| F4 | `GET subscriptions`, `tenants/{id}/subscriptions`; `POST` add, `{sid}/extend`, `{sid}/cancel` | `NewSubscription` → `TenantSubscription`, `TenantSubscriptionStatus` |
| F5 | `GET subscriptions/attention` (the menu badge of `Module.Tenants`) | → count |
| F6 | `GET /api/client-modules/manifest` (ETag), `files/{id}/{version}/{file}`, `files/{id}/{version}/{token}/{**path}` | → `ClientModuleManifest(Etag, Modules)`, `_framework` `.wasm`, `.css`, `.js` |
| F7 | `modules`, `modules/{id}`, `POST uploads`, `uploads/{token}/install`, `PUT modules/{id}/settings`, `enable`, `disable`, `versions/{v}/activate`, `DELETE` | package, `InstallClientModule`, `ClientModuleSettings` → catalog entries |
| F8 | `GET dev/events` (server-sent events, when local reload is on) | `hello`, `changed` + module ids |
| F9 | `GET /api/menus` | → combined menus (platform + tenant, `MenuItem` tree) |
| F10 | `GET/PUT /api/menus/admin` | `SaveMenuCustomization` |
| F11 | `GET /api/branding`, `assets/{folder}/{name}` | → effective `BrandLayer`, images |
| F12 | `GET/PUT /api/branding/admin`, `POST admin/assets/{kind}` | `SaveBrandLayer`, image file |
| F13 | fanout `tenants.changed` | a reload signal |
| F14 | `DatabaseServers:*` (PostgreSQL, SQL Server, MySQL, Oracle) | a database per tenant, a schema per module |

None of the four modules publishes or consumes integration events.

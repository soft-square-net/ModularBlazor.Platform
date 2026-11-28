---
title: Platform host (runtime, messaging, live)
description: "Level-1 dataflow of the WebApi host: module runtime and catalog, integration outbox and inbox, the broker fanouts, the live hub, tenancy, grants and lookups."
sidebar:
  order: 1
---

The host (`src/WebApi/WebApi`) runs in every pool replica. The modules do not call its processes: the host
provides them through `WebApi.Contracts` and `WebApi.Data`. They are drawn here because every module's flows pass
through them.

```mermaid
flowchart LR
  Admin(["Platform admin"]):::ext
  Browser(["Browser"]):::ext
  Mod("Any runtime module<br/>(own container, own ALC)"):::custom

  subgraph Host["WebApi host"]
    direction TB
    P1("1.1 Module runtime"):::core
    P2("1.2 Module state sync"):::core
    P3("1.3 Outbox sweeper"):::core
    P4("1.4 Integration consumer"):::core
    P5("1.5 Dead letters"):::core
    P6("1.6 Live hub + LiveBus"):::core
    P7("1.7 Tenant directory<br/>+ seed reconciler"):::core
    P8("1.8 Grants"):::core
    P9("1.9 Lookups"):::core
  end

  Cat[("module catalog<br/>App_Data/modules-catalog")]:::store
  TS[("tenant store<br/>schema platform")]:::store
  OB[("{module}.integration_outbox<br/>{module}.integration_inbox")]:::store
  MQ[("RabbitMQ")]:::store

  Admin <-->|F1| P1
  Admin <-->|F2| P5
  Browser <-->|F3| P6
  Browser <-->|F4| P9
  Mod <-->|F5| P1
  Mod -->|F7| OB
  Mod <-->|F10| P4
  Mod -->|F12| P6
  Mod <-->|F15| P7
  Mod <-->|F17| P9
  P1 <--> Cat
  P2 <-->|F6| MQ
  P3 <--> OB
  P3 -->|F8| MQ
  P4 <--> OB
  P4 <-->|F9| MQ
  P5 <-->|F11| MQ
  P6 <-->|F13| MQ
  P7 <--> TS
  P7 <-->|F14| MQ
  P8 <--> TS
  P8 <-->|F16| MQ

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
```

| Flow | Route or channel | Payload |
|---|---|---|
| F1 | `/api/admin/modules`: `POST` (multipart), `{id}/activate`, `deactivate`, `unload`, `PUT {id}/settings`, `DELETE {id}` | package `.dll`, `.zip` or `.nupkg`, settings → module list and state |
| F2 | `/api/admin/integration/dead-letters`, `{moduleId}`, `{moduleId}/replay` | → dead-lettered envelopes |
| F3 | SignalR `/hubs/live`: `Subscribe(topic)`, `Unsubscribe(topic)` | → `LiveSubscribeResult`; pushes `LiveMessage(Topic, Type, Payload, Sequence, At)` |
| F4 | `GET /api/lookups`, `/api/lookups/{name}?q`, `{name}/keys`, `POST {name}/query` | `LookupQuery`, `LookupKeysRequest` → `LookupPage` (`LookupItem`), `LookupDescriptor[]` |
| F5 | load into a collectible ALC, map its Carter routes under `/api/{RoutePrefix}` | `ModuleManifest(Id, Name, Version, RoutePrefix, Permissions, LiveTopics, Subscriptions, HotUnload)` |
| F6 | fanout `modules.state` | module id + state; each replica re-syncs |
| F7 | `IIntegrationEvents.Publish(type, data, subject, unitPath, orgId)` | an outbox row in the same `SaveChanges` |
| F8 | topic exchange `integration` (routing key = type) | `IntegrationEnvelope(Id, Type, TenantId, Subject, OccurredAt, CorrelationId, Source, Data, OrgId, UnitPath)` |
| F9 | durable queue `integration.{module}.{pool}`, bound to the manifest's `Subscriptions` | `IntegrationEnvelope` |
| F10 | `IIntegrationEventHandler.HandleAsync`, in one transaction with the inbox row | `envelope.DataAs<T>()`, into the subscriber's own record |
| F11 | `integration.dlx`: an envelope lands there after the retries; the admin replays it | `IntegrationEnvelope` |
| F12 | `ILiveNotifier.PublishAsync(topic, type, payload, seq)` | checked against the manifest's `LiveTopics` |
| F13 | fanout `live` | `LiveMessage(TenantId, Topics, Type, Payload, Sequence, ...)` |
| F14 | fanout `tenants.changed`; host subscription `tenancy.seed-changed.v1` | a reload signal; the seed-changed event triggers a reconciliation |
| F15 | `ITenantSeedSource` (the Organization connector) and `ITenantInitializer` (each module) | `DeploymentSeedDto` → `TenantInfo`, then a `SeedReportRequest` back |
| F16 | fanout `access.grants.changed` | a signal: drop the grant caches and re-check the live groups |
| F17 | the lookup's owner module answers it over HTTP | `LookupQuery` → `LookupPage` |

**Background services:** `ModuleReconciler`, `LocalModuleWatcher`, `ModuleStateSubscriber`, `OutboxSweeper<T>`,
`RabbitMqHostIntegrationSubscriber`, `LiveSubscriber`, `TenantDirectoryService`, `TenantChangeSubscriber`,
`TenantSeedReconciler` and `GrantChangeSubscriber`.

**Tables (schema `platform`, in the tenant store):** `tenants`, `access_grants`, `tenant_subscriptions`.

**Shared work-item contract (`WebApi.Contracts/WorkItems`):** `WorkItemAssigned(item, previousAssignee)` and
`WorkItemStateChanged(item, previousState)`. A module publishes them as `{module}.work-item-*.v1`.

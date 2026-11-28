---
title: "Context: dataflow level 0"
description: The platform's context - its actors and external systems, and every module as a process, with the HTTP requests, integration events and live messages between them, and their payload types.
sidebar:
  order: 3
---

Level 0 draws the platform from the outside first. Then it opens the platform up to show the modules as processes.

- Each edge carries a short flow id (F1, F2, ...). The table under each diagram gives that flow's transport and its
  payloads.
- The payloads are the request and response DTOs of the generated typed clients (`I{Name}Client`), and the event
  records in each module's `Contracts` package.
- Shapes follow the [legend](../). **Processes** are rounded.

## 0. The platform as one process

```mermaid
flowchart LR
  subgraph Actors[" "]
    direction TB
    Reader(["Reader<br/>(anonymous)"]):::ext
    Staff(["Staff: member, journalist,<br/>moderator, shift manager"]):::ext
    Admins(["Tenant admin, platform admin,<br/>org (customer) admin"]):::ext
    Browser(["Browser: Blazor WASM<br/>+ client modules"]):::ext
  end

  P("Modular Blazor platform<br/>gateway, module pools,<br/>runtime, modules"):::core

  subgraph Ext[" "]
    direction TB
    IdP(["Identity service<br/>OpenIddict"]):::ext
    Central(["Central Organization<br/>service"]):::ext
    LLM(["AI provider"]):::ext
    MQ[("RabbitMQ")]:::store
    Redis[("Redis")]:::store
    TStore[("Tenant store")]:::store
    TDb[("Tenant databases")]:::store
    ElsaDb[("ElsaWorkflows DB")]:::store
    Blob[("Object storage")]:::store
  end

  Reader & Staff & Admins -->|F1| Browser
  Browser <-->|F2| IdP
  Browser <-->|F3| P
  Browser <-->|F4| P
  P <-->|F5| IdP
  P <-->|F6| Central
  P <-->|F7| MQ
  P <-->|F8| Redis
  P <-->|F9| TStore
  P <-->|F10| TDb
  P <-->|F11| ElsaDb
  P <-->|F12| Blob
  P <-->|F13| LLM

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
  style Actors fill:none,stroke:none
  style Ext fill:none,stroke:none
```

| Flow | Transport | Payload |
|---|---|---|
| F1 | UI interaction | clicks, forms, uploads |
| F2 | OIDC authorization code + PKCE, refresh | → access token (JWT: sub, tenant, roles), id token |
| F3 | HTTPS + Bearer, `/api/{module}/...` through the gateway | JSON DTOs → JSON DTOs, `ProblemDetails`, files |
| F4 | SignalR `/hubs/live` | `Subscribe(topic)` → `LiveMessage(Topic, Type, Payload, Sequence, At)` |
| F5 | `client_credentials` service tokens; `/api/service/*` admin calls | → tokens, members, accounts, applications |
| F6 | `seeds?deployment`, `events?after=`, `links`, `reports` | → `DeploymentSeedDto`, `FeedPage`, `TenantLinkDto`; `SeedReportRequest` |
| F7 | exchanges `integration` (topic), `integration.dlx`; fanouts `modules.state`, `tenants.changed`, `access.grants.changed`, `live` | `IntegrationEnvelope`, fanout signals, `LiveMessage` |
| F8 | HybridCache, distributed locks, Data Protection keys | cache entries |
| F9 | schema `platform` (`HybirdTenants`) | `tenants`, `access_grants`, `tenant_subscriptions` |
| F10 | one database per tenant, one schema per module | module rows, `integration_outbox`, `integration_inbox` |
| F11 | schemas `elsa`, `elsa_events` | workflow definitions, instances, bookmarks, the inbox |
| F12 | S3-compatible store or the file system | blobs, upload chunks, thumbnails |
| F13 | Ollama, OpenAI, Azure OpenAI, OpenAI-compatible | chat and structured prompts → completions |

## 0.1 Inside: platform and shell processes

The browser starts from the core modules. It loads the module manifest, the menus, the branding and the caller.
Then it loads the client modules' files and opens the live hub. All `/api` and `/hubs` traffic passes through the
YARP gateway, which is left out of the drawing.

```mermaid
flowchart LR
  Browser(["Browser<br/>BlzWebClient, layouts,<br/>client modules"]):::ext

  subgraph Platform["Platform host processes (every pool)"]
    direction TB
    CM("client-modules"):::core
    MN("menus"):::core
    BR("branding"):::core
    ID("identity"):::core
    TE("tenancy"):::core
    RT("Module runtime"):::core
    LH("Live hub"):::core
    LK("Lookups"):::core
  end

  MQ[("RabbitMQ")]:::store
  IdP(["Identity service"]):::ext

  Browser <-->|F1| CM
  Browser <-->|F2| MN
  Browser <-->|F3| BR
  Browser <-->|F4| ID
  Browser <-->|F5| TE
  Browser <-->|F6| RT
  Browser <-->|F7| LH
  Browser <-->|F8| LK
  Browser <-->|F9| IdP
  RT <-->|F10| MQ
  TE -->|F11| MQ
  ID -->|F12| MQ
  LH <-->|F13| MQ

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
```

| Flow | Route or channel | Payload |
|---|---|---|
| F1 | `GET /api/client-modules/manifest`, `files/{id}/{version}/...` | → `ClientModuleManifest(Etag, Modules)`, assemblies, styles |
| F2 | `GET /api/menus` | → combined menus (platform + tenant) |
| F3 | `GET /api/branding` | → effective `BrandLayer`, assets |
| F4 | `GET /api/identity/me`, `grants` | `NewGrant` → `CurrentPrincipal` (user, tenant, permissions, claims), `AccessGrant` |
| F5 | `/api/tenancy/tenants`, `subscriptions` | `CreateTenantRequest`, `UpdateTenantRequest`, `NewSubscription` → `TenantInfo`, `TenantSubscription` |
| F6 | `/api/admin/modules` | package upload (`.dll`, `.zip`, `.nupkg`) → module state |
| F7 | `/hubs/live` | `Subscribe(topic)` → `LiveSubscribeResult`, `LiveMessage` |
| F8 | `/api/lookups/{name}` | `LookupQuery` → `LookupPage` of `LookupItem` |
| F9 | OIDC sign-in, token refresh | tokens |
| F10 | fanout `modules.state` | module id + state |
| F11 | fanout `tenants.changed` | a reload signal |
| F12 | fanout `access.grants.changed` | a signal |
| F13 | fanout `live` | `LiveMessage` for every replica's hub |

## 0.2 Inside: the newsroom domain (HTTP)

The browser calls each module through its typed client. The servers also call each other through the gateway. The
table gives the main payloads of each flow. The [level-1 pages](#level-1-pages) list them all.

```mermaid
flowchart LR
  Staff(["Staff in the browser"]):::ext

  subgraph Mods["newsroom domain modules"]
    direction TB
    NR("newsroom"):::custom
    CD("contentdesigner"):::custom
    AI("ai"):::custom
    EL("elsa"):::custom
    SH("shifts"):::custom
    CO("content"):::custom
    DF("dynamicfields"):::custom
    ORG("organization<br/>connector + directory"):::custom
  end

  Central(["Central Organization<br/>service"]):::ext

  Staff <-->|F1| NR
  Staff <-->|F2| CO
  Staff <-->|F3| CD
  Staff <-->|F4| DF
  Staff <-->|F5| SH
  Staff <-->|F6| AI
  Staff <-->|F7| Central
  NR -->|F8| CO
  NR -->|F9| DF
  NR & CO & CD & EL -->|F10| ORG
  CO -->|F11| DF
  CD -->|F12| CO
  CD -->|F13| DF
  EL -->|F14| CO
  EL -->|F15| NR
  EL -->|F16| SH
  AI -->|F17| CO
  AI -->|F18| DF
  ORG <-->|F19| Central
  SH -->|F20| Central

  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
```

| Flow | Route | Payload (request → response) |
|---|---|---|
| F1 | `/api/newsroom/proposals`, `projects` | `NewProposal`, `Submission`, `Approval`, `KillDecision` → `ProposalDto`, `ProjectDto`, `ProposalPage` |
| F2 | `/api/content/posts`, `contents` | `NewPost`, `PostEdit`, `NewContent`, `ContentEdit`, `ContentAction`, `VersionRequest`, `ScheduleRequest` → `PostDetail`, `PostSummary`, `ContentDetail`, `FeedPage` |
| F3 | `/api/contentdesigner/posts/{id}/design`, `workspace` | `SaveRequest`, `ArrangeRequest`, `ApplyRequest`, `PublishRequest` → `PostDesignDto`, `WorkspaceDto`, `PreviewDto` |
| F4 | `/api/dynamicfields/fields`, `catalog` | `NewField`, `DraftEdit`, `PublishRequest` → `FieldDetailDto`, `FieldPage`, `SchemaOrgCatalogDto` |
| F5 | `/api/shifts/shifts`, `templates`, `check-in` | `ShiftRequest`, `TemplateCloneRequest`, `HandoverRequest` → `ShiftDto`, `ShiftTemplateDto`, `UnitAttendanceDto` |
| F6 | `/api/ai/seo/optimize`, `jobs/{id}` | `SeoRequest` → `JobStarted`, `JobView`, `AiJobResult` |
| F7 | central `/api/organization/orgs/...` | `CreateUnitRequest`, `MoveUnitRequest`, `CreateMembershipRequest`, `CreateSeedRequest` → `OrgUnitDto`, `MembershipDto`, `TenantSeedDto` |
| F8 | `POST posts`, `GET categories` | `NewPost` → `PostDetail`, `CategoryDto[]` |
| F9 | `content/validate-node`, `catalog/root-post-fields` | `NodeValues` → `ContentReport`, `RootPostFieldDto[]` |
| F10 | `/api/organization/local/units...` | → `DirectoryUnitDto`, `DirectoryMemberDto` |
| F11 | `content/validate`, `validate-node`, `fields/{key}/versions/{n}` | `ContentTree`, `NodeValues` → `ContentReport`, `FieldVersionDto`, `RootPostFieldDto` |
| F12 | `categories`, `contents`, `posts/{id}`, `posts/{id}/events`, `public/{publicId}` | → `CategoryDto`, `ContentSummary`, `PostDetail`, `FeedPage`, `PublishedPost` |
| F13 | `content/jsonld`, `content/validate` | `JsonLdRequest`, `ContentTree` → `JsonLdReport`, `ContentReport` |
| F14 | `posts/{id}/unit`, `schedule`, `publish`, `contents/{id}/assign`, `automation/*` | `UnitAssignment`, `ScheduleRequest`, `ContentAssignment` → `PostDetail` |
| F15 | `GET projects/{id}`, `POST projects/{id}/state` | `StateReport` → `ProjectDto` |
| F16 | `shifts?from&to`, `shifts/ended`, `overdue`, `shifts/{id}/automation` | → `ShiftDto[]`, `EndedShiftDto[]`, `ShiftAutomationInfo` |
| F17 | `posts/{id}` | → `PostDetail` |
| F18 | `fields/{key}/versions/{n}`, `content/validate-node` | `NodeValues` → `FieldVersionDto`, `ContentReport` |
| F19 | `seeds?deployment`, `events?after=`, `links`, `seeds/{id}/reports` | → `DeploymentSeedDto`, `FeedPage`, `TenantLinkDto`; `SeedReportRequest` |
| F20 | `IOrganizationClient`, as the caller | `CreateUnitRequest` → `OrgUnitDto` |

## 0.3 Inside: the asset and directory modules (HTTP)

These modules call no other module. Readers reach `storage`, `contentdesigner` and `content` without signing in,
through the public routes.

```mermaid
flowchart LR
  subgraph Actors[" "]
    direction TB
    Staff(["Staff in the browser"]):::ext
    Reader(["Reader (anonymous)"]):::ext
    Admin(["Tenant / platform admin"]):::ext
  end

  subgraph Mods[" "]
    direction TB
    ST("storage"):::custom
    TX("taxonomies"):::custom
    KW("keywords"):::custom
    CD("contentdesigner<br/>render"):::custom
    CO("content<br/>public"):::custom
    IA("identity-admin"):::custom
  end

  subgraph Out[" "]
    direction TB
    Blob[("Object storage")]:::store
    IdP(["Identity service"]):::ext
  end

  Staff <-->|F1| ST
  Reader <-->|F2| ST
  Staff <-->|F3| TX
  Staff <-->|F4| KW
  Reader <-->|F5| CD
  Reader <-->|F6| CO
  Admin <-->|F7| IA
  ST <-->|F8| Blob
  IA <-->|F9| IdP

  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
  style Actors fill:none,stroke:none
  style Mods fill:none,stroke:none
  style Out fill:none,stroke:none
```

| Flow | Route | Payload (request → response) |
|---|---|---|
| F1 | `/api/storage/uploads`, `folders`, `items`, `sharing` | `StartUploadCommand`, chunks, `CreateFolderCommand`, `MoveItemsCommand`, `SetAclRequest`, `CreateShareLinkRequest` → `UploadDto`, `ItemDto`, `FolderView`, `SharingDto`, `DownloadLink` |
| F2 | `GET shares/{tenant}/{token}`, `public/{tenant}/{fileId}/{name}` | → `SharedView`, a file stream |
| F3 | `/api/taxonomies/schemes`, `terms` | `NewScheme`, `NewTerm`, `TermMove` → `SchemeDto`, `TermRefDto`, `JsonLdDto` |
| F4 | `/api/keywords/concepts` | `ConceptValues`, `ConceptEdit` → `ConceptDto`, `ConceptSummaryDto`, `ConceptJsonLdDto` |
| F5 | `GET render/{publicId}`, `render/{publicId}/amp` | → an HTML page, an AMP page |
| F6 | `GET public/{publicId}` | → `PublishedPost` |
| F7 | `/api/identity-admin/members`, `grants`, `platform/...` | `InviteMemberRequest`, `NewGrantRequest`, `ClientApplicationRequest`, `TenantPolicyRequest` → `Member`, `MemberAccess`, `ClientApplication`, `TenantPolicy` |
| F8 | `IBlobStore` | blobs |
| F9 | `/api/service/*` (client credentials) | `InviteMember`, `UpdateMember`, `ApplicationInput`, ... |

## 0.4 Inside: integration events and their payloads

Every event travels as an `IntegrationEnvelope` on the `integration` topic exchange. The diagram draws only the
flows that have a subscriber in the code.

```mermaid
flowchart LR
  ORG("organization"):::custom
  SH("shifts"):::custom
  CO("content"):::custom
  NR("newsroom"):::custom
  EL("elsa"):::custom
  HOST("Platform host<br/>TenantSeedReconciler"):::core

  ORG -.->|E1| SH
  ORG -.->|E2| CO
  ORG -.->|E3| NR
  ORG -.->|E4| HOST
  SH -.->|E5| CO
  SH -.->|E6| EL
  CO -.->|E7| NR
  CO -.->|E8| EL
  NR -.->|E9| EL

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
```

| Flow | Event type | Record in `data` |
|---|---|---|
| E1 | `org.unit-created.v1`, `org.unit-updated.v1`, `org.unit-moved.v1`, `org.unit-removed.v1`, `org.membership-changed.v1` | `UnitCreated`, `UnitUpdated` (`OrgUnitSnapshot`), `UnitMoved`, `UnitRemoved`, `MembershipChanged` |
| E2 | `org.unit-*.v1` | `UnitCreated`, `UnitUpdated`, `UnitMoved`, `UnitRemoved` |
| E3 | `org.unit-moved.v1` | `UnitMoved` |
| E4 | `tenancy.seed-changed.v1` | a seed change; the host reconciles the tenants |
| E5 | `shifts.section-moved.v1` | `SectionMoved(SectionId, UnitId)` |
| E6 | `shifts.published.v1`, `shifts.member-added.v1`, `shifts.checked-in.v1` | `ShiftPublished` (`ShiftSnapshot`), `ShiftMemberAdded`, `ShiftCheckedIn` |
| E7 | `content.post-state-changed.v1`, `content.post-categories-changed.v1` | `ContentFeedEvent`, `PostCategoriesChanged` |
| E8 | `content.post-*.v1`, `content.work-item-*.v1` | `PostCreated`, `PostCategoriesChanged`, `PostUnitChanged`, `ContentFeedEvent`, `WorkItemAssigned`, `WorkItemStateChanged` |
| E9 | `newsroom.project-approved.v1`, `newsroom.project-reapproved.v1`, `newsroom.project-state-changed.v1` | `ProjectApproved`, `ProjectStateChanged` |

**Live messages** are not integration events. `content`, `shifts`, `organization`, `elsa` and `ai` call
`ILiveNotifier`, and the message reaches every replica's hub over the `live` fanout. The hub sends it as
`LiveMessage(topic, type, payload, sequence, at)` to the subscribers of the topic. The topics are:

- `content:post:{id}`
- `shifts:shift:{id}`
- `organization:org:{id}`
- the unit groups `unit:{id}`
- `user:{id}`

## Planned (not built)

```mermaid
flowchart LR
  NR("newsroom"):::custom
  SH("shifts"):::custom
  CO("content"):::custom
  AI("ai"):::custom
  Bind["newsroom N3: bindings,<br/>routing, read model,<br/>unit live topics"]:::planned
  Search["Module.Search (S1, S2)<br/>OpenSearch, embeddings"]:::planned
  Hooks["Webhooks module"]:::planned
  Relay["Cross-deployment<br/>relay feed"]:::planned
  NR --- Bind
  Bind -.-o|P1| SH
  CO -.-o|P2| Search
  AI -.-o|P3| Search
  Hooks -.-o|P4| Relay
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef planned fill:#16161d,stroke:#9a9bb3,stroke-dasharray:6 4,color:#b8b8c8
```

| Flow | What is planned |
|---|---|
| P1 | NewsRoom N3 assigns work to the running shift, routes by rules and keeps a load read model per unit |
| P2 | A Content indexer feeds per-tenant OpenSearch indices |
| P3 | Ai grounds its compose jobs on Search and shows related posts |
| P4 | A Webhooks module acts on tenant links, fed across deployments by a relay feed |

## Level-1 pages

The sidebar lists one level-1 dataflow page for each module, under **Module architecture → Dataflow level 1**.

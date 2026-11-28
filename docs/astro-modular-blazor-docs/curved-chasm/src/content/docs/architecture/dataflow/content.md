---
title: Content
description: "Level-1 dataflow of Module.Content: posts of shared contents, the pipeline, work items, the public read model, its calls to DynamicFields and Organization, and its events."
sidebar:
  order: 12
---

`content` (Module.Content, v0.1.0, **sales pool**) owns posts and the shared contents a post is made of. It also
owns the editorial pipeline, the content work items (stage and assignee) and a change feed. The post kind and the
content kinds are DynamicFields field keys. Validation is done by calling DynamicFields.

```mermaid
flowchart LR
  subgraph In[" "]
    direction TB
    Ed(["Journalist / editor / desk"]):::ext
    Reader(["Reader"]):::ext
    Callers(["newsroom, contentdesigner,<br/>elsa, ai"]):::custom
    MQi[("RabbitMQ in")]:::store
  end

  subgraph Content["content (/api/content)"]
    direction TB
    C1("12.1 Posts workflow"):::custom
    C2("12.2 Post filing"):::custom
    C3("12.3 Contents"):::custom
    C4("12.4 Pipeline + feeds"):::custom
    C6("12.5 Public read"):::custom
    C7("12.6 Automation<br/>(until Elsa E3)"):::custom
    C8("12.7 Unit handlers"):::custom
    C9("12.8 ContentEvents,<br/>PostLive"):::custom
  end

  subgraph Out[" "]
    direction TB
    Db[("schema content")]:::store
    DF("dynamicfields"):::custom
    Org("organization<br/>local directory"):::custom
    MQo[("RabbitMQ out")]:::store
    Live("Live hub"):::core
  end

  Ed <-->|F1| C1
  Ed <-->|F2| C2
  Ed <-->|F3| C3
  Ed <-->|F4| C4
  Reader <-->|F5| C6
  Callers <-->|F6| C1
  Callers <-->|F6| C3
  Callers <-->|F7| C7
  MQi -->|F8| C8
  C1 & C2 & C3 & C4 & C6 & C7 & C8 <--> Db
  C1 & C3 <-->|F9| DF
  C2 & C3 <-->|F10| Org
  C1 & C2 & C3 --> C9
  C9 -->|F11| MQo
  C9 -->|F12| Live

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
  style In fill:none,stroke:none
  style Out fill:none,stroke:none
```

| Flow | Route or channel | Payload (request → response) |
|---|---|---|
| F1 | `POST posts`, `PUT posts/{id}`, `posts/{id}/submit`, `approve`, `accept`, `return`, `assign`, `transfer`, `schedule`, `publish`, `publish-due` | `NewPost`, `PostEdit`, `VersionRequest`, `AcceptRequest`, `ReturnRequest`, `Assignment`, `TransferRequest`, `ScheduleRequest` → `PostDetail`, `PostSummary[]` |
| F2 | `PUT posts/{id}/categories`, `unit`, `root`, `slugs`, `translations/{lang}`; `POST/DELETE posts/{id}/contents`, `PUT contents/order` | `PostCategories`, `UnitAssignment`, `PostRootEdit`, `PostSlugsEdit`, `PostTranslationEdit`, `PostContentAttachment`, `PostContentOrder` → `PostDetail` |
| F3 | `GET/POST/PUT/DELETE contents`, `contents/{id}/act`, `assign`, `unit`, `archive`, `revisions/{n}` | `NewContent`, `ContentEdit`, `ContentAction`, `ContentAssignment`, `ContentVersion` → `ContentDetail`, `ContentSummary[]`, `ContentRevisionDetail` |
| F4 | `GET/PUT pipeline`, `GET events?after=`, `posts/{id}/events?after=`, `categories`, `presses`, `kinds` | `PipelineDefinition` → `PipelineInfo`, `FeedPage`, `CategoryDto`, `PressDto`, `PostKindInfo` |
| F5 | `GET public/{publicId}` (anonymous) | → `PublishedPost` |
| F6 | the same routes as F1 and F3, called by other modules through the gateway | as in F1 and F3 |
| F7 | `GET automation/accepted`, `POST automation/accepted/{id}/assign`, `automation/shifts/{shiftId}/handover` (Elsa's poller) | → `PostSummary[]`, `PostDetail` |
| F8 | queue `integration.content.sales` | `SectionMoved` (`shifts.section-moved.v1`); `UnitCreated`, `UnitUpdated`, `UnitMoved`, `UnitRemoved` (`org.unit-*.v1`) |
| F9 | `GET /api/dynamicfields/catalog/root-post-fields`, `fields/{key}/versions/{n}`; `POST content/validate`, `content/validate-node` | `ContentTree`, `NodeValues` → `RootPostFieldDto[]`, `FieldVersionDto`, `ContentReport` |
| F10 | `GET /api/organization/local/units...` | → `DirectoryUnitDto` |
| F11 | topic `integration` | see the events below |
| F12 | `ILiveNotifier`, topic `content:post:{id}` | `LiveMessage` (feed type, version) |

**Tables (schema `content`):**

- posts: `articles`, `article_history`, `post_categories`, `post_contents`, `post_slugs`
- contents: `contents`, `content_revisions`, `content_history`
- `pipelines`, `events` (the feed), `categories`, `presses`
- `integration_outbox`, `integration_inbox`

## Events (`Content.Contracts`)

| Type | Data | Subscribers |
|---|---|---|
| `content.post-created.v1` | `PostCreated(PostId, PublicId, Version, PostKind, Title, OwnerId, State, ...)` | elsa (`content.post-*`) |
| `content.post-changed.v1`, `content.post-state-changed.v1` | `ContentFeedEvent(FeedType, PostId, ContentId, Version, Revision, Stage, ...)` | newsroom (state-changed), elsa |
| `content.post-categories-changed.v1` | `PostCategoriesChanged(PostId, PublicId, Version, PressId, PrimaryCategoryId, ...)` | newsroom, elsa |
| `content.post-unit-changed.v1` | `PostUnitChanged(PostId, PublicId, Version, PressId, PrimaryCategoryId, ...)` | elsa |
| `content.content-attached / detached / created / changed / state-changed / assigned / archived / deleted.v1` | `ContentFeedEvent` | none yet |
| `content.validation-failed.v1` | `ContentValidationFailed(Check, Action, ContentId, PostId, Version, ... ContentProblem[])` | none yet |
| `content.work-item-assigned.v1`, `content.work-item-state-changed.v1` | `WorkItemAssigned`, `WorkItemStateChanged` (platform work-item contract) | elsa |

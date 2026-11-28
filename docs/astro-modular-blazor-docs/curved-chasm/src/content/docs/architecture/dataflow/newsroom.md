---
title: NewsRoom
description: "Level-1 dataflow of Module.NewsRoom: proposals, moderation and post projects, workflow bindings, routing rules, the workload read model and the live boards, with its calls, events and live topics."
sidebar:
  order: 13
---

`newsroom` (Module.NewsRoom, v0.1.0, **core pool**) turns a member's proposal into a post project. Steps N1 to N3 are
built:

- **N1, N2:** proposals, moderation, and post projects. Approval creates the draft post in Content.
- **N3:** workflow bindings (which Elsa workflow runs for a post or content kind in a unit), routing rules, a
  workload read model fed by Shifts and work-item events, and live boards per unit.

```mermaid
flowchart LR
  subgraph In[" "]
    direction TB
    Member(["Member"]):::ext
    Mod(["Moderator /<br/>newsroom manager"]):::ext
    Elsa("elsa"):::custom
    MQi[("RabbitMQ in")]:::store
  end

  subgraph NewsRoom["newsroom (/api/newsroom)"]
    direction TB
    N1("13.1 Proposals"):::custom
    N2("13.2 Moderation"):::custom
    N3("13.3 Projects"):::custom
    N4("13.4 Bindings"):::custom
    N5("13.5 Routing"):::custom
    N6("13.6 Workload"):::custom
    N7("13.7 Event handlers"):::custom
    N8("13.8 LiveNudgeHandler"):::custom
  end

  subgraph Out[" "]
    direction TB
    Db[("schema newsroom")]:::store
    CO("content"):::custom
    DF("dynamicfields"):::custom
    SH("shifts"):::custom
    Org("organization<br/>local directory"):::custom
    MQo[("RabbitMQ out")]:::store
    Live("Live hub"):::core
  end

  Member <-->|F1| N1
  Mod <-->|F2| N2
  Mod <-->|F3| N3
  Mod <-->|F4| N4
  Mod <-->|F5| N5
  Mod <-->|F6| N6
  Elsa <-->|F7| N3
  Elsa <-->|F8| N4
  Elsa <-->|F9| N5
  Elsa <-->|F10| N6
  MQi -->|F11| N7
  MQi -->|F12| N8
  N1 & N2 & N3 & N4 & N5 & N6 & N7 <--> Db
  N1 & N2 <-->|F13| DF
  N2 & N3 & N6 <-->|F14| CO
  N6 <-->|F15| SH
  N1 & N2 & N3 <-->|F16| Org
  N1 & N2 & N3 & N4 -->|F17| MQo
  N8 -->|F18| Live

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
  style In fill:none,stroke:none
  style Out fill:none,stroke:none
```

| Flow | Route or channel | Payload |
|---|---|---|
| F1 | `POST proposals`, `PUT/DELETE proposals/{id}`, `{id}/submit`, `GET {id}`, `mine` | `NewProposal`, `ProposalEdit`, `Submission` → `ProposalDto`, `ProposalPage` |
| F2 | `GET proposals/queue`, `POST {id}/approve`, `return`, `cancel` (`newsroom.moderate`) | `Approval`, `ReturnDecision`, `CancelDecision` → `ApprovalDto`, `ProposalDto` |
| F3 | `GET projects`, `{id}`, `{id}/proposal`, `POST {id}/draft`, `fix`, `kill` | `DraftRequest(Version, AbandonEarlier)`, `Correction` (post kind, Section, categories), `KillDecision` → `ProjectDto`, `ProjectPage` |
| F4 | `GET/POST bindings`, `PUT/DELETE bindings/{id}` | `NewBinding(KindType, Kind, UnitId, DefinitionId, DefinitionVersion)`, `BindingChange` → `BindingDto` |
| F5 | `GET/POST routing-rules`, `PUT/DELETE routing-rules/{id}`, `GET sections/suggestions` | `NewRule(ContentKind, UnitId, UnitTag, Order)`, `RuleChange` → `RoutingRuleDto`, `SectionSuggestion[]` |
| F6 | `GET workload?unitId`, `POST workload/reconcile?prune` | → `UnitWorkload`, `ReconcileReport` |
| F7 | Elsa: `GET projects/{id}`, `projects/{id}/workflow`, `POST projects/{id}/state` | `StateReport(State, ApprovalNumber, Reason)` → `ProjectDto`, `BindingResolution` |
| F8 | Elsa: `GET bindings/resolve?kindType&kind&unitId` | → `BindingResolution` (the definition to run, or none) |
| F9 | Elsa: `POST routing/route` | `RouteRequest(ContentKind, Categories)` → `RouteResult` (the unit) |
| F10 | Elsa: `POST workload/query` | `WorkloadRequest(UserIds)` → `WorkloadCounts` (open items per member) |
| F11 | queue `integration.newsroom.core`, see the subscriptions below | `UnitMoved`, `ContentFeedEvent`, `PostCategoriesChanged`, the Shifts records, `WorkItemAssigned`, `WorkItemStateChanged`, content removals |
| F12 | the module's own events, delivered back after the commit | `ProposalSubmitted`, `ProposalDecided`, `ProjectApproved`, `ProjectStateChanged`, `ProjectDetailsChanged` |
| F13 | `/api/dynamicfields/fields/proposal/versions/0`, `catalog/root-post-fields`, `fields/{key}/versions/{n}`, `content/validate-node` | `NodeValues` → `FieldVersionDto`, `RootPostFieldDto[]`, `ContentReport` |
| F14 | `POST /api/content/posts`, `GET categories`, `GET contents?assigneeId` (reconcile) | `NewPost` → `PostDetail`, `CategoryDto[]`, `ContentSummary[]` |
| F15 | `GET /api/shifts/shifts?from&to` (reconcile) | → `ShiftDto[]` |
| F16 | `GET /api/organization/local/units...` (`IUnitLookup`) | → `DirectoryUnitDto` |
| F17 | topic `integration` | see the events below |
| F18 | `ILiveNotifier`, topics `newsroom:board:{unit}` and `unit:{unit}`, for each unit from the nearest up | `newsroom.proposal.changed` (`ProposalNudge`), `newsroom.project.changed` (`ProjectNudge`) |

**Tables (schema `newsroom`):**

- `proposals`, `post_projects`
- `workflow_bindings`, `routing_rules`
- the workload read model: `work_items`, `shift_records`, `shift_attendance`
- the outbox and inbox

## Events published (`NewsRoom.Contracts`)

| Type | Data | Subscribers |
|---|---|---|
| `newsroom.proposal-submitted.v1` | `ProposalSubmitted(ProposalId, Version, Title, AuthorId, UnitId, UnitPath, ...)` | newsroom (live) |
| `newsroom.proposal-decided.v1` | `ProposalDecided(ProposalId, Version, Decision, ...)` | newsroom (live) |
| `newsroom.project-approved.v1`, `newsroom.project-reapproved.v1` | `ProjectApproved(ProjectId, ProposalId, Version, ApprovalNumber, PostId, PostPublicId, ...)` | elsa, newsroom (live, approved only) |
| `newsroom.project-binding-available.v1` | `ProjectApproved` data, for a project approved before its binding existed | elsa |
| `newsroom.project-state-changed.v1` | `ProjectStateChanged(ProjectId, ProposalId, Version, ApprovalNumber, PreviousState, ...)` | elsa, newsroom (live) |
| `newsroom.project-details-changed.v1` | `ProjectDetailsChanged(ProjectId, ..., PostKind, SectionId, SectionPath, Categories, Title, ChangedAt)` | newsroom (live) |
| `newsroom.work-item-assigned.v1`, `newsroom.work-item-state-changed.v1` | `WorkItemAssigned`, `WorkItemStateChanged` (`ProjectWorkItem`) | newsroom (workload) |

## Subscriptions

| Pattern | Handler | What it does |
|---|---|---|
| `org.unit-moved.v1` | `UnitMovedHandler` | re-paths proposals and projects; may announce a binding |
| `content.post-state-changed.v1` | `PostStateChangedHandler` | moves the project's state |
| `content.post-categories-changed.v1` | `PostCategoriesChangedHandler` | updates the project's categories |
| `shifts.published`, `updated`, `cancelled`, `checked-in`, `checked-out`, `ended`, `section-moved` (`.v1`) | `ShiftEventsHandler` | the workload model: `shift_records`, `shift_attendance` |
| `*.work-item-assigned.v1`, `*.work-item-state-changed.v1` | `WorkItemAssignedHandler`, `WorkItemStateHandler` | the workload model: `work_items` |
| `content.content-deleted.v1`, `content.content-archived.v1` | `ContentRemovedHandler` | closes the content's work items |
| its own proposal and project events | `LiveNudgeHandler` | the live boards (F18) |

**References:** `Organization.Contracts`, `Content.Contracts` and `Shifts.Contracts`. **Live topic:** `board`, open to
any of `newsroom.moderate`, `newsroom.projects`, `newsroom.workflow` and `newsroom.manage`.

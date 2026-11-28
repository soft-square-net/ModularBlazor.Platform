---
title: NewsRoom
description: "Level-1 dataflow of Module.NewsRoom: proposals, moderation and post projects (N1-N2 built), its calls to Content, DynamicFields and Organization, its events, and the planned N3 bindings, routing and read model."
sidebar:
  order: 13
---

`newsroom` (Module.NewsRoom, v0.1.0, **core pool**) turns a member's proposal into a post project. A moderator
approves the proposal, which creates the draft post in Content and starts the bound workflow in Elsa. Steps N1 and
N2 are built. **N3 is planned:** workflow bindings, routing rules, the load read model and the unit live topics.

```mermaid
flowchart LR
  Member(["Member"]):::ext
  Mod(["Moderator"]):::ext
  Elsa("elsa"):::custom
  MQi[("RabbitMQ in")]:::store

  subgraph NewsRoom["newsroom (/api/newsroom)"]
    direction TB
    N1("13.1 Proposals"):::custom
    N2("13.2 Moderation"):::custom
    N3("13.3 Projects"):::custom
    N4("13.4 Event handlers"):::custom
    N5("13.5 Events to outbox"):::custom
    N6["13.6 N3 planned: bindings,<br/>routing, read model"]:::planned
  end

  Db[("schema newsroom")]:::store
  CO("content"):::custom
  DF("dynamicfields"):::custom
  Org("organization<br/>local directory"):::custom
  MQo[("RabbitMQ out")]:::store

  Member <-->|F1| N1
  Mod <-->|F2| N2
  Mod <-->|F3| N3
  Elsa <-->|F4| N3
  MQi -->|F5| N4
  N1 & N2 & N3 & N4 <--> Db
  N1 & N2 <-->|F6| DF
  N2 & N3 <-->|F7| CO
  N1 & N2 <-->|F8| Org
  N1 & N2 & N3 --> N5
  N5 -->|F9| MQo
  N6 -.-o N3

  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
  classDef planned fill:#16161d,stroke:#9a9bb3,stroke-dasharray:6 4,color:#b8b8c8
```

| Flow | Route or channel | Payload |
|---|---|---|
| F1 | `POST proposals`, `PUT/DELETE proposals/{id}`, `{id}/submit`, `GET {id}`, `mine` | `NewProposal`, `ProposalEdit`, `Submission` → `ProposalDto`, `ProposalPage` |
| F2 | `GET proposals/queue`, `POST {id}/approve`, `return`, `cancel` (`newsroom.moderate`) | `Approval`, `ReturnDecision`, `CancelDecision` → `ApprovalDto`, `ProposalDto` |
| F3 | `GET projects`, `{id}`, `{id}/proposal`, `POST {id}/draft`, `fix`, `kill` | `DraftRequest`, `Correction`, `KillDecision` → `ProjectDto`, `ProjectPage` |
| F4 | Elsa: `GET projects/{id}`, `POST projects/{id}/state` | `StateReport` (state, approvalNumber, reason) → `ProjectDto` |
| F5 | queue `integration.newsroom.core` | `UnitMoved` (`org.unit-moved.v1`), `ContentFeedEvent` (`content.post-state-changed.v1`), `PostCategoriesChanged` |
| F6 | `/api/dynamicfields/fields/{key}`, `versions/{n}`, `catalog/root-post-fields`, `content/validate-node` | `NodeValues` → `FieldVersionDto`, `RootPostFieldDto[]`, `ContentReport` |
| F7 | `POST /api/content/posts`, `GET /api/content/categories` | `NewPost` → `PostDetail`, `CategoryDto[]` |
| F8 | `GET /api/organization/local/units...` (`IUnitLookup`) | → `DirectoryUnitDto` |
| F9 | topic `integration` | see the events below |

**Tables (schema `newsroom`):** `proposals`, `post_projects`, plus the outbox and inbox.

## Events (`NewsRoom.Contracts`)

| Type | Data | Subscribers |
|---|---|---|
| `newsroom.proposal-submitted.v1` | `ProposalSubmitted(ProposalId, Version, Title, AuthorId, UnitId, UnitPath, ...)` | none yet |
| `newsroom.proposal-decided.v1` | `ProposalDecided(ProposalId, Version, Decision, Title, AuthorId, UnitId, ...)` | none yet |
| `newsroom.project-approved.v1`, `newsroom.project-reapproved.v1` | `ProjectApproved(ProjectId, ProposalId, Version, ApprovalNumber, PostId, PostPublicId, ...)` | elsa |
| `newsroom.project-state-changed.v1` | `ProjectStateChanged(ProjectId, ProposalId, Version, ApprovalNumber, PreviousState, ...)` | elsa |
| `newsroom.work-item-assigned.v1`, `newsroom.work-item-state-changed.v1` | `WorkItemAssigned`, `WorkItemStateChanged` (`ProjectWorkItem`) | none yet |

**References:** `Organization.Contracts` and `Content.Contracts`.

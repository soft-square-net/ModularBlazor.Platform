---
title: Elsa (workflow pool)
description: "Level-1 dataflow of Module.Elsa: the Elsa 3.9 engine in the workflow pool, workflows started from integration events, the newsroom activities, the deadline poller, and the module APIs it calls."
sidebar:
  order: 14
---

`elsa` (Module.Elsa, v0.1.0) runs only in the **workflow pool**, which the gateway reaches at `/api/elsa`. It
hosts the Elsa 3.9 engine, with its own database (`ConnectionStrings:elsa`). Workflows start from integration
events (step E1). Their activities change Content, NewsRoom and Shifts through those modules' HTTP APIs, with the
`elsa-workflows` client-credentials token. **E2 and E3 are planned:**

- E2: default workflows per post kind and per content kind
- E3: remove the poller and Content's `/automation/*` routes

```mermaid
flowchart LR
  Designer(["Workflow designer"]):::ext
  MQ[("RabbitMQ in")]:::store

  subgraph Elsa["elsa"]
    direction TB
    E1("14.1 Workflows API"):::custom
    E2("14.2 NewsroomEventHandler"):::custom
    E3("14.3 Newsroom activities"):::custom
    E4("14.4 Engine + timer sweeper"):::custom
    E5("14.5 Automation poller<br/>(removed in E3)"):::custom
    E6["14.6 E2 planned:<br/>default workflows"]:::planned
  end

  EDb[("ElsaWorkflows DB<br/>schema elsa")]:::store
  IDb[("schema elsa_events<br/>inbox")]:::store
  IdP(["Identity service"]):::ext
  CO("content"):::custom
  NR("newsroom"):::custom
  SH("shifts"):::custom
  Org("organization<br/>local directory"):::custom
  Live("Live hub"):::core

  Designer <-->|F1| E1
  MQ -->|F2| E2
  E2 <--> IDb
  E2 --> E3
  E1 & E3 & E4 <--> EDb
  E3 <-->|F3| IdP
  E3 <-->|F4| CO
  E3 <-->|F5| NR
  E3 <-->|F6| Org
  E3 <-->|F7| SH
  E3 -->|F8| Live
  E5 <-->|F9| CO
  E5 <-->|F10| SH
  E6 -.-o E3

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
  classDef planned fill:#16161d,stroke:#9a9bb3,stroke-dasharray:6 4,color:#b8b8c8
```

| Flow | Route or channel | Payload |
|---|---|---|
| F1 | Elsa workflows API (`MapWorkflowsApi`, authorized; `workflow.view`, `workflow.design`, `workflow.run`) | workflow definitions and instances (Elsa JSON) |
| F2 | queue `integration.elsa.workflow` | `ProjectApproved`, `ProjectStateChanged`; `WorkItemAssigned`, `WorkItemStateChanged`; `PostCreated`, `PostCategoriesChanged`, `PostUnitChanged`, `ContentFeedEvent`; `ShiftPublished`, `ShiftMemberAdded`, `ShiftCheckedIn` |
| F3 | `client_credentials` (`elsa-workflows`) | → service token |
| F4 | `PUT posts/{id}/unit`, `POST posts/{id}/schedule`, `posts/{id}/publish`, `contents/{id}/assign`, `GET posts/{id}`, `contents/{id}`, `contents?assigneeId` | `UnitAssignment`, `ScheduleRequest`, `VersionRequest`, `ContentAssignment` → `PostDetail`, `ContentDetail` |
| F5 | `GET projects/{id}`, `POST projects/{id}/state` | `(state, approvalNumber, reason)` → `ProjectSnapshot` |
| F6 | `GET local/units/{id}`, `{id}/members`, `{id}/categories` | → `DirectoryUnitDto`, `DirectoryMemberDto` |
| F7 | `GET shifts?from&to` | → `ShiftDto[]` |
| F8 | `Notify` → `HostLiveMessenger` → `ILiveNotifier` | `LiveMessage` |
| F9 | `GET automation/accepted`, `POST accepted/{id}/assign`, `automation/shifts/{id}/handover`, `posts/publish-due` | → `PostSummary[]`, `PostDetail`, `int[]` |
| F10 | `GET shifts/ended?since`, `overdue`, `shifts/{id}/automation` | → `EndedShiftDto[]`, `ShiftDto[]`, `ShiftAutomationInfo` |

**Activities (14.3):** `RouteToUnit`, `EnsurePostInUnitCategories`, `WaitForRefiling`, `AssignInShift`,
`WaitForStage`, `Deadline`, `DeskReview`, `PublishPost`, `Notify`.

**Background services:** `ElsaEngineService`, `TimerSweeperService` and `NewsroomAutomationService`.

Elsa publishes no integration events. It reports back over HTTP. **Subscriptions:**

- `newsroom.project-approved.v1`, `newsroom.project-reapproved.v1`, `newsroom.project-state-changed.v1`
- `content.work-item-*.v1`, `content.post-*.v1`
- `shifts.checked-in.v1`, `shifts.published.v1`, `shifts.member-added.v1`

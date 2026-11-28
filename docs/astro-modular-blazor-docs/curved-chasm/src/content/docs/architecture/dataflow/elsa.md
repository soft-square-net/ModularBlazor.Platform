---
title: Elsa (workflow pool)
description: "Level-1 dataflow of Module.Elsa: the Elsa 3.9 engine in the workflow pool, the default newsroom workflows started from integration events, the newsroom activities, the dry run, the deadline poller, and the module APIs it calls."
sidebar:
  order: 14
---

`elsa` (Module.Elsa, v0.1.0) runs only in the **workflow pool**, which the gateway reaches at `/api/elsa`. It
hosts the Elsa 3.9 engine, with its own database (`ConnectionStrings:elsa`). Steps E1 and E2 are built:

- **E1:** workflows start and resume from integration events.
- **E2:** default code workflows ship with the module: `newsroom-post` (a post project) and `newsroom-content` (one
  content, as a sub-workflow). NewsRoom's bindings choose them, or a definition authored in Elsa Studio. E2 also
  adds the shift handover and a dry run.

The activities change Content, NewsRoom and Shifts through their HTTP APIs, with the `elsa-workflows`
client-credentials token. **E3 is planned:** remove the deadline poller and Content's `/automation/*` routes.

```mermaid
flowchart LR
  subgraph In[" "]
    direction TB
    Designer(["Workflow designer"]):::ext
    MQ[("RabbitMQ in")]:::store
  end

  subgraph Elsa["elsa"]
    direction TB
    E1("14.1 Workflows API<br/>+ dry run"):::custom
    E2("14.2 NewsroomEventHandler"):::custom
    E3("14.3 Default workflows<br/>post, content"):::custom
    E4("14.4 Newsroom activities"):::custom
    E5("14.5 Engine + timer sweeper"):::custom
    E6("14.6 Automation poller<br/>(removed in E3)"):::custom
  end

  subgraph Out[" "]
    direction TB
    EDb[("ElsaWorkflows DB<br/>schemas elsa, elsa_events")]:::store
    IdP(["Identity service"]):::ext
    NR("newsroom"):::custom
    CO("content"):::custom
    SH("shifts"):::custom
    Org("organization<br/>local directory"):::custom
    Live("Live hub"):::core
  end

  Designer <-->|F1| E1
  MQ -->|F2| E2
  E1 & E2 & E3 & E5 <--> EDb
  E4 <-->|F3| IdP
  E2 & E4 <-->|F4| NR
  E2 -->|starts| E3
  E4 <-->|F5| CO
  E4 <-->|F6| SH
  E4 <-->|F7| Org
  E4 -->|F8| Live
  E6 <-->|F9| CO
  E6 <-->|F10| SH

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
  style In fill:none,stroke:none
  style Out fill:none,stroke:none
```

| Flow | Route or channel | Payload |
|---|---|---|
| F1 | Elsa workflows API (`workflow.view`, `workflow.design`, `workflow.run`); `POST /api/elsa/newsroom/dry-run` (`workflow.design`, changes nothing) | workflow definitions and instances (Elsa JSON); `DryRunRequest(PostId, ...)` → what the bound workflow would do |
| F2 | queue `integration.elsa.workflow` | see the subscriptions below |
| F3 | `client_credentials` (`elsa-workflows`) | → service token |
| F4 | `GET /api/newsroom/bindings/resolve`, `projects/{id}`, `projects/{id}/workflow`, `POST projects/{id}/state`, `routing/route`, `workload/query` | `StateReport`, `RouteRequest`, `WorkloadRequest` → `BindingResolution`, `ProjectDto`, `RouteResult`, `WorkloadCounts` |
| F5 | `/api/content/posts/{id}`, `posts/{id}/unit`, `assign`, `accept`, `transfer`, `schedule`, `publish`; `contents/{id}`, `contents/{id}/assign`, `contents/{id}/unit` | `UnitAssignment`, `ContentAssignment`, `ScheduleRequest`, `VersionRequest` → `PostDetail`, `ContentDetail` |
| F6 | `GET /api/shifts/shifts?from&to`, `shifts/{id}` | → `ShiftDto` (who is on, in which role, checked in) |
| F7 | `GET /api/organization/local/units`, `{id}`, `{id}/members`, `{id}/categories` | → `DirectoryUnitDto`, `DirectoryMemberDto` |
| F8 | `Notify`, `Deadline` → `HostLiveMessenger` → `ILiveNotifier`, topic `user:{id}` or the unit's topics | `workflow.notification`, `workflow.deadline-missed`, `workflow.published`, `workflow.publish-refused` |
| F9 | `GET automation/accepted`, `POST accepted/{id}/assign`, `automation/shifts/{id}/handover`, `posts/publish-due` | → `PostSummary[]`, `PostDetail`, `int[]` |
| F10 | `GET shifts/ended?since`, `overdue` | → `EndedShiftDto[]`, `ShiftDto[]` |

**Workflows (14.3):**

- `newsroom-post` v1: `EnsurePostInUnitCategories`, `WaitForRefiling`, `ProduceContents`, `DeskReview`,
  `PublishPost`, `Notify`
- `newsroom-content` v1, one per content: `FileContentInUnit`, `WorkInShift`

A released version never changes. A change ships as the next version beside the old one, so suspended instances
still resume.

**Activities (14.4):** `RouteToUnit`, `FileContentInUnit`, `EnsurePostInUnitCategories`, `WaitForRefiling`,
`ProduceContents`, `AssignInShift`, `WorkInShift`, `WaitForStage`, `Deadline`, `DeskReview`, `PublishPost`, `Notify`.
`AssignInShift` and `WorkInShift` pick a checked-in member with the shift role (`Elsa:Newsroom:ContentRole`, by
default `writer`) and the fewest open items (F4 `workload/query`).

**Background services:** `ElsaEngineService`, `TimerSweeperService` and `NewsroomAutomationService`.

## Subscriptions

| Pattern | Data | What it does |
|---|---|---|
| `newsroom.project-approved.v1`, `newsroom.project-reapproved.v1`, `newsroom.project-binding-available.v1` | `ProjectApproved` | resolves the binding (F4) and starts the project's workflow |
| `newsroom.project-state-changed.v1` | `ProjectStateChanged` | resumes or ends the project's workflow |
| `content.post-*.v1` | `PostCreated`, `PostCategoriesChanged`, `PostUnitChanged`, `ContentFeedEvent` | resumes the post's waiting activities |
| `content.work-item-*.v1` | `WorkItemAssigned`, `WorkItemStateChanged` | resumes the content's waiting activities |
| `shifts.published`, `member-added`, `checked-in`, `checked-out`, `handover`, `ended` (`.v1`) | the Shifts records | resumes the unit's waiting activities; hands open work over at a handover or the end of a shift |

Elsa publishes no integration events. It reports back over HTTP.

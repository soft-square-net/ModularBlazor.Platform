---
title: Workflows and assignment
description: How NewsRoom binds Elsa workflows to post kinds and units, routes contents to Sections, enforces the Section categories rule, and assigns work to the people on shift.
sidebar:
  order: 3
---

:::note[Status: designed]
This is the agreed design for phases P4 (NewsRoom) and P5 (Elsa). Names may change when they are built.
:::

## Workflow bindings

A **WorkflowBinding** maps a kind and a unit to an Elsa workflow definition and version:

| Key | Workflow |
| --- | --- |
| (post kind, unit) | The **main** workflow for posts of that kind produced by that unit |
| (content kind, unit) | The **sub-workflow** for each content of that kind inside the post |

Bindings are **inherited up the tree**: a binding on a Department applies to its Sections unless a Section has its own.
A post kind with no binding anywhere up the path is a **setup error**: NewsRoom reports it and nothing is assigned.

## Routing rules

A **RoutingRule** says which unit produces a content kind, for example "videos go to the unit tagged `multimedia`".
A rule targets a unit tag, a specific unit, or (later) a unit in a linked tenant. Desk and Multimedia are both
Sections.

## The Section categories rule

A Section unit in Organization holds a list of post category ids (or `*`, meaning all). The rule:

> A post can be assigned to a unit only when at least one of the post's categories belongs to that unit.

- The Elsa activity **`EnsurePostInUnitCategories(postId, unitId)`** checks it when the moderator approves, before the
  project enters InProduction. It runs again whenever Contents reports that the post's categories or unit changed
  (`content.post-categories-changed.v1`, `content.post-unit-changed.v1`).
- On failure the workflow **returns the post to the moderator** (state NeedsModeration). It does not fail.
- **No bypass:** only the workflow's service identity holds `content.unit.assign`, so an ordinary user cannot set a
  post's unit directly and skip the check.
- Routing uses the same data to **suggest** the Section whose categories match.

## Assignment in the shift

Work goes to the people **on duty** in the target unit:

1. The workflow finds the **running shift** of the unit.
2. The shift's manager has a short delay to assign the item by hand.
3. Otherwise it is assigned automatically to the **least-loaded** person on the shift who holds the required role.
   NewsRoom keeps a read model of who is checked in and how many open items each person has, built from
   `shifts.checked-in.v1`, `shifts.checked-out.v1` and the work-item events.
4. At **handover** (`shifts.handover.v1`, `shifts.ended.v1`), open items move to the next shift of the unit.

Rights follow attendance. A shift member gets their role's permissions only from **check-in** until **check-out** (or
the scheduled end plus the grace period). Nobody on a shift has rights without checking in, and check-out revokes their
live subscriptions at once.

## Elsa activities

Phase P5 adds these activities to the Elsa module. Elsa becomes an integration-event subscriber that resumes workflow
bookmarks.

| Activity | Does |
| --- | --- |
| Route to unit | Applies the routing rules to a content kind |
| EnsurePostInUnitCategories | The categories rule above; on failure, back to the moderator |
| Assign in shift | Manager delay, then least-loaded role holder in the running shift |
| Wait for stage | Waits for a content work item to reach a stage (`content.work-item-state-changed.v1`) |
| Deadline / Escalate | Timers on a stage, escalating to the shift lead or the unit manager |
| Desk review | Moves the project to ReadyForDesk and waits for the desk's decision |
| Publish | Schedules or publishes the post through Contents |
| Notify | Sends a live notification to a user or a unit |

## Events

| Event | When |
| --- | --- |
| `newsroom.proposal-submitted.v1` | A proposal is submitted |
| `newsroom.proposal-decided.v1` | A moderator approves, cancels or returns it |
| `newsroom.project-state-changed.v1` | A PostProject changes state |
| `newsroom.work-item-assigned.v1` | A project is assigned (the shared work-item contract) |

They use the platform [integration events](/backend/integration-events/) envelope: type, org, tenant, unit path,
subject and data, written to the outbox in the same save as the change.

## Live updates

NewsRoom publishes to the unit groups of the [live hub](/backend/live-updates/), so a planning board updates as
proposals and projects move. Unit messages fan out to the ancestors, so a Department board sees its Sections' work.
"My work" (phase P6) combines the work-item events of NewsRoom, Contents and the other modules into one list per user.

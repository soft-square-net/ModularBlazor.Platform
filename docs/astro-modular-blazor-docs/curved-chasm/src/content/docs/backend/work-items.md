---
title: Work items
description: The shared work-item contract in WebApi.Contracts — WorkItemRef, WorkItemAssignee, WorkItemSnapshot and the per-module work-item-assigned and work-item-state-changed events.
sidebar:
  order: 17
---

Several modules own **work items**: Content's content items, NewsRoom's post projects, later others. Each module
keeps its own items, states and rules. What they share is a small contract in `WebApi.Contracts/WorkItems`, so other
modules ("My work", live updates, reports) can follow every module's items through
[integration events](/backend/integration-events/) without calling the owner.

## The types

| Type | Shape | Example |
| --- | --- | --- |
| `WorkItemRef` | `(Module, Type, Id)`, text `module:type:id` | `content:content:42`, `newsroom:post-project:7` |
| `WorkItemAssignee` | `(Kind, Id)`, with `Kind` = `User`, `Shift`, `Role` or `Unit` | `WorkItemAssignee.Shift("s-7")`, text `shift:s-7` |
| `WorkItemSnapshot` | `(Ref, Title, State, Assignee?, DueAt?, UnitId?)` | what a list needs to show the item |

- **`State`** is the owner's state name (`InProgress`, `InReview`). Only the owner gives it a meaning.
- **`Assignee`** is null when the item is unassigned. In JSON it is `{"kind":"shift","id":"s-7"}`.
- **`UnitId`** is the organization unit the item belongs to, if any.
- `WorkItemRef.Parse` and `TryParse` read the text form. The id may itself contain `:`.

## The events

Each module publishes its own event types, with the reference as the subject:

| Event type | Data | Helper |
| --- | --- | --- |
| `{module}.work-item-assigned.v1` | `WorkItemAssigned(Item, PreviousAssignee?)` | `WorkItemEvents.Assigned(module)` |
| `{module}.work-item-state-changed.v1` | `WorkItemStateChanged(Item, PreviousState)` | `WorkItemEvents.StateChanged(module)` |

Publishing, in the same `SaveChanges` as the change:

```csharp
var snapshot = new WorkItemSnapshot(new WorkItemRef("content", "content", item.Id.ToString()),
    item.Title, item.State.ToString(), WorkItemAssignee.User(userId), item.DueAt, item.UnitId);

events.PublishWorkItemAssigned(snapshot, previousAssignee, unitPath: item.UnitPath);
await db.SaveChangesAsync(ct);
```

`PublishWorkItemAssigned` and `PublishWorkItemStateChanged` are extensions of `IIntegrationEvents`. They take the
module from `Ref.Module`, so a module publishes under its own name.

Subscribing to every module's work items:

```csharp
Subscriptions = [WorkItemEvents.AnyAssigned, WorkItemEvents.AnyStateChanged] // "#.work-item-assigned.v1", ...

services.AddIntegrationHandler<MyWorkHandler>(WorkItemEvents.AnyAssigned);
services.AddIntegrationHandler<MyWorkHandler>(WorkItemEvents.AnyStateChanged);

public sealed class MyWorkHandler(MyWorkDbContext db) : IIntegrationEventHandler
{
    public Task HandleAsync(IntegrationEnvelope e, CancellationToken ct)
    {
        var item = WorkItemEvents.IsAssigned(e.Type)
            ? e.DataAs<WorkItemAssigned>().Item
            : e.DataAs<WorkItemStateChanged>().Item;
        // upsert the row of item.Ref in this module's own table
        return Task.CompletedTask;
    }
}
```

The patterns use `#`, not `*`, because a module id may contain dots. `WorkItemEvents.ModuleOf(type)` gives the
publishing module of a work-item event type.

These records are platform contracts, not module types, so modules may share them. A module's other events still
use its own records.

The "My work" view across modules is planned for a later phase. This contract is what it will read.

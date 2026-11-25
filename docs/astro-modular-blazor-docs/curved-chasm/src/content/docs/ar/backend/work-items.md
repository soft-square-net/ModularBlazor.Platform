---
title: عناصر العمل (Work items)
description: عقد عناصر العمل المشترك في WebApi.Contracts — WorkItemRef وWorkItemAssignee وWorkItemSnapshot وحدثا work-item-assigned وwork-item-state-changed لكل وحدة.
sidebar:
  order: 17
---

تملك عدة وحدات **عناصر عمل** (work items): عناصر المحتوى في Content، ومشاريع المنشورات في NewsRoom، وغيرها لاحقا. تحتفظ
كل وحدة بعناصرها وحالاتها وقواعدها. وما تتشاركه عقد صغير في `WebApi.Contracts/WorkItems`، لتتابع الوحدات الأخرى
("عملي"، التحديثات الحية، التقارير) عناصر كل وحدة عبر [أحداث التكامل](/ar/backend/integration-events/) دون استدعاء المالك.

## الأنواع

| النوع | الشكل | مثال |
| --- | --- | --- |
| `WorkItemRef` | `(Module, Type, Id)`، ونصه `module:type:id` | `content:content:42`، `newsroom:post-project:7` |
| `WorkItemAssignee` | `(Kind, Id)`، و`Kind` هو `User` أو `Shift` أو `Role` أو `Unit` | `WorkItemAssignee.Shift("s-7")`، ونصه `shift:s-7` |
| `WorkItemSnapshot` | `(Ref, Title, State, Assignee?, DueAt?, UnitId?)` | ما تحتاجه القائمة لعرض العنصر |

- **`State`** اسم الحالة لدى المالك (`InProgress`، `InReview`). المالك وحده يعطيها معنى.
- **`Assignee`** فارغ (null) إذا لم يسند العنصر. وفي JSON يكون `{"kind":"shift","id":"s-7"}`.
- **`UnitId`** الوحدة التنظيمية التي ينتمي إليها العنصر، إن وجدت.
- يقرأ `WorkItemRef.Parse` و`TryParse` الصيغة النصية. وقد يحتوي المعرف نفسه على `:`.

## الأحداث

تنشر كل وحدة أنواع أحداثها، والمرجع هو الموضوع (subject):

| نوع الحدث | البيانات | المساعد |
| --- | --- | --- |
| `{module}.work-item-assigned.v1` | `WorkItemAssigned(Item, PreviousAssignee?)` | `WorkItemEvents.Assigned(module)` |
| `{module}.work-item-state-changed.v1` | `WorkItemStateChanged(Item, PreviousState)` | `WorkItemEvents.StateChanged(module)` |

النشر، في `SaveChanges` نفسه الذي يحمل التغيير:

```csharp
var snapshot = new WorkItemSnapshot(new WorkItemRef("content", "content", item.Id.ToString()),
    item.Title, item.State.ToString(), WorkItemAssignee.User(userId), item.DueAt, item.UnitId);

events.PublishWorkItemAssigned(snapshot, previousAssignee, unitPath: item.UnitPath);
await db.SaveChangesAsync(ct);
```

`PublishWorkItemAssigned` و`PublishWorkItemStateChanged` امتدادان لـ`IIntegrationEvents`. يأخذان الوحدة من
`Ref.Module`، فتنشر الوحدة باسمها.

الاشتراك في عناصر عمل كل الوحدات:

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

تستخدم الأنماط `#` لا `*`، لأن معرف الوحدة قد يحتوي على نقاط. ويعطي `WorkItemEvents.ModuleOf(type)` الوحدة الناشرة
لنوع حدث من أحداث عناصر العمل.

هذه السجلات عقود للمنصة لا أنواع وحدات، فيجوز للوحدات أن تتشاركها. أما أحداث الوحدة الأخرى فتبقى بسجلاتها الخاصة.

واجهة "عملي" عبر الوحدات مخطط لها في مرحلة لاحقة، وهذا العقد هو ما ستقرؤه.

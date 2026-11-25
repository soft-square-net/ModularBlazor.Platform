---
title: أحداث التكامل (Integration events)
description: أحداث بين الوحدات أثناء التشغيل عبر صندوق صادر وصندوق وارد في مخطط كل وحدة، وRabbitMQ أو ناقل داخل العملية، واشتراكات يقرؤها المضيف من البيان.
sidebar:
  order: 14
---

**حدث التكامل** (integration event) يخبر الوحدات الأخرى بأن شيئا حدث في وحدتك: `shifts.checked-in.v1`،
`content.work-item-assigned.v1`. وهو JSON عادي داخل مغلف (envelope). لا تتشارك أي وحدة نوعا مع وحدة أخرى، ولا يحمل
المضيف أيا منها، فتبقى كل وحدة قابلة للتفريغ.

```
module A: SaveChanges ─► outbox row (same transaction) ─► after commit ─► IBrokerPublisher
                                                                                  │
                         topic exchange "integration" (routing key = event type) ◄┘
                                │
           durable queue integration.{module}.{pool} ─► consumer (one per replica) ─► module B: inbox + handlers (one transaction)
```

## الضمانات

- **لا يرسل شيء إذا تراجع التغيير.** يضيف `Publish` صفا إلى الصندوق الصادر (outbox) في `DbContext` الخاص بالوحدة فقط،
  ويرسل الصف بعد اعتماد `SaveChanges` (أو المعاملة).
- **لا يضيع شيء إذا توقفت العملية بعد الاعتماد.** يرسل كانس الصندوق الصادر (outbox sweeper) الصفوف التي لم ترسل، لكل
  مستأجر نشط.
- **يعالج المشترك الحدث مرة واحدة.** يسجل صندوقه الوارد (inbox) معرف الحدث في المعاملة نفسها التي تحمل تغييرات المعالج،
  ويسقط أي تسليم ثان.
- **تنتظر الأحداث المشترك المفرغ.** تعلن طابوره الدائم عند التثبيت، ويبقى أثناء تفريغ الوحدة، ويحذف عند إلغاء التثبيت.
- **يعاد الحدث الفاشل ثم ينقل إلى الرسائل الميتة.** يعرض المسؤولون الرسائل الميتة ويطلعون عليها ويعيدون تشغيلها عبر
  `/api/admin/integration/dead-letters` (الصلاحية `platform.modules.manage`).

## المغلف وأسماء الأنواع

`IntegrationEnvelope(Id, Type, TenantId, Subject, OccurredAt, CorrelationId, Source, Data, OrgId?, UnitPath?)`.

- **Type:** كلمات بأحرف صغيرة تفصلها نقاط، وتنتهي برقم الإصدار: `{module}.{event}.v{n}`. لا تغير شكل إصدار منشور أبدا؛
  انشر `v2` بجانبه بدلا من ذلك.
- **Subject:** ما يدور عنه الحدث، مثل `post:42`.
- **UnitPath:** معرفات الوحدات التنظيمية من الجذر تفصلها `/` (بالصيغة نفسها لـ[مواضيع التحديثات الحية](/ar/backend/live-updates/)).
- **CorrelationId:** يتشاركه الحدث وكل حدث ينشره معالجوه.

## النشر من وحدة

1. اربط الصندوقين الصادر والوارد في `DbContext` الخاص بالوحدة. هذا اختياري، فتبقى نماذج الوحدات التي لا تستخدم الأحداث
   كما هي:

   ```csharp
   public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ITenantContext tenant)
       : TenantDbContext(options, tenant)
   {
       protected override string Schema => "inventory";
       protected override bool UsesIntegrationEvents => true; // maps integration_outbox and integration_inbox
   }
   ```

2. أضف ترحيلا واحدا لكل محرك قاعدة بيانات (انظر [ترقية وحدة إلى preview.7](/ar/backend/upgrading-to-preview-7/)).
3. سجل الخدمات في `ConfigureServices` الخاصة بالوحدة:

   ```csharp
   services.AddTenantDbContext<InventoryDbContext>();
   services.AddIntegrationEvents<InventoryDbContext>();
   ```

4. انشر في وحدة العمل نفسها التي تحمل التغيير:

   ```csharp
   public sealed record StockReserved(Guid ItemId, int Quantity); // the module's own event record

   db.Reservations.Add(reservation);
   events.Publish("inventory.stock-reserved.v1", new StockReserved(item.Id, quantity), subject: $"item:{item.Id}");
   await db.SaveChangesAsync(ct); // the row commits with the reservation; it is sent after the commit
   ```

يحتاج `Publish` إلى نطاق مستأجر: الحدث ينتمي دائما إلى المستأجر الذي وقع فيه.

## الاشتراك

يعلن المشترك الأنماط مرتين: في البيان (manifest)، ليربط المضيف طابوره دون تحميل الوحدة، وفي خدماته، ليعمل المعالج
الصحيح.

```csharp
public ModuleManifest Manifest { get; } = new("reports", "Reports", "1.0.0", "reports")
{
    Subscriptions = ["inventory.stock-*.v1"]
};

public void ConfigureServices(IServiceCollection services, IConfiguration settings)
{
    services.AddTenantDbContext<ReportsDbContext>();
    services.AddIntegrationEvents<ReportsDbContext>();               // the inbox lives here too
    services.AddIntegrationHandler<StockHandler>("inventory.stock-*.v1");
}

public sealed record StockReservedCopy(Guid ItemId, int Quantity);   // the subscriber's own copy

public sealed class StockHandler(ReportsDbContext db) : IIntegrationEventHandler
{
    public Task HandleAsync(IntegrationEnvelope e, CancellationToken ct)
    {
        var data = e.DataAs<StockReservedCopy>();
        db.Movements.Add(new Movement(e.Id, data.ItemId, -data.Quantity));
        return Task.CompletedTask;                                    // no SaveChanges: the inbox saves it
    }
}
```

- **الأنماط:** `*` داخل كلمة تطابق أي أحرف عدا النقطة (`org.unit-*.v1`)؛ والكلمة التي هي `#` تماما تطابق صفرا أو أكثر من
  الكلمات (`org.#`، `#.work-item-assigned.v1`).
- **تعمل المعالجات في نطاق مستأجر** من حاوية المشترك نفسه، بعد ترحيلاته لذلك المستأجر.
- **لا تستدع `SaveChanges`** في المعالج: تحفظ المنصة تغييرات المعالج مع صف الصندوق الوارد. إذا رمى المعالج استثناء
  تراجع الاثنان وأعيدت محاولة الحدث.
- **النشر من داخل المعالج مسموح.** يحتفظ الحدث الجديد بمعرف الترابط.

## إعدادات المضيف

| الإعداد | الافتراضي | المعنى |
| --- | --- | --- |
| `ConnectionStrings:broker` | لا يوجد | RabbitMQ. بدونه يسلم ناقل داخل العملية إلى الوحدات المحملة في العملية نفسها؛ ولا شيء دائم. |
| `Integration:MaxAttempts` | `5` | مرات تسليم الحدث الواحد إلى الوحدة الواحدة قبل نقله إلى الرسائل الميتة. |
| `Integration:RetryDelay` | `00:00:05` | الانتظار قبل إعادة تسليم حدث فاشل. |
| `Integration:Prefetch` | `16` | الأحداث غير المؤكدة التي يحملها مستهلك الوحدة في النسخة الواحدة. |

ولكل وحدة، يضبط `AddIntegrationEvents<T>(o => ...)` القيم `Source` و`SweepInterval` و`SweepDelay` و`Retention` (نافذة
التكرار في الصندوق الوارد، 7 أيام) و`BatchSize`.

## اشتراكات المضيف

يستطيع المضيف نفسه أن يشترك، للإشارات التي لا تخص وحدة بعينها. يستخدمها مسوي بذور المستأجرين ليشغل دورة فور وصول
`tenancy.seed-changed.v1` (انظر [بذور المستأجرين](/ar/backend/tenant-seeds/)).

```csharp
internal sealed class SeedChangedTrigger(TenantSeedReconciler reconciler) : IHostIntegrationHandler
{
    public Task HandleAsync(IntegrationEnvelope e, CancellationToken ct) { reconciler.Trigger(); return Task.CompletedTask; }
}

services.AddHostIntegrationHandler<SeedChangedTrigger>(TenantSeedNames.SeedChangedEvent);
```

اشتراك المضيف ليس اشتراك وحدة:

| | اشتراك الوحدة | اشتراك المضيف |
| --- | --- | --- |
| من يستقبل | نسخة واحدة من كل مجموعة تشغل الوحدة | **كل** النسخ |
| الطابور | دائم `integration.{module}.{pool}` | حصري، يحذف مع النسخة |
| النطاق | نطاق مستأجر من حاوية الوحدة | الحاوية الجذرية للمضيف، **بلا مستأجر** (قد يكون مستأجر المغلف فارغا) |
| التكرار | جدول الصندوق الوارد، في معاملة المعالج | قائمة في الذاكرة بمعرفات الأحداث الأخيرة لكل نسخة |
| الفشل | إعادة محاولة ثم رسائل ميتة | إعادة محاولة داخل العملية، ثم تسجيل وإسقاط |

لذلك معالجات المضيف للإشارات التي ينجز عملها أيضا على مؤقت أو عند البدء. والعمل الدائم مكانه وحدة.

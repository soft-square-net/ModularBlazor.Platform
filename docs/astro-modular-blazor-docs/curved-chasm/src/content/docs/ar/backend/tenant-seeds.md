---
title: بذور المستأجرين (Tenant seeds)
description: كيف يصف نظام خارجي (خدمة Organization) المستأجرين الذين يجب أن يملكهم النشر، وكيف يسويهم المضيف في مخزن المستأجرين بسياسة توزيع وقائد واحد.
sidebar:
  order: 16
---

**بذرة المستأجر** (tenant seed) تصف مستأجرا يجب أن يملكه النشر: معرفه واسمه ومضيفاته وموضع قاعدة بياناته المطلوب
ووحداته ومسؤوليه. يملك البذور نظام خارجي، هو خدمة Organization، ويحولها المضيف إلى مستأجرين.

```
Organization connector (a module) ─ITenantSeedSource─► TenantSeedReconciler (host, one leader)
        │                                                   │ placement policy → tenant store → NotifyChanged
        └── tenancy.seed-changed.v1 ──► every replica ──────┘ admins' grants → ReportAsync(status)
```

## التفعيل

- اضبط `Platform:DeploymentId` على كل نسخ النشر. ما دام غير مضبوط، لا يفعل المسوي شيئا.
- حمل وحدة تسجل `ITenantSeedSource` في حاويتها. يحله المضيف في نطاق جديد في كل دورة، ويشغل دورة فور تحميل وحدة كهذه.
- يحتاج المضيف إلى مخزن مستأجرين (`ConnectionStrings:TenantStore`).

## العقد

```csharp
public sealed record TenantSeed(string Id, long Version, string DeploymentId, string TenantId, string Name,
    IReadOnlyList<string> Hosts, string DatabaseServer, string DatabaseName,
    IReadOnlyList<string> Modules, IReadOnlyList<string> Admins, JsonElement Extra = default);

public interface ITenantSeedSource
{
    Task<IReadOnlyList<TenantSeed>> ListAsync(string deploymentId, CancellationToken ct);
    Task ReportAsync(string seedId, long version, SeedStatus status, TenantPlacement? actualPlacement, string? reason, CancellationToken ct);
}
```

- **يجب أن يزيد `Version` مع كل تغيير.** الإصدار الذي يملكه المستأجر بالفعل لا يغير شيئا.
- **`Admins`** معرفات أصحاب الرموز (token subjects). يحصلون على منحة دائمة `access.grants.manage` على النطاق `tenant`،
  تحت مصدر المنح `seed:{seedId}`، وتستبدل مع كل إصدار مطبق.
- **`Extra`** للمصدر (الهوية البصرية، شريحة من شجرة الوحدات التنظيمية). يتجاهله المضيف.
- الحالات هي `Requested` و`Provisioning` و`Provisioned` و`Adjusted` و`Rejected` و`Failed`.

## دورة واحدة

يعمل المسوي كل `Tenancy:Seeds:Interval` (دقيقة واحدة)، وعند تحميل مصدر بذور، وعند وصول `tenancy.seed-changed.v1`. تقود
كل دورة نسخة واحدة: عبر Redis (`ConnectionStrings:redis`) إن وجد، وإلا فعبر قفل قاعدة بيانات مخزن المستأجرين. لكل بذرة
لهذا النشر جديدة أو أحدث من مستأجرها:

1. **الموضع** (للمستأجرين الجدد فقط): تقبل `ITenantPlacementPolicy` الموضع المطلوب، أو تعدله، أو ترفض البذرة. الافتراضية،
   `AcceptAsIsPlacementPolicy`، تقبله. سجل غيرها في المضيف لتستبدلها.
2. **المستأجر** ينشأ (قاعدة البيانات أولا) أو يحدث اسمه ومضيفاته. وتبلغ كل النسخ، فتعمل `ITenantInitializer` الخاصة
   بالوحدات.
3. **منح المسؤولين** تكتب.
4. **الحالة تبلغ** إلى المصدر. وبعدها فقط يسجل الإصدار على المستأجر، فتعاد الخطوة الفاشلة أو التبليغ الفاشل في الدورة
   التالية.

لا تنقل البذرة قاعدة بيانات مستأجر موجود أبدا. وإذا اختلف الموضع المطلوب عن الفعلي، يبلغ المضيف `Adjusted` مع الموضع
الفعلي.

يرفض المضيف البذرة إذا لم يكن معرف المستأجر slug، أو إذا كان المستأجر موجودا وأنشئ يدويا أو من بذرة أخرى، أو إذا كانت
إحدى المضيفات لمستأجر آخر، أو إذا كان خادم قاعدة البيانات غير معروف. ويبلغ الرفض مرة واحدة لكل إصدار.

## التسوية الآن: `tenancy.seed-changed.v1`

ينشر مصدر البذور `TenantSeedNames.SeedChangedEvent` (`tenancy.seed-changed.v1`) عبر صندوقه الصادر عندما تتغير بذرة من
بذور النشر. يستقبله المضيف على **كل** النسخ عبر [اشتراك مضيف](/ar/backend/integration-events/#اشتراكات-المضيف) ويستدعي
`TenantSeedReconciler.Trigger()`. ولا يشغل الدورة إلا القائد. لا يحمل الحدث شيئا يحتاجه المسوي، فيتجاهل مستأجره
وبياناته.

الحدث الفائت يكلف فترة واحدة على الأكثر: يلتقط المؤقت التغيير.

## الإعدادات

| الإعداد | الافتراضي | المعنى |
| --- | --- | --- |
| `Platform:DeploymentId` | لا يوجد | معرف هذا النشر. مطلوب. |
| `Tenancy:Seeds:Interval` | `00:01:00` | كم مرة يسحب القائد البذور. |
| `Tenancy:Seeds:LeaderLease` | `00:10:00` | المدة التي قد تحمل فيها الدورة الواحدة قفل Redis. |
| `Tenancy:Seeds:InitializationTimeout` | `00:05:00` | المدة التي ينتظر فيها المسوي مهيئات المستأجر في هذه النسخة. |
| `Tenancy:Seeds:LockConnectionName` | `redis` | اسم سلسلة اتصال Redis؛ وقفل قاعدة البيانات بدونه. |

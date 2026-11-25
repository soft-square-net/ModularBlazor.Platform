---
title: ترقية وحدة إلى preview.7
description: ما يتغير لوحدة تعمل أثناء التشغيل عند انتقالها إلى ModularBlazor 1.0.0-preview.7، وترحيل الصندوق الصادر الوحيد الذي تحتاجه الوحدة فقط إذا استخدمت أحداث التكامل.
sidebar:
  order: 18
---

يضيف الإصدار `1.0.0-preview.7` [أحداث التكامل](/ar/backend/integration-events/) و[التحديثات الحية](/ar/backend/live-updates/)
و[بذور المستأجرين](/ar/backend/tenant-seeds/) و[عقد عناصر العمل](/ar/backend/work-items/). كل ذلك اختياري: الوحدة التي لا
تستخدم شيئا منه ترفع إصدارات حزمها فقط.

## 1. رفع الحزم (كل وحدة)

تثبت Module SDK كل حزم عقود المنصة (`WebApi.Contracts` و`WebApi.Data` و`ModularBlazor.Abstractions` ...) على إصدارها هي،
فيكفي سطر واحد لنقلها كلها. في `global.json`:

```json
{
  "msbuild-sdks": {
    "soft.square.net.ModularBlazor.Module.Sdk": "1.0.0-preview.7"
  }
}
```

المشاريع التي تشير إلى حزم المنصة دون SDK (مشاريع الاختبار في قوالب الوحدات) تقرأ الإصدار من
`ModularBlazorPlatformVersion`؛ اضبطه على `1.0.0-preview.7` أيضا.

ابن المشروع وشغل الاختبارات. **الوحدة التي لا تستخدم أحداث التكامل لا تحتاج إلى أي ترحيل.** لا يربط جدولا الصندوق
الصادر والوارد إلا إذا طلبهما `DbContext`، فيبقى نموذجها، وفحص EF للتغييرات المعلقة في النموذج، كما كانا.

الوحدة المبنية على إصدار معاينة سابق تبقى قابلة للتحميل على مضيف preview.7. أما الوحدة التي تستخدم الميزات الجديدة
فتحتاج إلى مضيف preview.7.

## 2. أحداث التكامل (فقط للوحدات التي تستخدم `UsesIntegrationEvents`)

لا يحتاج هذه الخطوات إلا وحدة تنشر أو تشترك.

1. اربط الجداول في `DbContext` الخاص بالوحدة:

   ```csharp
   protected override bool UsesIntegrationEvents => true;
   ```

2. **أضف ترحيل الصندوق الصادر الوحيد، مرة لكل محرك قاعدة بيانات** تشحن الوحدة ترحيلاته. من مستودع Platform:

   ```powershell
   ./src/WebApi/scripts/Add-Migration.ps1 -Project <path to the module's Infrastructure project> -Name IntegrationEvents
   ```

   ينشئ الترحيل `integration_outbox` و`integration_inbox` في مخطط الوحدة نفسها. ويطبق عند تفعيل الوحدة على مضيف
   preview.7، لكل مستأجر نشط.

3. سجل الخدمات، والمعالجات إذا كانت الوحدة تشترك:

   ```csharp
   services.AddIntegrationEvents<MyModuleDbContext>();
   services.AddIntegrationHandler<SomethingHappenedHandler>("other.something-*.v1");
   ```

4. اذكر الأنماط نفسها في البيان، ليربط المضيف طابور الوحدة عند التثبيت:

   ```csharp
   Subscriptions = ["other.something-*.v1"]
   ```

## 3. التحديثات الحية (اختياري)

- أعلن أنواع المواضيع وصلاحياتها في البيان: `LiveTopics = [new LiveTopic("item", "mymodule.view")]`.
- انشر عبر `ILiveNotifier` بعد اعتماد التغيير.
- اختياريا، سجل `ILiveScopeResolver` في خدمات الوحدة لنطاقات المورد الأخرى.

العملاء لا يرسلون نطاقات أبدا: `Subscribe` في المركز يأخذ الموضوع فقط.

## 4. عناصر العمل (اختياري)

الوحدة التي تملك عناصر عمل تنشر `{module}.work-item-assigned.v1` و`{module}.work-item-state-changed.v1` عبر
`PublishWorkItemAssigned` و`PublishWorkItemStateChanged`. وتحتاج إلى الخطوة 2.

## 5. بذور المستأجرين (للمضيفات فقط)

لا تغير البذور شيئا في الوحدات، إلا الوحدة التي تخدم البذور (موصل Organization): تسجل `ITenantSeedSource` في خدماتها
وتنشر `tenancy.seed-changed.v1` عندما تتغير بذرة. والمضيف الذي يستخدم البذور يضبط `Platform:DeploymentId`.

## قائمة التحقق

- [ ] كل حزم المنصة على `1.0.0-preview.7`؛ والبناء والاختبارات تنجح.
- [ ] تستخدم أحداث التكامل: `UsesIntegrationEvents => true`، وترحيل `IntegrationEvents` لكل محرك،
      و`AddIntegrationEvents<T>()`، والمعالجات و`Subscriptions`.
- [ ] تنشر تحديثات حية: `LiveTopics` في البيان.
- [ ] المضيف الذي تعمل عليه preview.7.

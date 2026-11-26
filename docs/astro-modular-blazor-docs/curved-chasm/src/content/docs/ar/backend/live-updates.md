---
title: التحديثات الحية (Live updates)
description: مركز SignalR في المضيف /hubs/live، ومواضيع تصرح من بيانات البيان ومن نطاقات يحددها الخادم، وILiveNotifier للوحدات وILiveChannel لعميل Blazor.
sidebar:
  order: 15
---

مركز SignalR واحد في المضيف، `/hubs/live`، يدفع رسائل صغيرة من نوع "تغير شيء ما" إلى عميل Blazor. ثم يعيد العميل قراءة
البيانات عبر نقاط النهاية الخاصة بالوحدة. لا تضيف الوحدة أي كود للمركز: تنشر عبر `ILiveNotifier`، ويقول بيانها أي
المواضيع تقدم ومن يحق له استقبالها.

```
module (any pool) ─ILiveNotifier─► connections on this replica ─┐
                        └─► RabbitMQ fanout "live" ─► every other replica ─► its connections
browser ─WebSocket /hubs/live?access_token=…─► Subscribe(topic)
```

## المواضيع

لا يسمي الموضوع المستأجر أبدا: يستخدم المركز دائما مستأجر الاتصال.

| الموضوع | يبنى بـ | من يحق له الاشتراك |
| --- | --- | --- |
| `user:{id}` | `LiveTopics.User(id)` | ذلك المستخدم فقط |
| `{moduleId}:{kind}:{key}` | `LiveTopics.Of("content", "post", "42")` | حاملو الصلاحية التي تعلنها الوحدة لهذا النوع |
| `unit:{id}` | `LiveTopics.Unit(id)` | حاملو صلاحية النوع `unit`، التي تعلنها الوحدة الوحيدة المزودة للوحدات التنظيمية (Organization) |

## التصريح بيانات، ومن الخادم فقط

يرسل العميل **اسم الموضوع فقط**. النطاقات التي يرسلها العميل لا تحتسب أبدا.

1. تعلن الوحدة أنواع مواضيعها في البيان، ويقرؤها المضيف عند التثبيت:

   ```csharp
   public ModuleManifest Manifest { get; } = new("content", "Content", "1.0.0", "content")
   {
       LiveTopics = [new LiveTopic("post", "content.view")]
   };
   ```

2. للموضوع `content:post:42` يتحقق المضيف من `content.view` على النطاق `post:42`.

   المورد المرئي عبر **عدة صلاحيات** (preview.8) يعلن النوع مرة واحدة بـ`LiveTopic.AnyOf`، ويكفي امتلاك أي واحدة منها
   للمورد:

   ```csharp
   LiveTopics = [LiveTopic.AnyOf("post", "content.author", "content.accept", "content.publish")]
   ```

   يبقى `new LiveTopic(kind, permission)` كما كان. يضع `AnyOf` الصلاحية الأولى في `Permission` والبقية في
   `OrPermissions`، و`Permissions` تسردها كلها. السجلات المثبتة قبل preview.8 تقرأ دون تغيير.

   **يعلن النوع مرة واحدة.** يرفض التثبيت بيانا يعلن النوع نفسه مرتين (استخدم `AnyOf` بدلا من ذلك)، ونوعا ليس بأحرف
   صغيرة `a-z` و`0-9` و`-`، ونوع المنصة `user`، والصلاحيات الفارغة. وسجل وحدة مثبت سابقا لا يزال يعلن نوعا مرتين لا يحصل
   على مواضيع من ذلك النوع (يرفض، لا يدمج).
3. اختياريا، تضيف الوحدة نطاقات المورد الأخرى (مطبعته، فئته، وحداته التنظيمية الأم) بـ`ILiveScopeResolver` مسجل في
   **خدماتها هي**:

   ```csharp
   public sealed class PostScopes(ContentDbContext db) : ILiveScopeResolver
   {
       public async Task<IReadOnlyList<string>> ScopesAsync(string kind, string key, CancellationToken ct) =>
           kind == "post" && Guid.TryParse(key, out var id)
               ? await db.Posts.Where(p => p.Id == id).Select(p => new[] { $"press:{p.PressId}", $"unit:{p.UnitId}" }).FirstOrDefaultAsync(ct) ?? []
               : [];
   }

   services.AddScoped<ILiveScopeResolver, PostScopes>();
   ```

   يحله المضيف لكل اشتراك من حاوية الوحدة، في مستأجر المشترك، ولا يحتفظ إلا بالنصوص التي يعيدها.

**يفشل مغلقا.** بلا محلل، أو إذا لم تكن الوحدة محملة في نسخة المركز (مجموعة core)، أو إذا رمى المحلل استثناء، لا يحتسب
إلا النطاق المطابق تماما. وبلا مزود للوحدات التنظيمية، أو مع مزودين اثنين، ترفض مواضيع `unit:`.

عندما تتغير منح المستخدم (عند تسجيل الخروج من المناوبة مثلا) تعيد كل نسخة فحص اشتراكاته، وتسقط ما لم يعد مسموحا،
وترسل `Revoked(topic)` إلى العميل.

## النشر من وحدة

```csharp
public sealed class PostPublished(ILiveNotifier live)
{
    public Task NotifyAsync(Post post, CancellationToken ct) =>
        live.PublishAsync(LiveTopics.Of("content", "post", post.Id.ToString()), "post-updated",
            JsonSerializer.SerializeToElement(new { post.Id, post.State }), sequence: post.Version, ct);
}
```

- `ILiveNotifier` بنطاق (scoped) ويستخدم المستأجر الحالي.
- الحمولة `JsonElement`، فلا يرى المضيف أي نوع من أنواع الوحدة. اجعلها صغيرة؛ يعيد العميل قراءة البيانات.
- `PublishToUnitAsync("root/region/desk", ...)` يرسل إلى الوحدة التنظيمية وإلى كل أصولها.
- **انشر بعد اعتماد التغيير.** الرسائل بأفضل جهد. العميل الذي فاتته رسالة يلحق من موجز الوحدة نفسها (`?after={sequence}`).

## عميل Blazor: `ILiveChannel`

```csharp
@inject ILiveChannel Live

await using var subscription = await Live.SubscribeAsync(
    $"content:post:{PostId}", OnMessageAsync, fromSequence: lastSeen,
    new LiveSubscribeOptions { OnResync = r => ReloadAfterAsync(r.LastSequence), OnRevoked = _ => CloseAsync() });
```

- يوجد `ILiveChannel` في `ModularBlazor.Abstractions`؛ والموضوع هو النص نفسه الذي تنشر إليه الوحدة.
- تتصل القناة ما دام المستخدم مسجل الدخول، وتعيد الاتصال بتراجع تدريجي وتشترك من جديد.
- تسقط التسلسلات المكررة، وتطلق إعادة مزامنة (resync) بعد إعادة الاتصال أو عند وجود فجوة.
- `State` هي `Connected` أو `Reconnecting` أو `Offline`. ويعرضها Layouts.Default في شريط الأدوات.

## النشر (Deployment)

- توجه البوابة `/hubs/live` مع WebSockets إلى مجموعة core. يتخطى العميل التفاوض، فلا حاجة إلى جلسات لاصقة.
- بلا وسيط رسائل، تصل الرسائل إلى اتصالات النسخة الناشرة فقط.

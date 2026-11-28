---
title: الواجهة البرمجية وتسجيل الدخول والمستأجرون
description: استدعاء WebApi عبر بوابتها من Astro أثناء البناء وفي المتصفح، ومشاركة تسجيل الدخول بـ OpenID Connect مع عميل Blazor، وCORS، والترويسة X-Tenant.
sidebar:
  order: 4
---

الواجهة البرمجية للمنصة هي WebApi خلف بوابة YARP ‏(`http://localhost:5080` في المختبر). يسجل المستخدمون دخولهم في خدمة
الهوية OpenIddict ‏(`http://localhost:5090`)، ويستدعي عميل Blazor ‏(`ModularBlazor.BlzWebClient`، ‏`http://localhost:5070`)
البوابة برمز الوصول الخاص بهم. ويتحدث موقع Astro مع البوابة نفسها.

## وقت البناء: البيانات العامة فقط

تستطيع صفحات Astro استدعاء `fetch` في الواجهة الأمامية للملف (frontmatter). تعمل هذه الشيفرة **حين يبنى الموقع** (أو مع
كل طلب مع محول SSR)، ولا تعمل أبدا في متصفح الزائر. فاستخدمها لنقاط النهاية المجهولة فقط:

```astro title="src/pages/modules.astro"
---
const api = import.meta.env.WEBAPI_BASE_URL ?? 'http://localhost:5080';
// Add an X-Tenant header to get one tenant's manifest.
const manifest = await fetch(`${api}/api/client-modules/manifest`).then(r => r.json());
---
<ul>{manifest.modules.map((m: { id: string; name: string; version: string }) => <li>{m.name} {m.version}</li>)}</ul>
```

يجيب `GET /api/client-modules/manifest` المستدعين المجهولين بالوحدات المسموحة للجميع (القوالب). أما نقاط نهاية الوحدات
(`/api/content/...` و`/api/tenancy/...` و`/api/admin/...`) فتحتاج إلى رمز، ولا يجوز أبدا تضمين رمز في HTML ثابت. شكل البيان
`{ etag, modules: [{ id, name, version, kind, url, sha256, … }] }`؛ وكل نقاط النهاية موصوفة في مستندات OpenAPI
(`/openapi/webapi.json` و`/openapi/modules/<id>.json`، وScalar على `/scalar`).

## في المتصفح: CORS

لا تجيب المجموعات وخدمة الهوية عن الطلبات القادمة من أصل آخر إلا من الأصول المذكورة في `Cors:AllowedOrigins` (تطابق تام،
بلا أحرف بدل، وبلا بيانات اعتماد: الرمز ينتقل في ترويسة). أضف أصول Astro للتطوير والإنتاج:

```json title="src/WebApi/WebApi/appsettings.Development.json"
{
  "Cors": {
    "AllowedOrigins": [ "http://localhost:5070", "http://localhost:4321" ]
  }
}
```

في مختبر Hybird، تأخذ كل نسخة من نسخ المجموعات `Cors__AllowedOrigins__0` لعميل Blazor في `AddPool`
(`src/Aspire/Hybird/AppHost.cs`)؛ فأضف بجانبه `Cors__AllowedOrigins__1` لخادم تطوير Astro. وتمرر البوابة الترويسات كما هي.

## مشاركة تسجيل الدخول

يسجل عميل Blazor الدخول بـ OpenID Connect ‏(Authorization Code مع PKCE، عبر `AddOidcAuthentication`). وتحفظ مكتبة المصادقة
في Blazor المستخدم المسجل، ومعه رمز الوصول، في **`sessionStorage`** تحت المفتاح `oidc.user:<authority>:<client id>`؛ وفي
المختبر `oidc.user:http://localhost:5090/:blazor-client`. وحين يخدم Astro والعميل من **الأصل نفسه** ويسجل المستخدم دخوله
**في التبويب نفسه**، يستطيع سكربت في Astro قراءته:

```ts
const key = 'oidc.user:http://localhost:5090/:blazor-client';
const user = JSON.parse(sessionStorage.getItem(key) ?? 'null');
if (user?.access_token && user.expires_at * 1000 > Date.now()) {
  const me = await fetch(`${apiBase}/api/identity/me`, {
    headers: { Authorization: `Bearer ${user.access_token}` },
  });
  if (me.ok) showUser(await me.json());
} else {
  // Not signed in here, or expired: link to /app/login. The client renews or asks to sign in again.
}
```

- اقرأ المستخدم فقط. التجديد وتسجيل الخروج من مسؤولية عميل Blazor، كي يكون للجلسة مالك واحد.
- `sessionStorage` خاص بكل تبويب: الصفحة المفتوحة في تبويب جديد لا ترى مستخدما حتى يعمل العميل فيه مرة (فيجدد حينها بصمت
  ما دامت ملفات تعريف الجلسة في خدمة الهوية صالحة).
- الفحوص في جهة العميل لتحسين التجربة فقط. وتفرض WebApi الصلاحيات (`RequirePermission`) على كل طلب.
- أي سكربت على الأصل يستطيع قراءة الرمز. أبعد سكربتات الأطراف الثالثة عن الصفحات التي تقرؤه، واضبط سياسة أمان المحتوى (CSP).

## المستأجرون

تحدد WebApi مستأجر كل طلب من الترويسة `X-Tenant`، ثم من اسم المضيف؛ ومع مستخدم مسجل تكون مطالبة `tenant` في الرمز هي المرجع:

| الطلب | النتيجة |
| --- | --- |
| مسجل، بلا `X-Tenant` وبلا مضيف لمستأجر | مستأجر الرمز |
| مسجل، و`X-Tenant` يساوي مستأجر الرمز | ذلك المستأجر |
| مسجل، و`X-Tenant` يسمي مستأجرا آخر | 403 |
| `X-Tenant` واسم مضيف لمستأجر غير متفقين | 400 |
| مستأجر غير معروف / موقوف | 404 / 403 (على نقطة `TenantOptional` يعمل المستأجر غير المعروف مستدعيا أجنبيا بلا مستأجر: انظر [المستأجرون والبيانات](/ar/backend/tenancy-data/)) |

فالاستدعاء المسجل لا يحتاج إلى الترويسة. أما الاستدعاء المجهول من موقع خاص بكل مستأجر (اسم مضيف لكل مستأجر مثلا) فيرسلها:

```ts
fetch(url, { headers: { 'X-Tenant': tenantId } });
```

والرمز لمستأجر آخر يعني تسجيل دخول جديدا: يمرر العميل `tenant=<id>` في طلب التفويض.

## الربط بالتطبيق

الروابط العادية تعمل حين ينشر العميل تحت `/app/` (انظر [الاستضافة](/ar/guides/hosting/)):

| الرابط من Astro | يفتح |
| --- | --- |
| `/app/` | الصفحة الرئيسية للعميل (القالب وحدة بدء) |
| `/app/login` | إعادة توجيه إلى صفحة الدخول في خدمة الهوية، ثم العودة |
| `/app/dashboard` | وحدة عميل كسولة (هنا نموذج Dashboard بعد تثبيته)، تنزل عند التنقل |

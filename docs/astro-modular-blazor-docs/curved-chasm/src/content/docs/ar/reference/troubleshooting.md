---
title: حل المشكلات
description: مشكلات شائعة حين تتشارك صفحات Astro وعميل Blazor موقعا واحدا، وكيفية حلها.
sidebar:
  order: 2
---

## صفحة مفقودة تعرض شاشة Blazor "Loading core modules…"

بديل SPA مفعل على مستوى الموقع كله. احصره في `/app/` (راجع [الاستضافة](/ar/guides/hosting/#الإنتاج-موقع-nginx-واحد)).

## العميل يحمل، لكن الوحدات لا تحمل

- يجب أن تكون `<base href>` في `index.html` الخاص بالعميل هي `/app/` حين يخدم من `/app/`.
- تأتي الوحدات من WebApi ‏(`api/client-modules/files/…`) لا من الموقع: تحقق من `WebApi:BaseUrl` في `appsettings.json`
  الخاص بالعميل ومن تبويب الشبكة في المتصفح. وعدم تطابق SHA-256 يعني ذاكرة تخزين قديمة أمام البوابة.

## يفشل تسجيل الدخول برسالة "invalid redirect_uri"

لا تقبل خدمة الهوية إلا عناوين إعادة التوجيه المسجلة للعميل `blazor-client` ‏(`Identity:BlazorClient:RedirectUris`).
أضف `<site>/app/authentication/login-callback` و`logout-callback` المقابل حين ينتقل العميل إلى `/app/`.

## "Could not find any element matching selector '#app'"

شغل **العميل** في صفحة Astro. تحتاج الجزر إلى مضيف الجزر المنفصل، الذي يسجل المكونات عبر
`RegisterForJavaScript` ولا يضيف `App` إلى `#app`.

## جزيرة تبقى على بديلها

- راجع وحدة التحكم بحثا عن أخطاء 404 تحت `/islands/_framework/`: مضيف الجزر غير منشور هناك، أو أن
  `loadBootResource` يشير إلى مكان آخر.
- يجب أن يطابق المعرف في `<BlazorIsland component="…">` القيمة في `RegisterForJavaScript(identifier: "…")` تماما.
- يجب أن تطابق `params` أسماء معاملات `[Parameter]` في المكون وأنواعها.

## أخطاء CORS في المتصفح

أضف أصل الصفحة بدقة (البروتوكول والمضيف والمنفذ) إلى `Cors:AllowedOrigins` في مجموعات WebApi وفي خدمة الهوية (في المختبر:
`Cors__AllowedOrigins__<n>` في `src/Aspire/Hybird/AppHost.cs`).

## الوضع الداكن يختلف بين الموقع والتطبيق

هما على أصلين مختلفين (منفذين مختلفين في التطوير)، فلا يتشاركان `localStorage`. اخدمهما من أصل واحد، أو استخدم
وكيل التطوير المذكور في [الاستضافة](/ar/guides/hosting/#التطوير-المحلي).

## السلوكيات تتوقف بعد التنقل

مع `<ClientRouter />` في Astro، أعد تشغيل `autoInit` عند `astro:page-load` وتخلص منه عند `astro:before-swap`
([MudTail في Astro](/ar/guides/mudtail-in-astro/#مع-انتقالات-العرض)).

## النص العربي يبدو مكسورا أو متباعد الحروف

اضبط `lang="ar"` مع `dir="rtl"` (أو استدع `setLocale('ar')`). عندها يختار MudTail خطوطا تحتوي على الحروف
العربية ويلغي تباعد الحروف الذي يكسر اتصالها.

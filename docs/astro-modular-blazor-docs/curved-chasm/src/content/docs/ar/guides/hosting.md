---
title: الاستضافة والنشر
description: موقع nginx واحد يخدم Astro على / وعميل Blazor على /app/، وبناء Docker متعدد المراحل، والتطوير المحلي.
sidebar:
  order: 5
---

## التطوير المحلي

يشغل مختبر Aspire ‏Hybird المنصة؛ ويعمل موقع Astro وMudTail بأداتيهما. وتتواصل الأجزاء عبر HTTP.

| الجزء | الأمر | العنوان |
| --- | --- | --- |
| مختبر المنصة | `dotnet run --project src/Aspire/Hybird --launch-profile http` | رابط لوحة التحكم في الطرفية |
| البوابة (مجموعات WebApi) | يشغلها المختبر | http://localhost:5080 |
| خدمة الهوية | يشغلها المختبر | http://localhost:5090 |
| عميل Blazor | يشغله المختبر (`ModularBlazor.BlzWebClient`) | http://localhost:5070 |
| موقع Astro | `npm run dev` في مشروع Astro | http://localhost:4321 |
| عرض MudTail التجريبي | `npx vite --port 3001` في `src/MudTail` | http://localhost:3001 |

في التطوير يكون موقع Astro والعميل على **أصلين مختلفين** (منفذين مختلفين)، فلا يتشاركان تخزين المتصفح. لاختبار
النسق المشترك وتسجيل الدخول المشترك، مرر العميل عبر خادم تطوير Astro:

```js title="astro.config.mjs"
export default defineConfig({
  vite: {
    server: {
      proxy: { '/app': { target: 'http://localhost:5070', changeOrigin: true } },
    },
  },
});
```

ويجب حينها بناء العميل مع `<base href="/app/">`، وأن تقبل خدمة الهوية
`http://localhost:4321/app/authentication/login-callback` عنوان إعادة توجيه (`Identity:BlazorClient:RedirectUris`)، وأن
يكون `http://localhost:4321` في `Cors:AllowedOrigins` للمجموعات ولخدمة الهوية.

## الإنتاج: موقع nginx واحد

عميل Blazor ملفات ثابتة (مخرجات `dotnet publish`، المجلد `wwwroot/`)، فيصلح له أي خادم ملفات ثابتة؛ ونستخدم nginx هنا.
ضع مخرجات Astro في الجذر والعميل تحت `/app/`. الوحدات ليست من ملفات الموقع: ينزلها العميل من WebApi
(`api/client-modules/files/…`) ويتحقق منها بقيمة SHA-256 في البيان.

```text
/usr/share/nginx/html/
  index.html, news/, sport/, _astro/, 404.html     Astro (dist/)
  app/
    index.html, _framework/, css/, js/              Blazor client (publish output, base href /app/)
  islands/                                          optional islands host
```

```nginx title="snippets/app.conf"
root /usr/share/nginx/html;
include /etc/nginx/mime.types;
types { application/wasm wasm; application/octet-stream dll; }

# The client's own settings (gateway and identity addresses): never stale.
location = /app/appsettings.json {
    add_header Cache-Control "no-cache";
}

# Blazor client: unknown paths under /app/ go to its index.html and the Router handles them.
location /app/ {
    try_files $uri $uri/ /app/index.html =404;
}

# Astro: real files only, with Astro's own 404 page.
location / {
    try_files $uri $uri/ $uri.html =404;
}
error_page 404 /404.html;

# Hashed Astro assets can be cached for a long time.
location /_astro/ {
    expires 1y;
    add_header Cache-Control "public, immutable";
}
```

:::caution
اجعل البديل `/app/` محصورا في `/app/`. فلو كان `try_files … /index.html` على مستوى الموقع كله، لأجاب عن كل صفحة
Astro مفقودة بواجهة Blazor بدلا من 404.
:::

## Docker

ابن موقع Astro وانشر العميل في مرحلتين، وانسخ المخرجين إلى صورة nginx واحدة:

```dockerfile
FROM node:22 AS astro
WORKDIR /site
COPY docs/astro-site/package*.json ./
RUN npm ci
COPY docs/astro-site/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src
COPY . .
RUN dotnet publish src/apps/blazor/ModularBlazor.BlzWebClient -c Release -o /app/publish

FROM nginx:alpine
COPY --from=astro /site/dist/ /usr/share/nginx/html/
COPY --from=publish /app/publish/wwwroot/ /usr/share/nginx/html/app/
```

استبدل `docs/astro-site` بمجلد مشروع Astro لديك. وإن كان الموقع يستورد MudTail، فانسخ `src/MudTail` إلى هذه
المرحلة وابنه أولا (`npm --prefix src/MudTail ci && npm --prefix src/MudTail run build`)، لأن اعتمادية `file:`
تشير إلى ذلك المجلد.

اكتب ملف `wwwroot/appsettings.json` الخاص بالإنتاج (قيمتا `Oidc:Authority` و`WebApi:BaseUrl` لتلك البيئة) في الصورة، أو
اربطه في `/usr/share/nginx/html/app/appsettings.json`: فتطبيق WebAssembly لا يقرأ بيئة الخادم.

## موقع التوثيق هذا

هذا الموقع نفسه مشروع Astro (Starlight) في `docs/astro-modular-blazor-docs/curved-chasm`:

```bash
npm install
npm run dev       # http://localhost:4321
npm run build     # static output in dist/
```

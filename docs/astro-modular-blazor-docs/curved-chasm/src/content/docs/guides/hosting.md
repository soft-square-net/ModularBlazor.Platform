---
title: Hosting and deployment
description: One nginx site for Astro at / and the Blazor client at /app/, a multi-stage Docker build, and local development.
sidebar:
  order: 5
---

## Local development

The Hybird Aspire lab starts the platform; the Astro site and MudTail run with their own tools. They talk over HTTP.

| Part | Command | URL |
| --- | --- | --- |
| Platform lab | `dotnet run --project src/Aspire/Hybird --launch-profile http` | dashboard link in the console |
| Gateway (WebApi pools) | started by the lab | http://localhost:5080 |
| Identity service | started by the lab | http://localhost:5090 |
| Blazor client | started by the lab (`ModularBlazor.BlzWebClient`) | http://localhost:5070 |
| Astro site | `npm run dev` in the Astro project | http://localhost:4321 |
| MudTail demo | `npx vite --port 3001` in `src/MudTail` | http://localhost:3001 |

In development the Astro site and the client are on **different origins** (ports), so they do not share
browser storage. To test the shared theme and sign-in, proxy the client through Astro's dev server:

```js title="astro.config.mjs"
export default defineConfig({
  vite: {
    server: {
      proxy: { '/app': { target: 'http://localhost:5070', changeOrigin: true } },
    },
  },
});
```

The client must then be built with `<base href="/app/">`, the identity service must accept
`http://localhost:4321/app/authentication/login-callback` as a redirect URI (`Identity:BlazorClient:RedirectUris`), and
`http://localhost:4321` must be in `Cors:AllowedOrigins` of the pools and the identity service.

## Production: one nginx site

The Blazor client is static files (`dotnet publish` output, `wwwroot/`), so any static server works; nginx is used
here. Put the Astro output at the root and the client under `/app/`. Modules are not files of the site: the client
downloads them from the WebApi (`api/client-modules/files/…`), checked against the SHA-256 in the manifest.

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
Keep the `/app/` fallback scoped to `/app/`. A site-wide `try_files … /index.html` would answer every missing
Astro page with the Blazor shell instead of a 404.
:::

## Docker

Build the Astro site and publish the client in two stages, and copy both outputs into one nginx image:

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

Replace `docs/astro-site` with the folder of your Astro project. If the site imports MudTail, copy
`src/MudTail` into the stage and build it first (`npm --prefix src/MudTail ci && npm --prefix src/MudTail run build`),
because its `file:` dependency points at that folder.

Write the production `wwwroot/appsettings.json` (the `Oidc:Authority` and `WebApi:BaseUrl` of that environment) into
the image, or mount it at `/usr/share/nginx/html/app/appsettings.json`: a WebAssembly app reads no server-side
environment.

## This documentation site

This site is itself an Astro (Starlight) project in `docs/astro-modular-blazor-docs/curved-chasm`:

```bash
npm install
npm run dev       # http://localhost:4321
npm run build     # static output in dist/
```

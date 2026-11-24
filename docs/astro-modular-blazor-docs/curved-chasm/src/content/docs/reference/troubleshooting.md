---
title: Troubleshooting
description: Common problems when Astro pages and the Blazor client share a site, and how to fix them.
sidebar:
  order: 2
---

## A missing page shows the Blazor "Loading core modules…" screen

The SPA fallback is site-wide. Scope it to `/app/` (see [Hosting](/guides/hosting/#production-one-nginx-site)).

## The client loads, but modules fail to load

- `<base href>` in the client's `index.html` must be `/app/` when it is served from `/app/`.
- Modules come from the WebApi (`api/client-modules/files/…`), not from the site: check `WebApi:BaseUrl` in the
  client's `appsettings.json` and the browser's network tab. A SHA-256 mismatch means a stale cache in front of the
  gateway.

## Sign-in fails with "invalid redirect_uri"

The identity service accepts only the redirect URIs listed for `blazor-client`
(`Identity:BlazorClient:RedirectUris`). Add `<site>/app/authentication/login-callback` and the matching
`logout-callback` when the client moves under `/app/`.

## "Could not find any element matching selector '#app'"

The **client** was started on an Astro page. Islands need the separate islands host, which registers components
with `RegisterForJavaScript` and does not add `App` to `#app`.

## An island stays on its fallback

- Check the console for 404s under `/islands/_framework/`: the islands host is not published there, or
  `loadBootResource` points elsewhere.
- The identifier in `<BlazorIsland component="…">` must match `RegisterForJavaScript(identifier: "…")` exactly.
- `params` must match the component's `[Parameter]` names and types.

## CORS errors from the browser

Add the page's exact origin (scheme, host and port) to `Cors:AllowedOrigins` of the WebApi pools and of the identity
service (in the lab, `Cors__AllowedOrigins__<n>` in `src/Aspire/Hybird/AppHost.cs`).

## Dark mode differs between the site and the app

They are on different origins (different ports in development), so they do not share `localStorage`. Serve both
from one origin, or use the dev proxy from [Hosting](/guides/hosting/#local-development).

## Behaviours stop working after navigation

With Astro's `<ClientRouter />`, re-run `autoInit` on `astro:page-load` and dispose it on `astro:before-swap`
([MudTail in Astro](/guides/mudtail-in-astro/#with-view-transitions)).

## Arabic text looks broken or spaced out

Set `lang="ar"` together with `dir="rtl"` (or call `setLocale('ar')`). MudTail then picks fonts with Arabic glyphs
and resets letter-spacing, which would break the joins between letters.

## An uploaded module's page opens, but its API returns 404

The client part loads (the page and its menu show), but every call through the gateway fails, for every user and
tenant:

```text
GET http://localhost:5080/api/dynamicfields/catalog/schema-org  404 (Not Found)
```

The server part is installed, but no replica of the pool the gateway routes to has loaded it. Check, in order:

1. **The pool does not apply the module.** A pool loads a module only if its `Modules:Enabled` list includes it (or
   is empty) and its `Modules:Excluded` list does not. In the lab the gateway sends every prefix that is not
   extracted to the core pool, which takes every module except the extracted ones. Older labs gave the core pool a
   fixed list (`identity, tenancy, client-modules, inventory`), so any other uploaded module stayed unloaded: update
   the platform checkout and restart the lab. In a deployment, align the pool's `Modules:Enabled` /
   `Modules:Excluded` with the gateway's route. `GET /pools/core/api/admin/modules` shows `enabledInPool: false`
   for such a module, and `GET /pools/core/api/pool` shows the pool's lists.
2. **The module is not `Active`.** Activate it on the Host Modules page (an upload is installed inactive).
3. **The module failed to load.** The replica's log names the error. A common one is a module built against a newer
   platform than the running host, e.g. a module that uses `ILookupClient` (`WebApi.Contracts.Lookups`) on a host
   without lookups: run the lab from a checkout of the platform version the module was built for.

---
title: API, sign-in and tenants
description: Calling the WebApi through its gateway from Astro at build time and in the browser, sharing the Blazor client's OpenID Connect sign-in, CORS and the X-Tenant header.
sidebar:
  order: 4
---

The platform's API is the WebApi behind its YARP gateway (`http://localhost:5080` in the lab). Users sign in at the
OpenIddict identity service (`http://localhost:5090`), and the Blazor client (`ModularBlazor.BlzWebClient`,
`http://localhost:5070`) calls the gateway with their access token. An Astro site talks to the same gateway.

## Build time: public data only

Astro pages can `fetch` in their frontmatter. That code runs **when the site is built** (or per request with an
SSR adapter), never in the visitor's browser. Use it only for anonymous endpoints:

```astro title="src/pages/modules.astro"
---
const api = import.meta.env.WEBAPI_BASE_URL ?? 'http://localhost:5080';
// Add an X-Tenant header to get one tenant's manifest.
const manifest = await fetch(`${api}/api/client-modules/manifest`).then(r => r.json());
---
<ul>{manifest.modules.map((m: { id: string; name: string; version: string }) => <li>{m.name} {m.version}</li>)}</ul>
```

`GET /api/client-modules/manifest` answers anonymous callers with the modules everyone may load (the layouts). Module
endpoints (`/api/content/...`, `/api/tenancy/...`, `/api/admin/...`) need a token, and a token must never be baked into
static HTML. The manifest is `{ etag, modules: [{ id, name, version, kind, url, sha256, … }] }`; every endpoint is in
the OpenAPI documents (`/openapi/webapi.json`, `/openapi/modules/<id>.json`, Scalar at `/scalar`).

## In the browser: CORS

The pools and the identity service answer cross-origin requests only from the origins in `Cors:AllowedOrigins` (exact
match, no wildcards, no credentials: the token travels in a header). Add the Astro origins for development and
production:

```json title="src/WebApi/WebApi/appsettings.Development.json"
{
  "Cors": {
    "AllowedOrigins": [ "http://localhost:5070", "http://localhost:4321" ]
  }
}
```

In the Hybird lab, every pool replica gets `Cors__AllowedOrigins__0` for the Blazor client in `AddPool`
(`src/Aspire/Hybird/AppHost.cs`); add a `Cors__AllowedOrigins__1` next to it for the Astro dev server. The gateway
passes the headers through.

## Sharing the sign-in

The Blazor client signs in with OpenID Connect (Authorization Code with PKCE, `AddOidcAuthentication`). Blazor's
authentication library keeps the signed-in user, with its access token, in **`sessionStorage`** under
`oidc.user:<authority>:<client id>`; in the lab `oidc.user:http://localhost:5090/:blazor-client`. When Astro and the
client are served from the **same origin** and the user signed in **in the same tab**, an Astro script can read it:

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

- Only read the user. Renewing and signing out belong to the Blazor client, so there is one owner of the session.
- `sessionStorage` is per tab: a page opened in a new tab sees no user until the client runs there once (it then
  renews silently while the identity service's session cookie lasts).
- Client-side checks are UX. The WebApi enforces permissions (`RequirePermission`) on every request.
- Any script on the origin can read the token. Keep third-party scripts off pages that read it, and set a Content
  Security Policy.

## Tenants

The WebApi resolves the tenant of every request from the `X-Tenant` header, then the host name; with a signed-in user
the token's `tenant` claim is the authority:

| Request | Result |
| --- | --- |
| Signed in, no `X-Tenant`, no tenant host | The token's tenant |
| Signed in, `X-Tenant` equal to the token's tenant | That tenant |
| Signed in, `X-Tenant` naming another tenant | 403 |
| `X-Tenant` and a tenant host name that disagree | 400 |
| Unknown tenant / suspended tenant | 404 / 403 (on a `TenantOptional` endpoint an unknown tenant runs as a foreign caller, without a tenant: see [Foreign tenants](/backend/tenancy-data/#foreign-tenants)) |

So a signed-in call needs no header. An anonymous call on a site that is per tenant (one hostname per tenant, for
example) sends it:

```ts
fetch(url, { headers: { 'X-Tenant': tenantId } });
```

A token for another tenant is a new sign-in: the client passes `tenant=<id>` on the authorization request.

## Linking into the app

Plain links work when the client is published under `/app/` (see [Hosting](/guides/hosting/)):

| Link from Astro | Opens |
| --- | --- |
| `/app/` | the client home (the layout is a startup module) |
| `/app/login` | redirect to the identity service's sign-in page, then back |
| `/app/dashboard` | a lazy client module (here the Dashboard sample, once installed), downloaded on navigation |

# WebApi.Identity.Server

The identity service of the WebApi platform: **OpenIddict** (authorization server) and **ASP.NET Core Identity**
(accounts). It runs as its own process with its own database, never as a runtime module. It implements step 1 of the
auth design (`docs/.../backend/auth-design.md`).

It owns global accounts, tenant memberships, the tenant security policy and token issuing. It doesn't own
permissions or grants: the WebApi host looks those up on the server.

## Endpoints and values for resource servers

| What | Value in the Hybird lab |
| --- | --- |
| Issuer (`iss`) | `http://localhost:5090/` (config `Identity:Issuer`; the trailing slash is part of it) |
| Audience (`aud`) | `modular-webapi` |
| Discovery | `http://localhost:5090/.well-known/openid-configuration` |
| JWKS | `http://localhost:5090/.well-known/jwks` |
| Authorize / token | `/connect/authorize`, `/connect/token` |
| Userinfo / end session / revoke | `/connect/userinfo`, `/connect/endsession`, `/connect/revoke` |

Access tokens are signed JWTs (RS256, `typ: at+jwt`) and are **not encrypted**. A resource server validates them with
`JwtBearer`, with `Authority = http://localhost:5090/`, `Audience = modular-webapi`, and `RequireHttpsMetadata = false`
in the lab. It maps the claims below without the inbound claim-type mapping (`MapInboundClaims = false`). Access
tokens last 10 minutes (`Identity:AccessTokenLifetime`). Refresh tokens last 14 days and rotate on every use.

## Clients

| Client | Type | Flows | Scopes |
| --- | --- | --- | --- |
| `blazor-client` | public | Authorization Code + **PKCE (required)**, refresh token | `openid profile email offline_access api` |
| `elsa-workflows` | confidential | client credentials | `api` |
| `webapi-identity-admin` | confidential | client credentials | `identity.admin` (alone) |

The seeder registers the clients (create or update) at every start. The `blazor-client` redirect URIs are in
`appsettings.json` (`Identity:BlazorClient`), for the Blazor client on `https://localhost:5001`. The
`elsa-workflows` secret is in `appsettings.Development.json`. Outside Development it must come from a secret store,
and the client isn't registered while the secret is missing.

Asking for the `api` scope (or any scope) gives a token whose audience is `modular-webapi`. The one exception is
`identity.admin`: it must be asked for alone, and its token's audience is `modular-identity`, so the WebApi never
accepts it and this service accepts it only on the service admin API (below). The `webapi-identity-admin` secret is in
`appsettings.Development.json` (`Identity:AdminClient`); elsewhere a secret store, and the client is not registered
while it is missing.

These are the **built-in** clients (property `managed_by` = `config`). Extra clients can be created in the admin UI
(`managed_by` = `ui`, see [Platform admin API](#platform-admin-api)); the seeder never touches those.

### Rotating a built-in client's secret

`Auth/RotatingApplicationManager.cs` replaces OpenIddict's application manager so that a confidential client can have
a second, **next** secret (stored hashed in the application's properties) during a rotation:

1. The admin UI starts a rotation: a new secret is made and shown once. Both secrets are accepted.
2. The admin puts the new secret in the secret store (for `elsa-workflows`: `Identity:ElsaClient:ClientSecret` of this
   service and of the WebApi) and restarts the client. The first token issued with the new secret records
   `next_secret_used`.
3. The admin completes the rotation (allowed only once the new secret was used): it becomes the secret and the old
   one stops working. Cancelling drops the new one instead.

The seeder agrees with a rotation at start: a configured secret that is the current one keeps its hash; one that is
the next one is promoted (the rotation completes); a configured secret older than a completed rotation
(`secret_rotated`) is ignored with a warning (update the secret store); any other configured secret replaces the
stored one, as before.

## Claims

User tokens carry only these claims:

| Claim | Meaning |
| --- | --- |
| `sub` | Global user id (the same in every tenant) |
| `tenant` | The one tenant this token is for. It's missing only in a *platform token* (see below) |
| `role` | **Platform roles only** (`platform-admin`). Tenant roles stay memberships |
| `name` | The tenant profile's display name if there is one, else the account's |
| `email` | The account email |
| `amr` | How the user signed in, always a JSON array (`["pwd"]`, or with `"mfa"`) |

They never carry permissions, presses, categories or shifts. OpenIddict adds its own technical claims (`client_id`,
`scope`, `jti`, `oi_*`); ignore them.

Client-credentials tokens have `sub` = `client_id` = `elsa-workflows`, a `name`, and no `tenant`. The service
sends `X-Tenant` on every call and the host checks it.

## Flows

### Sign in for a tenant (Code + PKCE)

```
GET /connect/authorize?client_id=blazor-client&response_type=code&redirect_uri=…&scope=openid profile email offline_access api
    &code_challenge=…&code_challenge_method=S256&state=…&tenant=acme
```

1. If there's no identity-service session (cookie `modular.identity`), the user goes to `/Account/Login` and back.
2. The tenant comes from the **`tenant` parameter**, else from a **`login_hint` of the form `tenant:acme`**.
   - If neither is given, a single active membership picks itself.
   - With several, the user goes to the **tenant picker** `/Account/SelectTenant`.
   - With none, only a user with a platform role gets a token, a *platform token* without `tenant`.
3. Issuing checks:
   - The account may sign in and isn't locked out.
   - The membership exists and is **Active**.
   - The tenant's security policy holds (see below).

   A failed check is an OAuth error at the redirect URI, e.g. `error=access_denied&error_description=The account is
   not a member of tenant 'initech'.`
4. Each sign-in creates an OpenIddict authorization bound to that tenant. The code and refresh tokens belong to it.

### Refresh

`grant_type=refresh_token` checks everything again: account, lockout, membership status and tenant policy. If any
check fails, the result is `invalid_grant` with a description.

### Switching tenant

Run the authorize request again with the other tenant and `prompt=none`, e.g. `&tenant=globex&prompt=none`. The
identity-service session is still there, so the user doesn't enter a password again, and the app gets a new code and
token pair for `globex`. Refresh tokens stay bound to their tenant.

### Sign out

`/connect/endsession`, with `post_logout_redirect_uri` set to a registered URI, ends the identity-service session and
redirects. `/Account/Logout` does the same for someone using the service directly.

## Tenant security policy

The identity service keeps its own `identity.Tenants` row per tenant: a display name for the picker and the policy.
The ids must match the WebApi tenant directory (`acme`, `globex`).

- `RequireMfa`: a token for this tenant is only issued when the account has MFA enabled **and** this sign-in used it
  (`amr` contains `mfa`). Otherwise issuing (and refreshing) is refused, with
  `error=access_denied` and `error_description=mfa_required: Tenant 'globex' requires multi-factor authentication …`.
- This is the hook and the flag only. MFA enrolment and the second-factor login step aren't built yet (TODO in
  `Pages/Account/Login.cshtml.cs`).
- A platform admin sets it with `PUT /api/admin/tenants/{tenant}` and the body `{"displayName":"…","requireMfa":true}`.

## Membership admin API

Call it with a bearer token from this service (audience `modular-webapi`). Allowed callers:
- the **`platform-admin`** role, for any tenant;
- an **active `tenant-admin` membership**, with a token issued for that same tenant.

A tenant admin sees only that tenant's members, never the user's other tenants.

| Method and path | What it does |
| --- | --- |
| `GET /api/admin/tenants/{tenant}/members` | Lists the members (status, role, invited by, dates) |
| `POST /api/admin/tenants/{tenant}/members` `{"email","role?","displayName?"}` | Invites a member (status `Invited`). An unknown email gets a new account without a password. The user accepts on `/Account/Invitations` |
| `POST …/members/{userId}/suspend` | Sets `Suspended` and **revokes that tenant's refresh tokens and codes** |
| `POST …/members/{userId}/activate` | Sets a suspended member back to `Active` (an invited user has to accept) |
| `DELETE …/members/{userId}` | Removes the membership and the tenant profile, and **revokes that tenant's refresh tokens and codes** |
| `PUT /api/admin/tenants/{tenant}` | Sets the tenant display name and `RequireMfa` (platform admin only) |

Revocation works through the tenant-bound authorizations: OpenIddict refuses every refresh token of a revoked
authorization. Access tokens already issued stay valid until they expire (at most 10 minutes). The WebApi host's
per-request membership check closes that gap.

TODO: an invitation email with a set-password link for new accounts.

## Service admin API

The same membership operations for a **service**, not a user: the WebApi's `identity-admin` runtime module
(repository Module.Admin.OpenIddict), which checks the caller's module permissions and tenant first and then calls
here. Callers need a client-credentials token of `webapi-identity-admin` with the scope `identity.admin` (audience
`modular-identity`); policy `identity-service`. The acting user's id goes in `X-Acting-User` (recorded as the inviter).
Errors are `{"error","message"}` problems with the usual status codes (404 not a member, 409 already one).

| Method and path | What it does |
| --- | --- |
| `GET /api/service/tenants/{tenant}/members?search=` | Members with their account facts (email confirmed, locked out, two-factor, password set) |
| `GET …/members/{userId}` | One member |
| `POST …/members` `{"email","role","displayName?","jobTitle?"}` | Invites; creates the account if needed, and the tenant's settings row if the tenant has none yet |
| `PUT …/members/{userId}` `{"role?","displayName?","jobTitle?"}` | Changes the role and the tenant profile |
| `POST …/members/{userId}/suspend` / `activate` | As above; suspending revokes the tenant's authorizations |
| `DELETE …/members/{userId}` | Removes the membership and profile, revokes the tenant's authorizations |

Both APIs share one implementation (`Admin/Memberships.cs`).

### Sessions

A session is one sign-in of a member in a tenant: an ad-hoc authorization tagged with the tenant, plus the tokens
issued under it (`Admin/Sessions.cs`).

| Method and path | What it does |
| --- | --- |
| `GET /api/service/tenants/{tenant}/sessions?userId=&includeEnded=` | The members' sessions. Each has its client, its times and how many live tokens it has. A session is active while an access or refresh token of it is valid |
| `POST …/sessions/{sessionId}/revoke` | Revokes that authorization and its tokens |
| `POST …/members/{userId}/sessions/revoke` | Revokes all of the member's sessions in the tenant |

Revoking a session alone would not stop the browser from coming back. While its login cookie is valid, the client
silently gets a new authorization (`prompt=none`). So revoking also records the ended login (`Auth/SessionEnds.cs`):

- **One login.** Each login cookie carries a `login_at` claim. It does not change on sliding renewal and is kept
  through the security stamp refresh. The claim is copied onto every authorization the login creates.
- **All logins.** A "not before" time for the tenant.

The record is kept per user and tenant in `AspNetUserTokens` (provider `tenant-sessions`). `/connect/authorize`
checks it once it knows the tenant. A matching login gets `login_required` (with `prompt=none`) or the login page. A
new login gets a new `login_at`, so it passes. Other tenants are not affected. A platform login (no tenant) is
recorded under the key `*`.

### Platform admin API

What only platform admins manage; the module checks `platform.tenants.manage` first. No tenant in the path
(`Admin/PlatformAdmin.cs`).

| Method and path | What it does |
| --- | --- |
| `GET /api/service/platform/tenants` | Every tenant known here: name, `requireMfa`, members by status |
| `PUT …/tenants/{tenant}` `{"displayName?","requireMfa?"}` | Sets the policy (creates the tenant's row if needed); applies at the next token |
| `GET …/accounts?search=` | Up to 100 accounts with platform roles and memberships |
| `GET …/accounts/{userId}` | One account |
| `POST …/accounts/{userId}/lock` / `unlock` | Lockout without end, then every session ends (all tenants and `*`) / clears it |
| `POST …/accounts/{userId}/sign-out` | Revokes every authorization and token, ends all logins, renews the security stamp |
| `PUT …/accounts/{userId}/roles` `{"platformAdmin"}` | Grants or takes the `platform-admin` role, then renews the sessions |
| `GET …/applications`, `GET …/applications/{clientId}` | Clients with grant types, scopes, URIs, secret and rotation state, live tokens |
| `POST …/applications` | Creates a UI-managed client (secret returned once for a confidential one) |
| `PUT` / `DELETE …/applications/{clientId}` | Changes or deletes a UI-managed client (409 `built_in` for configured ones) |
| `POST …/applications/{clientId}/secret` | UI client: a new secret now. Built-in client: starts a rotation |
| `POST …/applications/{clientId}/rotation/complete` / `cancel` | Completes (409 until the new secret was used) or cancels a rotation |
| `POST …/applications/{clientId}/tokens/revoke` | Revokes every authorization and token of the client |
| `GET …/scopes` | The standard scopes and the stored ones |

A UI-managed client may use `authorization_code` (PKCE always required, at least one absolute redirect URI),
`refresh_token` (with the code grant) and `client_credentials` (confidential only). Its scopes must exist;
`identity.admin` is never allowed. `openid` needs no permission, so it is listed for every code-flow client.

## Data

- PostgreSQL database `identity` (Aspire resource `identity-db` on the `tenant-data` server, passed as `ConnectionStrings:identity`), schema `identity`, with
  EF Core migrations in `Data/Migrations`. They're applied at startup, and EF Core's migration lock keeps replicas
  safe.
- Tables:
  - the ASP.NET Core Identity tables;
  - the OpenIddict tables (applications, authorizations, scopes, tokens);
  - `TenantMemberships` (UserId, TenantId, Status, Role, InvitedBy, InvitedAt, JoinedAt);
  - `TenantProfiles` (UserId, TenantId, DisplayName, JobTitle);
  - `Tenants` (TenantId, DisplayName, RequireMfa).
- **Npgsql only.** `WebApi.Data`'s engine abstraction is built for per-tenant module databases: it references
  `WebApi.Contracts`, all four EF providers and per-engine migration assemblies. This service has one database of its
  own, so it uses `UseNpgsql` directly. Supporting another engine means one migration set per engine, like the
  modules have.

To add a migration:

```bash
dotnet ef migrations add <Name> -o Data/Migrations --project src/WebApi/Identity/WebApi.Identity.Server
```

## Dev accounts (Development only)

They're seeded from `appsettings.Development.json` (`Identity:Seed`); the passwords are in that file.

| Account | Platform role | Memberships |
| --- | --- | --- |
| `admin@modular.local` | `platform-admin` | none (gets a platform token) |
| `editor@modular.local` | none | `acme` (member), `globex` (member) |
| `acme-admin@modular.local` | none | `acme` (`tenant-admin`) |

Seeded tenants: `acme` (Acme Corporation) and `globex` (Globex Corporation), both with `RequireMfa = false`.

## Running

- **In the Hybird lab:** `dotnet run --project src/Aspire/Hybird --launch-profile http`. The `identity` resource
  listens on `http://localhost:5090` and gets `ConnectionStrings:identity` and `Identity:Issuer` from the AppHost.
- **Standalone:** set `ConnectionStrings:identity` to a Postgres server (the Development default is
  `localhost:5432`), then run with `ASPNETCORE_URLS=http://localhost:5090`.

## Signing keys

- **Development:** `AddDevelopmentSigningCertificate()` and `AddDevelopmentEncryptionCertificate()`. These are
  self-signed certificates in the current user's certificate store, created on first run and valid for a year.
  They're fine for one machine and one instance.
- **Production: TODO.** Today `Program.cs` loads PFX files from `Identity:SigningCertificate:Path/Password` and
  `Identity:EncryptionCertificate:Path/Password`, and refuses to start without them. Before going live:
  - keep the keys in a key vault (or the database, encrypted), never in the image;
  - rotate them: publish the next signing key in JWKS ahead of use (OpenIddict accepts several signing credentials;
    the first one signs), keep the old public key until the longest token has expired, then remove it;
  - each dedicated deployment gets its own keys, and resource servers only ever see the public keys through JWKS;
  - put the ASP.NET Core Data Protection keys (login cookie) in Redis before running more than one replica, as the
    design says.
- TLS: the lab runs plain HTTP, so `DisableTransportSecurityRequirement()` is on in Development only. Production
  terminates TLS and keeps the requirement.

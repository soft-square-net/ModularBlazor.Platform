---
title: Upgrading a module to preview.8
description: What changes for a runtime module on ModularBlazor 1.0.0-preview.8 - foreign tenants on tenant-optional endpoints and live topics with several permissions.
sidebar:
  order: 19
---

Version `1.0.0-preview.8` adds two things. A module that uses neither only bumps its packages: no migration, no code
change. It ships the same six packages as preview.7.

## 1. Bump the packages (every module)

In `global.json`, set `soft.square.net.ModularBlazor.Module.Sdk` to `1.0.0-preview.8`, and set
`ModularBlazorPlatformVersion` to `1.0.0-preview.8` where a project reads it. Build and run the tests.

A module built against preview.7 still loads on a preview.8 host.

## 2. Foreign tenants on `.TenantOptional()` endpoints

On a preview.8 host, a `.TenantOptional()` endpoint is reached by callers whose token (or `X-Tenant` header) names a
tenant this deployment does not know. Before, they got 403 or 404 before your code ran. Now the request runs without a
tenant and `ITenantContext.ForeignTenantId` names that tenant. See
[Tenancy: foreign tenants](/backend/tenancy-data/#foreign-tenants).

- **Check every tenant-optional endpoint.** Where it treats `Tenant is null` as "a platform-level caller", decide what a
  foreign caller may do and check `ITenantContext.IsForeign`. `IAccessScopes` gives a foreign caller no grants and no
  platform-role permissions, so permission checks already refuse it.
- **A central service** (Organization `Central`) authorizes foreign callers from its own data, with the token's `sub`
  and `ForeignTenantId`, trusting the id only when `ResolvedBy == "claim"`. Stub tenants added on the central
  deployment as a workaround can go.
- Endpoints that need a tenant, and suspended tenants, behave as before.

## 3. Live topics with several permissions

When one resource is visible through several permissions, declare its kind once with `LiveTopic.AnyOf`:

```csharp
LiveTopics = [LiveTopic.AnyOf("post", "content.author", "content.accept", "content.publish")]
```

Holding any one of them for the resource (its own scope or a scope your `ILiveScopeResolver` returns) is enough.
`new LiveTopic(kind, permission)` is unchanged. See [Live updates](/backend/live-updates/).

**Install now refuses** a manifest that declares the same kind twice, a kind that is not lowercase `a-z0-9-`, the kind
`user`, or a blank permission. Merge duplicate declarations into one `AnyOf`.

## Checklist

- [ ] Platform packages on `1.0.0-preview.8`; build and tests pass.
- [ ] Every `.TenantOptional()` endpoint handles `ITenantContext.IsForeign`.
- [ ] Each live topic kind declared once; several permissions through `LiveTopic.AnyOf`.
- [ ] The host runs preview.8 (needed for both features).

---
title: Identity admin
description: "Level-1 dataflow of Module.Admin.OpenIddict (identity-admin, local only): a facade over the identity service's service API for members, grants, sessions, accounts and client applications."
sidebar:
  order: 20
---

`identity-admin` (Module.Admin.OpenIddict, v1.0.0, **core pool**) exists only as local commits, with no remote yet.
It has no tables of its own:

- Tenant and platform admin calls go to the identity service's `/api/service/*`, with a client-credentials token
  (`IdentityService`).
- Grants go to the host's `IGrantStore`.

```mermaid
flowchart LR
  TAdmin(["Tenant admin"]):::ext
  PAdmin(["Platform admin"]):::ext

  subgraph IA["identity-admin"]
    direction TB
    I1("20.1 Members + sessions"):::custom
    I2("20.2 Access + grants"):::custom
    I3("20.3 Permissions,<br/>capabilities"):::custom
    I4("20.4 Platform admin"):::custom
  end

  IdP(["Identity service<br/>/api/service/*"]):::ext
  Grants("Grant store<br/>platform.access_grants"):::core

  TAdmin <-->|F1| I1
  TAdmin <-->|F2| I2
  TAdmin <-->|F3| I3
  PAdmin <-->|F4| I4
  I1 & I2 & I4 <-->|F5| IdP
  I2 <-->|F6| Grants

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
```

| Flow | Route (`/api/identity-admin`) | Payload |
|---|---|---|
| F1 | `members` (list, get, invite, `PUT`), `{userId}/suspend`, `activate`, `DELETE`, `sessions`, `sessions/{id}/revoke`, `members/{userId}/sessions/revoke` | `InviteMemberRequest`, `UpdateMemberRequest` → `Member`, `MemberInvited`, `MemberStatusChanged`, `MemberRemoved`, `MemberSession`, `SessionsRevoked` |
| F2 | `members/{userId}/access`, `POST grants`, `DELETE grants/{grantId}` | `NewGrantRequest` → `MemberAccess`, `MemberGrant` |
| F3 | `GET permissions`, `capabilities`, `platform/capabilities` | → `PermissionGroup[]`, `AdminCapabilities`, `PlatformCapabilities` |
| F4 | `platform/tenants`, `accounts` (+ `lock`, `unlock`, `sign-out`, `roles`), `applications` (+ `secret`, `rotation/complete`, `rotation/cancel`, `tokens/revoke`), `scopes` | `TenantPolicyRequest`, `AccountRolesRequest`, `ClientApplicationRequest` → `TenantPolicy`, `Account`, `AccountChanged`, `ClientApplication`, `ClientApplicationCreated`, `ClientSecret`, `ClientTokensRevoked`, `ScopeInfo` |
| F5 | `/api/service/tenants/{tenant}/members...`, `/api/service/platform/...` (client credentials) | `InviteMember`, `UpdateMember`, `TenantPolicyInput`, `AccountRolesInput`, `ApplicationInput` |
| F6 | `IGrantStore` | `AccessGrant` |

---
title: Identity (core module + identity service)
description: Level-1 dataflow of the identity core module (/api/identity) and the OpenIddict identity service, with tokens, memberships, grants and admin flows.
sidebar:
  order: 2
---

Identity has two parts:

- **`identity`**, a core module in every pool, served at `/api/identity`. It answers who the caller is, lists
  permissions and users, and manages grants.
- **The identity service** (`src/WebApi/Identity/WebApi.Identity.Server`), its own process. It is the OpenIddict
  server, with the accounts, tenant memberships and client applications.

```mermaid
flowchart LR
  Browser(["Browser"]):::ext
  TAdmin(["Tenant admin<br/>(Module.Admin)"]):::ext
  Svc(["Service clients:<br/>identity-admin, elsa,<br/>organization connector"]):::custom

  subgraph Module["identity core module"]
    direction TB
    M1("2.1 /me"):::core
    M2("2.2 /permissions, /users"):::core
    M4("2.3 /grants"):::core
  end

  subgraph Server["Identity service (OpenIddict)"]
    direction TB
    S1("2.4 /connect/authorize,<br/>/connect/token"):::core
    S2("2.5 /api/admin/tenants/{t}/members"):::core
    S3("2.6 /api/service/*"):::core
  end

  IdDb[("identity DB<br/>schema identity")]:::store
  TS[("platform.access_grants")]:::store
  MQ[("RabbitMQ")]:::store

  Browser <-->|F1| S1
  Svc <-->|F2| S1
  Browser <-->|F3| M1
  Browser <-->|F4| M2
  TAdmin <-->|F5| M4
  TAdmin <-->|F6| S2
  Svc <-->|F7| S3
  S1 & S2 & S3 <--> IdDb
  M4 <--> TS
  M4 -->|F8| MQ

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
```

| Flow | Route or channel | Payload |
|---|---|---|
| F1 | authorization code + PKCE, refresh | → access token (JWT: sub, tenant, roles), id token, refresh token |
| F2 | `client_credentials` (`elsa-workflows`, connector, identity-admin clients) | → service access token |
| F3 | `GET /api/identity/me` (anonymous allowed) | → `CurrentPrincipal(IsAuthenticated, UserId, Name, TenantId, AuthType, Permissions, ClaimItem[])` |
| F4 | `GET /api/identity/permissions`, `/users`, `/users/{userName}` | → `PermissionDefinition[]`, users |
| F5 | `GET/POST /api/identity/grants`, `DELETE grants/{id}` (`access.grants.manage`) | `NewGrant` → `AccessGrant` |
| F6 | `GET/POST members`, `{userId}/suspend`, `activate`, `DELETE`, `PUT /` | `InviteMember` (email, role, displayName) → members |
| F7 | `/api/service/tenants/{t}/members`, `sessions`, `/api/service/platform/tenants`, `accounts`, `applications`, `scopes` | `InviteMember`, `UpdateMember`, `TenantPolicyInput`, `AccountRolesInput`, `ApplicationInput` → members, sessions, accounts, applications |
| F8 | fanout `access.grants.changed` | a signal |

**Tables:**

- schema `identity`: `AspNetUsers` and the other ASP.NET Identity tables, `TenantMemberships`, `TenantProfiles`,
  `Tenants`, and the OpenIddict tables
- schema `platform`: `access_grants`

The identity module publishes no integration events.

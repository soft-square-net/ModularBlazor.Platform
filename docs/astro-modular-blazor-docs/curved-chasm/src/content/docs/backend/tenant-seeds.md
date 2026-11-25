---
title: Tenant seeds
description: How an outside system (the Organization service) describes the tenants a deployment should have, and how the host reconciles them into the tenant store with a placement policy and one leader.
sidebar:
  order: 16
---

A **tenant seed** describes a tenant a deployment should have: its id, name, hosts, requested database placement,
modules and admins. An outside system, the Organization service, owns the seeds. The host turns them into tenants.

```
Organization connector (a module) ─ITenantSeedSource─► TenantSeedReconciler (host, one leader)
        │                                                   │ placement policy → tenant store → NotifyChanged
        └── tenancy.seed-changed.v1 ──► every replica ──────┘ admins' grants → ReportAsync(status)
```

## Turning it on

- Set `Platform:DeploymentId` on every replica of the deployment. While it is unset, the reconciler does nothing.
- Load a module that registers an `ITenantSeedSource` in its own container. The host resolves it in a fresh scope on
  every cycle, and runs a cycle as soon as such a module is loaded.
- The host needs a tenant store (`ConnectionStrings:TenantStore`).

## The contract

```csharp
public sealed record TenantSeed(string Id, long Version, string DeploymentId, string TenantId, string Name,
    IReadOnlyList<string> Hosts, string DatabaseServer, string DatabaseName,
    IReadOnlyList<string> Modules, IReadOnlyList<string> Admins, JsonElement Extra = default);

public interface ITenantSeedSource
{
    Task<IReadOnlyList<TenantSeed>> ListAsync(string deploymentId, CancellationToken ct);
    Task ReportAsync(string seedId, long version, SeedStatus status, TenantPlacement? actualPlacement, string? reason, CancellationToken ct);
}
```

- **`Version` must grow with every change.** A version the tenant already has is a no-op.
- **`Admins`** are token subjects. They get a standing `access.grants.manage` grant on the scope `tenant`, under the
  grant source `seed:{seedId}`, replaced on every applied version.
- **`Extra`** is for the source (branding, a slice of the unit tree). The host ignores it.
- The statuses are `Requested`, `Provisioning`, `Provisioned`, `Adjusted`, `Rejected` and `Failed`.

## One cycle

The reconciler runs every `Tenancy:Seeds:Interval` (one minute), when a seed source is loaded, and when
`tenancy.seed-changed.v1` arrives. One replica leads each cycle: Redis (`ConnectionStrings:redis`) when there is one,
otherwise the tenant store's database lock. For each seed of this deployment that is new or newer than its tenant:

1. **Placement** (new tenants only): `ITenantPlacementPolicy` accepts the requested placement, adjusts it or rejects
   the seed. The default, `AcceptAsIsPlacementPolicy`, accepts it. Register another one in the host to replace it.
2. **The tenant** is created (database first) or its name and hosts updated. Every replica is told, so the modules'
   `ITenantInitializer`s run.
3. **The admins' grants** are written.
4. **The status is reported** to the source. Only then is the version recorded on the tenant, so a failed step or a
   failed report is retried on the next cycle.

A seed never moves an existing tenant's database. When the requested placement differs from the actual one, the host
reports `Adjusted` with the actual placement.

The host rejects a seed when the tenant id is not a slug, the tenant exists but was created by hand or by another
seed, a host belongs to another tenant, or the database server is unknown. A rejection is reported once per version.

## Reconciling now: `tenancy.seed-changed.v1`

A seed source publishes `TenantSeedNames.SeedChangedEvent` (`tenancy.seed-changed.v1`) through its outbox when a seed
of the deployment changes. The host receives it on **every** replica through a
[host subscription](/backend/integration-events/#host-subscriptions) and calls `TenantSeedReconciler.Trigger()`. Only the
leader runs the cycle. The event carries nothing the reconciler needs, so its tenant and data are ignored.

A missed event costs at most one interval: the timer picks up the change.

## Configuration

| Setting | Default | Meaning |
| --- | --- | --- |
| `Platform:DeploymentId` | none | This deployment's id. Required. |
| `Tenancy:Seeds:Interval` | `00:01:00` | How often the leader pulls the seeds. |
| `Tenancy:Seeds:LeaderLease` | `00:10:00` | How long one cycle may hold the Redis lock. |
| `Tenancy:Seeds:InitializationTimeout` | `00:05:00` | How long the reconciler waits for this replica's tenant initializers. |
| `Tenancy:Seeds:LockConnectionName` | `redis` | The Redis connection string name; the database lock without it. |

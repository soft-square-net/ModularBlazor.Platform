---
title: Integration events
description: Events between runtime modules through an outbox and an inbox in each module's schema, RabbitMQ or an in-process bus, and subscriptions the host reads from the manifest.
sidebar:
  order: 14
---

An **integration event** tells other modules that something happened in yours: `shifts.checked-in.v1`,
`content.work-item-assigned.v1`. It is plain JSON in an envelope. No module shares a type with another one, and the
host holds none of them, so every module stays unloadable.

```
module A: SaveChanges ─► outbox row (same transaction) ─► after commit ─► IBrokerPublisher
                                                                                  │
                         topic exchange "integration" (routing key = event type) ◄┘
                                │
           durable queue integration.{module}.{pool} ─► consumer (one per replica) ─► module B: inbox + handlers (one transaction)
```

## The guarantees

- **Nothing is sent when the change rolls back.** `Publish` only adds an outbox row to the module's own `DbContext`;
  the row is sent after that `SaveChanges` (or transaction) commits.
- **Nothing is lost when the process dies after the commit.** The outbox sweeper sends rows left unsent, for each
  active tenant.
- **A subscriber handles an event once.** Its inbox records the event id in the same transaction as the handler's
  changes. A second delivery is dropped.
- **Events wait for an unloaded subscriber.** Its durable queue is declared at install and kept while the module is
  unloaded. It is deleted at uninstall.
- **A failing event is retried, then dead-lettered.** Admins list, peek and replay dead letters through
  `/api/admin/integration/dead-letters` (permission `platform.modules.manage`).

## Envelope and type names

`IntegrationEnvelope(Id, Type, TenantId, Subject, OccurredAt, CorrelationId, Source, Data, OrgId?, UnitPath?)`.

- **Type:** lowercase words separated by dots, ending with a version: `{module}.{event}.v{n}`. Never change the shape
  of a published version; publish `v2` next to it instead.
- **Subject:** what the event is about, for example `post:42`.
- **UnitPath:** the unit ids from the root, separated by `/` (the same format as the [live topics](/backend/live-updates/)).
- **CorrelationId:** shared by an event and every event its handlers publish.

## Publishing from a module

1. Map the outbox and inbox in the module's `DbContext`. This is opt-in, so modules that do not use events keep
   their model unchanged:

   ```csharp
   public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ITenantContext tenant)
       : TenantDbContext(options, tenant)
   {
       protected override string Schema => "inventory";
       protected override bool UsesIntegrationEvents => true; // maps integration_outbox and integration_inbox
   }
   ```

2. Add one migration per engine (see [Upgrading a module to preview.7](/backend/upgrading-to-preview-7/)).
3. Register the services in the module's `ConfigureServices`:

   ```csharp
   services.AddTenantDbContext<InventoryDbContext>();
   services.AddIntegrationEvents<InventoryDbContext>();
   ```

4. Publish in the same unit of work as the change:

   ```csharp
   public sealed record StockReserved(Guid ItemId, int Quantity); // the module's own event record

   db.Reservations.Add(reservation);
   events.Publish("inventory.stock-reserved.v1", new StockReserved(item.Id, quantity), subject: $"item:{item.Id}");
   await db.SaveChangesAsync(ct); // the row commits with the reservation; it is sent after the commit
   ```

`Publish` needs a tenant scope: an event always belongs to the tenant it happened in.

## Subscribing

A subscriber declares the patterns twice: in the manifest, so the host binds its queue without loading the module,
and in its services, so the right handler runs.

```csharp
public ModuleManifest Manifest { get; } = new("reports", "Reports", "1.0.0", "reports")
{
    Subscriptions = ["inventory.stock-*.v1"]
};

public void ConfigureServices(IServiceCollection services, IConfiguration settings)
{
    services.AddTenantDbContext<ReportsDbContext>();
    services.AddIntegrationEvents<ReportsDbContext>();               // the inbox lives here too
    services.AddIntegrationHandler<StockHandler>("inventory.stock-*.v1");
}

public sealed record StockReservedCopy(Guid ItemId, int Quantity);   // the subscriber's own copy

public sealed class StockHandler(ReportsDbContext db) : IIntegrationEventHandler
{
    public Task HandleAsync(IntegrationEnvelope e, CancellationToken ct)
    {
        var data = e.DataAs<StockReservedCopy>();
        db.Movements.Add(new Movement(e.Id, data.ItemId, -data.Quantity));
        return Task.CompletedTask;                                    // no SaveChanges: the inbox saves it
    }
}
```

- **Patterns:** `*` inside a word matches any characters except a dot (`org.unit-*.v1`); a word that is exactly `#`
  matches zero or more words (`org.#`, `#.work-item-assigned.v1`).
- **Handlers run in a tenant scope** of the subscriber's own container, after its migrations for that tenant.
- **Do not call `SaveChanges`** in a handler: the platform saves the handler's changes with the inbox row. When the
  handler throws, both roll back and the event is retried.
- **Publishing from a handler is fine.** The new event keeps the correlation id.

## Host configuration

| Setting | Default | Meaning |
| --- | --- | --- |
| `ConnectionStrings:broker` | none | RabbitMQ. Without it, an in-process bus delivers to the modules loaded on the same process; nothing is durable. |
| `Integration:MaxAttempts` | `5` | Deliveries of one event to one module before it is dead-lettered. |
| `Integration:RetryDelay` | `00:00:05` | Wait before a failed event is delivered again. |
| `Integration:Prefetch` | `16` | Unacknowledged events a module's consumer holds at once on a replica. |

Per module, `AddIntegrationEvents<T>(o => ...)` sets `Source`, `SweepInterval`, `SweepDelay`, `Retention` (the
inbox's duplicate window, 7 days) and `BatchSize`.

## Host subscriptions

The host itself can subscribe, for signals that are not a module's business. The tenant seed reconciler uses it to
run a cycle as soon as `tenancy.seed-changed.v1` arrives (see [Tenant seeds](/backend/tenant-seeds/)).

```csharp
internal sealed class SeedChangedTrigger(TenantSeedReconciler reconciler) : IHostIntegrationHandler
{
    public Task HandleAsync(IntegrationEnvelope e, CancellationToken ct) { reconciler.Trigger(); return Task.CompletedTask; }
}

services.AddHostIntegrationHandler<SeedChangedTrigger>(TenantSeedNames.SeedChangedEvent);
```

A host subscription is not a module subscription:

| | Module subscription | Host subscription |
| --- | --- | --- |
| Receives | one replica of each pool that runs the module | **every** replica |
| Queue | durable `integration.{module}.{pool}` | exclusive, deleted with the replica |
| Scope | a tenant scope of the module's container | the host's root container, **no tenant** (the envelope's tenant may be empty) |
| Duplicates | the inbox table, in the handler's transaction | an in-memory list of recent event ids per replica |
| Failure | retried, then dead-lettered | retried in process, then logged and dropped |

So host handlers are for signals whose work is also done on a timer or at start. Durable work belongs in a module.

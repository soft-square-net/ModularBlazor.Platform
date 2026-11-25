---
title: Upgrading a module to preview.7
description: What changes for a runtime module when it moves to ModularBlazor 1.0.0-preview.7, and the one outbox migration a module needs only when it uses integration events.
sidebar:
  order: 18
---

Version `1.0.0-preview.7` adds [integration events](/backend/integration-events/),
[live updates](/backend/live-updates/), [tenant seeds](/backend/tenant-seeds/) and the
[work-item contract](/backend/work-items/). Everything is opt-in: a module that uses none of it only bumps its
package versions.

## 1. Bump the packages (every module)

The Module SDK pins every platform contract package (`WebApi.Contracts`, `WebApi.Data`, `ModularBlazor.Abstractions`,
...) to its own version, so one line moves them all. In `global.json`:

```json
{
  "msbuild-sdks": {
    "soft.square.net.ModularBlazor.Module.Sdk": "1.0.0-preview.7"
  }
}
```

Projects that reference the platform packages without the SDK (test projects in the module templates) read the
version from `ModularBlazorPlatformVersion`; set it to `1.0.0-preview.7` too.

Build and run the tests. **A module that does not use integration events needs no migration.** The outbox and
inbox tables are mapped only when a `DbContext` asks for them, so its model, and EF's pending-model-changes check, stay
as they were.

A module built against an earlier preview still loads on a preview.7 host. A module that uses the new features needs
a preview.7 host.

## 2. Integration events (only for modules with `UsesIntegrationEvents`)

Only a module that publishes or subscribes needs these steps.

1. Map the tables in the module's `DbContext`:

   ```csharp
   protected override bool UsesIntegrationEvents => true;
   ```

2. **Add the one outbox migration, once per database engine** the module ships migrations for. From the Platform repo:

   ```powershell
   ./src/WebApi/scripts/Add-Migration.ps1 -Project <path to the module's Infrastructure project> -Name IntegrationEvents
   ```

   The migration creates `integration_outbox` and `integration_inbox` in the module's own schema. It applies when the
   module is activated on a preview.7 host, for every active tenant.

3. Register the services, and the handlers if the module subscribes:

   ```csharp
   services.AddIntegrationEvents<MyModuleDbContext>();
   services.AddIntegrationHandler<SomethingHappenedHandler>("other.something-*.v1");
   ```

4. List the same patterns in the manifest, so the host binds the module's queue at install:

   ```csharp
   Subscriptions = ["other.something-*.v1"]
   ```

## 3. Live updates (optional)

- Declare the topic kinds and their permission in the manifest: `LiveTopics = [new LiveTopic("item", "mymodule.view")]`.
- Publish with `ILiveNotifier` after the change is committed.
- Optionally register an `ILiveScopeResolver` in the module's services, for the resource's other scopes.

Clients never send scopes: the hub's `Subscribe` takes only the topic.

## 4. Work items (optional)

A module that owns work items publishes `{module}.work-item-assigned.v1` and `{module}.work-item-state-changed.v1`
with `PublishWorkItemAssigned` and `PublishWorkItemStateChanged`. It needs step 2.

## 5. Tenant seeds (hosts only)

Seeds change nothing for modules, except a module that serves seeds (the Organization connector): it registers an
`ITenantSeedSource` in its services and publishes `tenancy.seed-changed.v1` when a seed changes. A host that uses
seeds sets `Platform:DeploymentId`.

## Checklist

- [ ] All platform packages on `1.0.0-preview.7`; build and tests pass.
- [ ] Uses integration events: `UsesIntegrationEvents => true`, the `IntegrationEvents` migration for every engine,
      `AddIntegrationEvents<T>()`, handlers and `Subscriptions`.
- [ ] Publishes live updates: `LiveTopics` in the manifest.
- [ ] The host it runs on is preview.7.

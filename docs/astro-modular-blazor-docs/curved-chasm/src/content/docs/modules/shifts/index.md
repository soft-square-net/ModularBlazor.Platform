---
title: Shifts
description: The shifts runtime module. It keeps the newsroom's sections and the rota (Manager, Desk, Editor), and turns a published shift into time-windowed Content grants.
---

> **Status: implemented** in the repository
> [`ModularBlazor.Module.Shifts`](https://github.com/soft-square-net/ModularBlazor.Module.Shifts) (branch `main`), in
> Clean Architecture layers, with the **Shifts Management** client module. Not packed as a bundle yet: build it and
> upload the entry project's output.

The **Shifts** module keeps the newsroom's **sections** (each covering categories of one or more presses), the
**shifts** planned for them and each shift's **rota** (Manager, Desk, Editor), and check-out. A published shift writes
`content.*` grants (accept, assign, review, publish, ...) for its rota on the section's categories, valid only while the
shift runs. They matter once the [Contents module](/modules/contents/) is installed next to it. The design behind it is
in [Authentication and authorization design](/backend/auth-design/).

The module's id is `shifts`. Its permissions are `shifts.view` and `shifts.manage`.

## Layout

It follows the [Clean Architecture layout](/backend/module-template-clean-architecture/):

| Project | Holds |
| --- | --- |
| `WebApi.Modules.Shifts.Core` | `SectionAggregate`, `ShiftAggregate`, the domain rules, specifications |
| `WebApi.Modules.Shifts.UseCases` | One folder per use case: a Mediator command or query and its handler, returning an Ardalis `Result` |
| `WebApi.Modules.Shifts.Infrastructure` | `ShiftsDbContext`, tenant migrations in `Data/Migrations/<Provider>` |
| `WebApi.Modules.Shifts.Web` | The entry assembly: `IRuntimeModule`, one REPR endpoint per route |
| `WebApi.Modules.Shifts.Client` | The NSwag API client (`ShiftsClient`), a hidden client module |
| `Module.Shifts` | **Shifts Management** (menu entry "Shifts"), built with MudTail |

The migrations live under `Infrastructure/Data/Migrations`, so pass the folder and namespace to the platform script:

```powershell
./src/WebApi/scripts/Add-Migration.ps1 -Project ../Module.Shifts/src/WebApi.Modules.Shifts.Infrastructure -Name <Name> `
    -MigrationsDir Data/Migrations -Namespace soft.square.net.WebApi.Modules.Shifts.Infrastructure.Data.Migrations
```

## The API

| Route (under `/api/shifts`) | Does |
| --- | --- |
| `GET`, `POST /sections`; `PUT /sections/{id}` | The sections: name, categories, timing overrides |
| `GET /sections/{id}/settings` | The timings in effect for a section |
| `GET`, `POST /shifts`; `GET`, `PUT`, `DELETE /shifts/{id}` | Plan, read, edit and delete shifts with their rota |
| `POST /shifts/{id}/publish` | Publish a shift: its rota gets the time-windowed grants |
| `POST /shifts/{id}/check-out`, `POST /shifts/{id}/members/{userId}/check-out` | Check out the shift, or one member |
| `GET /mine`, `GET /overdue` | My shifts; shifts not checked out in time |
| `GET /shifts/{id}/automation` | What the automation (Elsa) needs about a shift |

## The client

| Page | Does |
| --- | --- |
| `/shifts` | The schedule by section from a day on, my shifts and the overdue ones; "New shift" plans one with its rota |
| `/shifts/{id}` | One shift: rota, timings, publish, edit, check out the shift or one member, delete |
| `/shifts/sections` | The sections: add, edit, the timings in effect |

## Developing

The playground (`dotnet run --project playground --launch-profile dev`) runs the platform with the module and grants
`shifts.*` to `acme-admin@modular.local` and `shifts.view` to `editor@modular.local` through `Access:DevGrants`. The
`install` profile starts the platform without the module and writes `playground/out/shifts.zip` and the client dlls,
to try the real install path. See [Create a module, A to Z](/backend/create-a-module/).

---
title: Shifts
description: The Shifts runtime module (Shifts v2). It plans shifts in Organization units, keeps role definitions and templates as data, tracks attendance with a shift clock, and grants rights only between check-in and check-out.
---

> **Status: implemented** in the repository
> [`ModularBlazor.Module.Shifts`](https://github.com/soft-square-net/ModularBlazor.Module.Shifts) (branch `main`,
> Shifts v2 steps S1–S4, tag `v2.0.0-preview.1`), in Clean Architecture layers, with the **Shifts Management** client
> module in Arabic and English. The package `soft.square.net.Shifts.Contracts` 2.0.0-preview.1 carries the event
> records. The module **requires** the [Organization](/modules/organization/) module and runs on Platform preview.7
> or later.

**Shifts** plans the **shifts** of each Organization **unit**, their **rota** (who is on duty, in which role) and
their **attendance**. Rights come from the rota only while a member is checked in: a check-in writes time-windowed
grants on the unit's scopes, and a check-out (by hand or by the clock) ends them. The rights matter once
[Contents](/modules/contents/) or another module that checks them is installed next to it.

The module's id is `shifts`. Its permissions are `shifts.view` and `shifts.manage`.

:::note[What changed in Shifts v2]
Shifts used to keep its own newsroom **sections** (each covering press categories), a fixed role enum (Manager, Desk,
Editor) with role templates in code, and granted the rota when a shift was published. That model is gone:

- sections moved to **Organization units** (a Section unit, or a Department that serves every category);
- roles are **data** (`ShiftRoleDefinition`), resolved up the unit tree;
- grants start at **check-in**, not at publish.

Old sections stay readable and can be moved with `POST /sections/move-to-organization`. Section editing was removed in
S4.
:::

## Layout

It follows the [Clean Architecture layout](/backend/module-template-clean-architecture/):

| Project | Holds |
| --- | --- |
| `WebApi.Modules.Shifts.Core` | `ShiftAggregate`, role definitions, templates, the legacy `SectionAggregate`, rules, specifications |
| `WebApi.Modules.Shifts.UseCases` | One folder per use case (Mediator command or query, Ardalis `Result`), the unit cache, the shift clock |
| `WebApi.Modules.Shifts.Infrastructure` | `ShiftsDbContext`, tenant migrations for four engines in `Data/Migrations/<Provider>` |
| `WebApi.Modules.Shifts.Web` | The entry assembly: `IRuntimeModule`, one REPR endpoint per route |
| `Shifts.Contracts` | The published package: `ShiftsEvents`, the event records, snapshots, `AttendanceStatuses`, `ShiftsLive` |
| `WebApi.Modules.Shifts.Client` | The NSwag API client, a hidden client module |
| `Module.Shifts` | **Shifts Management**, built with MudTail |

## Units

- **Unit cache.** Shifts keeps a cache of the tenant's Organization units (`unit_cache`): type, path, names, tags,
  time zone, categories, version. It is filled by `org.unit-*.v1` and `org.membership-changed.v1` events, by a first
  fill when the tenant initializes (as the module's service client, `Shifts:Organization:*`), and by
  `POST /units/resync`. Older versions never overwrite newer ones; a removal is a tombstone.
- **A shift belongs to a unit** (`Shift.UnitId`). A shift may still name a legacy section; a unit-only shift takes the
  section mapped to its unit, if any. A shift is never moved to another unit (409).
- **Authority.** `shifts.manage` on `UnitScopes.For(path)`, so a manager of a Department plans its Sections' shifts.
  Shifts with no unit, and section moves, need tenant-wide `shifts.manage`.
- **Members** of a rota or template come from `MembersOfAsync` of the unit and every ancestor. A member needs an
  identity subject and a membership that covers the shift start. Organization unreachable gives **503**.

## Roles and templates

- **`ShiftRoleDefinition`** (`shift_roles`): key, names (en/ar), permissions (checked against the permission catalog),
  `IsLead`, and a unit (null means tenant-wide). For each key the nearest unit up the path wins. New tenants get
  `manager` (lead), `desk` and `editor`, seeded only when the tenant has no definitions.
- A rota row is `{ userId, roleKey }`, with an `IsLead` snapshot. A published shift has exactly one lead.
- Changing a definition's permissions rewrites the grants of affected shifts that have not ended. Changing `IsLead`
  while it is in use, or deleting it without an heir, is 409.
- **`ShiftTemplate`**: unit, name, start (wall clock), duration (at most 24 h), slots (role and count), default
  members. `POST /templates/{id}/clone { date }` returns a draft shift plus warnings. The start follows the unit's time
  zone (or the nearest ancestor's, else UTC with a warning); a start in a DST gap moves forward, an ambiguous one takes
  the first occurrence.

## Attendance and the shift clock

| Rule | Behaviour |
| --- | --- |
| Grants | Only from check-in: window `[CheckedInAt, CheckedOutAt ?? EndsAt + grace]`, on the unit's scopes. A check-out revokes at once |
| Check-in window | From start − early check-in until the end, on a published shift that has not ended; checking in again is allowed |
| Status | `expected`, `present`, `late`, `absent`, `left-early`, `overtime` (`AttendanceStatuses`) |
| Thresholds | Per unit, nearest up the path, else the module settings: early check-in 00:30, late after 00:10, absent after 01:00, overtime after 00:15, checkout grace 00:15 |
| The clock | Every `ClockInterval` (1 min), per tenant, under a database try-lock. It marks absences, checks out members still in at end + grace (`Automatic`), and ends the shift (`EndedBy` = `shifts:clock`) |
| Handover | The lead writes a note (`PUT /shifts/{id}/handover`) |

Shifts owns check-out now. Elsa no longer calls a check-out endpoint; it reads the ended list (see
[Elsa](/modules/elsa/)).

## Events and live

Events go through the platform [outbox](/backend/integration-events/), source `shifts`, subject `shift:{id}`, with the
unit path and org id on the envelope:

| Type | Sent when |
| --- | --- |
| `shifts.published.v1` | A shift is published (first time only) |
| `shifts.updated.v1`, `shifts.cancelled.v1` | A published shift changes or is deleted |
| `shifts.member-added.v1`, `shifts.member-removed.v1` | A rota row of a published shift changes |
| `shifts.checked-in.v1`, `shifts.checked-out.v1` | A member checks in; a member is checked out (by hand, with the shift, or by the clock) |
| `shifts.handover.v1` | The lead writes the handover note |
| `shifts.ended.v1` | A shift ends. Absences show in its snapshot |
| `shifts.section-moved.v1` | A legacy section was moved to an Organization unit (subject `section:{id}`) |

Live: topic `shifts:shift:{id}`, message `shift.changed { shiftId }`, declared as `LiveTopic("shift", "shifts.view")`
and resolved to the unit's scopes. The pages also follow the unit topic when Organization is installed.

## The API

| Route (under `/api/shifts`) | Does |
| --- | --- |
| `GET`, `POST /shifts`; `GET`, `PUT`, `DELETE /shifts/{id}` | Plan, read, edit and delete shifts (`unitId` or a legacy `sectionId`) |
| `POST /shifts/{id}/publish` | Publish a shift |
| `POST /shifts/{id}/check-in`, `.../members/{userId}/check-in` | Check in yourself, or a member (the lead or a manager) |
| `POST /shifts/{id}/check-out`, `.../members/{userId}/check-out` | End the shift, or check one member out |
| `PUT /shifts/{id}/handover` | The lead's handover note |
| `GET /shifts/ended?since=&take=` | Ended shifts, oldest first, for workflows (tenant-wide `shifts.manage`) |
| `GET /mine`, `GET /overdue` | My shifts; shifts past their end |
| `GET`, `POST /roles`; `PUT`, `DELETE /roles/{id}`; `GET /roles/effective?unitId=` | Role definitions |
| `GET`, `POST /templates`; `PUT`, `DELETE /templates/{id}`; `POST /templates/{id}/clone` | Templates |
| `GET`, `PUT /units/{id}/attendance` | A unit's attendance thresholds |
| `GET /units`, `POST /units/resync` | The unit cache |
| `GET /sections`, `GET /sections/export`, `POST /sections/move-to-organization` | Legacy sections: read, export, move |

## The client

| Page | Does |
| --- | --- |
| `/shifts` | The planner per unit (and "Shifts without a unit" for unmoved sections) |
| `/shifts/{id}` | One shift: rota, publish, check-in, check-out, handover, live refresh |
| `/shifts/mine` | My shifts |
| `/shifts/templates`, `/shifts/roles`, `/shifts/attendance` | Templates, role definitions, unit thresholds |
| `/shifts/sections`, `/shifts/sections/move` | Moved sections (read only); resync, export, links and move |

## Known limits

- Category scopes (`category:{c}`) are still written next to the unit scopes; only `section:{int}` was dropped, and
  only for moved sections.
- Without the Content module, a seeded role that holds `content.*` permissions cannot be saved ("Not in the permission
  catalog").
- Without Organization, new shifts can only be planned on existing unmoved sections, and sections can no longer be
  edited.

## Developing

The playground (`dotnet run --project playground --launch-profile dev`) runs the platform with the module and grants
`shifts.*` to `acme-admin@modular.local` through `Access:DevGrants`. The `install` profile writes the bundle to try the
real install path. See [Create a module, A to Z](/backend/create-a-module/).

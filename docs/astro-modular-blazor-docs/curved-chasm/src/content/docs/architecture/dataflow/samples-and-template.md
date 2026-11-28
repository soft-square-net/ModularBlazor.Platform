---
title: Samples and the module template
description: Level-1 dataflow of the sample modules (greeting, inventory, pulse, Dashboard, Reports) and of the ModuleTemplate Clean Architecture module (notes, events, live).
sidebar:
  order: 21
---

The Samples repository shows the runtime with small modules. ModuleTemplate is the `dotnet new` template that
every `Module.*` repository started from.

```mermaid
flowchart LR
  User(["User"]):::ext

  subgraph Samples["Samples"]
    direction TB
    G("greeting"):::custom
    I("inventory"):::custom
    P("pulse"):::custom
    D("Module.Dashboard"):::client
    R("Module.Reports"):::client
  end
  subgraph Template["ModuleTemplate (modulename)"]
    direction TB
    N1("notes endpoints"):::custom
    N2("NoteEditedLiveHandler"):::custom
  end

  IDb[("schema inventory<br/>stock_items")]:::store
  NDb[("schema modulename<br/>notes")]:::store
  MQ[("RabbitMQ")]:::store
  Live("Live hub"):::core

  User <-->|F1| G
  User <-->|F2| I
  User <-->|F3| P
  User <--> D & R
  User <-->|F4| N1
  I <--> IDb
  N1 <--> NDb
  N1 -->|F5| MQ
  MQ -->|F5| N2
  N2 -->|F6| Live

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef client fill:#173b35,stroke:#45c2a1,color:#e5fff5
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
```

| Flow | Route or channel | Payload |
|---|---|---|
| F1 | `/api/greeting`: `GET /`, `GET /{name}`, `POST /echo` (sales and worker pools) | a greeting text |
| F2 | `/api/inventory` (v1.2.0): `GET items`, `POST items`, `POST items/{sku}/adjust` | stock items |
| F3 | `/api/pulse`: `whoami`, `beats`, `echo/{word}`, `status` (`PulseWorker`, `StartupProbe`) | the beat state |
| F4 | `/api/modulename/notes`: list, search, get, create, update, delete | note DTOs |
| F5 | outbox, then topic `integration`, then the module's own queue | `modulename.note-edited.v1` (subscription `modulename.note-*.v1`) |
| F6 | `ILiveNotifier`, the note's topic | `LiveMessage` |

The template's own module subscribes to its own `modulename.note-*.v1` events, to show the whole path: outbox,
broker, inbox, handler and live hub.

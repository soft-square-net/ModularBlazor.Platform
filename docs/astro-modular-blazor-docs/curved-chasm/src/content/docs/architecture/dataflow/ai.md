---
title: Ai (and the planned Search)
description: "Level-1 dataflow of Module.Ai (A1 built, local only): tenant AI providers with write-only keys, SEO optimize jobs, the job runner, live streaming to the user, usage, and the planned Module.Search."
sidebar:
  order: 19
---

`ai` (Module.Ai, v0.1.0, **core pool**) exists only as local commits, with no remote yet. Step A1 is built:

- per-tenant providers: Ollama, OpenAI, Azure OpenAI or OpenAI-compatible, with write-only encrypted keys
- settings and usage metering
- the SEO optimize job

A job is queued, the `AiJobRunner` runs it, and the chunks reach the user through the live hub. The result is read
over HTTP. **Module.Search is planned** (S1, S2): OpenSearch with per-tenant indices, and Ai depends on it, never the
reverse.

```mermaid
flowchart LR
  Ed(["Editor in the studio"]):::ext
  TAdmin(["Tenant admin"]):::ext

  subgraph Ai["ai (/api/ai)"]
    direction TB
    A1("19.1 SEO optimize"):::custom
    A2("19.2 Jobs"):::custom
    A3("19.3 AiJobRunner"):::custom
    A4("19.4 Providers"):::custom
    A5("19.5 Settings + usage"):::custom
  end

  Db[("schema ai")]:::store
  CO("content"):::custom
  DF("dynamicfields"):::custom
  LLM(["AI provider"]):::ext
  Live("Live hub"):::core
  Search["Module.Search<br/>(planned)"]:::planned

  Ed <-->|F1| A1
  Ed <-->|F2| A2
  TAdmin <-->|F3| A4
  TAdmin <-->|F4| A5
  A1 --> A3
  A3 & A4 & A5 <--> Db
  A3 <-->|F5| CO
  A3 <-->|F6| DF
  A3 <-->|F7| LLM
  A3 -->|F8| Live
  A3 -.-o|planned| Search

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
  classDef planned fill:#16161d,stroke:#9a9bb3,stroke-dasharray:6 4,color:#b8b8c8
```

| Flow | Route or channel | Payload |
|---|---|---|
| F1 | `POST seo/optimize` (`ai.use`) | `SeoRequest` → `JobStarted` (jobId) |
| F2 | `GET jobs/{id}?after=`, `jobs/{id}/result`, `POST jobs/{id}/cancel` | → `JobView`, `AiJobResult` (SuggestionPatch) |
| F3 | `providers` (CRUD), `{id}/secret`, `{id}/secret/clear`, `{id}/test`, `default/test` | `ProviderFields`, `ProviderChange`, `SecretBody`, `VersionBody` → `ProviderDto`, `ProviderTestResult` |
| F4 | `GET/PUT settings`, `GET usage` | `SettingsChange` → `SettingsDto`, `UsageSummary` |
| F5 | `GET /api/content/posts/{id}` | → `PostDetail` |
| F6 | `GET /api/dynamicfields/fields/{key}/versions/{n}`, `POST content/validate-node` | `NodeValues` → `FieldVersionDto`, `ContentReport` |
| F7 | `IChatClientFactory` (OllamaSharp, OpenAI-compatible) | chat and structured prompts → completions |
| F8 | `ILiveNotifier`, topic `user:{id}` | `LiveMessage` of type `ai.chunk` (the chunk number in the payload) and `ai.done` |

**Tables (schema `ai`):** `providers`, `settings`, `usage`, `capability_policies`.

The module publishes and consumes no integration events. **Planned:**

- S1, S2: Module.Search, with a Content indexer, embeddings and the related-posts panel
- A0: Keywords `concepts/match`
- A2–A6: streaming compose grounded on Search, and keyword suggestion

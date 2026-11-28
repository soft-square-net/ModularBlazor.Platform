---
title: AI and Search (planned)
description: The plan for two modules. Module.Search owns the OpenSearch content index and embeddings; Module.Ai owns local LLM suggestions and depends on Search, never the reverse.
---

:::caution[Status: planned, nothing built]
The plan was reviewed on 2026-11-28. Neither repository exists yet. Names, routes and phases below can still change.
:::

The AI work is split into **two modules**:

- **`ModularBlazor.Module.Search`**: the content search index, its embeddings and its query API. Search is useful
  without any AI: site search, related posts and the library all need it.
- **`ModularBlazor.Module.Ai`**: local-first LLM suggestions in the studio (SEO, compose, rephrase, keywords). It
  depends on Search through `WebApi.Modules.Search.Client`. **Search never references Ai.**

An earlier draft put search inside the AI module. That was dropped: vectors are part of the index format (model plus
dimension set the index version), so if Ai produced them, Search could not reindex without Ai installed.

## Who owns what

| | Module.Search | Module.Ai |
| --- | --- | --- |
| Data | An OpenSearch **content** cluster, separate from the logs stack. One index (or alias) per tenant, `mb-{tenant}-docs-v{n}`, with `arabic` and `english` analyzers | Its own feedback data (decided in a later phase) |
| Indexing | A worker subscribed to `content.*.v1` [integration events](/backend/integration-events/) (and Keywords concept events), backfill from Content's `/events?after=` feed, reindex by alias swap | — |
| Embeddings | An `IEmbeddingGenerator` (Microsoft.Extensions.AI), Ollama by default. Optional: without one, search is BM25 only | Chat and structured-output models, routing, an optional external "teacher" |
| API | `POST /api/search/query` (BM25 plus kNN hybrid, tenant-filtered), `/related`, `/similar-text` | Studio jobs: SEO optimize, compose, keyword suggestion |
| UI | Library and site search, a "Related" studio panel | An assistant dock panel |

## Rules the plan fixes

- **Suggestions never write content.** Each suggestion is a patch with one target (layout, values, placement,
  keywords), applied by the module that owns that data: layout through the studio's undo stack, values as a Content
  revision.
- **Deterministic checks first.** DynamicFields' JSON Schema of a field is used as the model's structured-output format,
  and DynamicFields validates the result.
- **Tenant isolation.** Per-tenant indices; the tenant and culture are part of every cache key; provider keys are
  encrypted and never logged. Permissions planned: `ai.use`, `ai.escalate`, `ai.configure`, `ai.behavior.read`.
- **Streaming over the live hub, not SSE.** A compose call returns `202 { jobId }`. Chunks go to the user's
  `user:{id}` topic on [`/hubs/live`](/backend/live-updates/) as `ai.chunk`, batched about every 100–150 ms, then
  `ai.done` or `ai.failed`. Missed chunks are read from `GET /api/ai/jobs/{id}?after=`, and the final patch only from
  `GET /api/ai/jobs/{id}/result`, never over the hub.
- **Runtime.** Ai runs in its own pool with one replica that owns the GPU queue. It must pass an unload test
  (collectible AssemblyLoadContext).

## Hardware and models

One GPU with 16–24 GB. An 8B chat model for streaming, a 14B model for structured output only on 24 GB, and a bge-m3
class multilingual embedder. Models are set in configuration (`Ai:Models:*`, `Search:Embeddings:Model`) and chosen
by an Arabic and English evaluation set. One priority gate: streaming first, then structured calls, then Search's
background embeddings.

## Phases

| Phase | Scope |
| --- | --- |
| A0 | Keywords batch match (`POST /concepts/match`); the studio's command and event contract for panels |
| S1 | Module.Search scaffold, OpenSearch in the playground, per-tenant indices, Content indexer and backfill, BM25 query |
| A1 | Module.Ai scaffold, tenant router, Ollama adapter, local-only lock; one slice end to end: SEO optimize |
| S2 | Embeddings, hybrid query, related posts, the studio "Related" panel |
| A2 | Streaming compose, grounded on Search |
| A3 | Keyword suggestion (Ai, Keywords match and Search) |
| A4–A6 | Feedback, behavior signals, offline distillation |

Still open: which external providers, whether tenants may bring their own keys, and whether training on tenant data is
in scope at all.

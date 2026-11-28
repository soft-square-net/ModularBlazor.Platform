---
title: AI and Search
description: The plan for two modules. Module.Search owns the OpenSearch content index and embeddings; Module.Ai owns local LLM suggestions and depends on Search, never the reverse.
---

:::caution[Status: A1 built locally, not published]
The plan was reviewed on 2026-11-28. **Module.Ai phase A1** (SEO suggestions) is built and tested, but only as local
commits: there is no GitHub repository for it yet, so it is not released, not in CI and not in any deployment.
**Module.Search** and the other phases are planned. Names, routes and phases can still change. See [Module.Ai A1, as
built](#moduleai-a1-as-built).
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

| Phase | Scope | Status |
| --- | --- | --- |
| A0 | Keywords batch match (`POST /concepts/match`); the studio's command and event contract for panels (`IStudioCommands`) | Planned |
| S1 | Module.Search scaffold, OpenSearch in the playground, per-tenant indices, Content indexer and backfill, BM25 query | Planned |
| A1 | Module.Ai scaffold, tenant router, Ollama adapter, local-only lock; one slice end to end: SEO optimize | **Built, local commits only** |
| S2 | Embeddings, hybrid query, related posts, the studio "Related" panel | Planned |
| A2 | Streaming compose, grounded on Search | Planned |
| A3 | Keyword suggestion (Ai, Keywords match and Search) | Planned |
| A4–A6 | Feedback and escalation to external providers, behavior signals, offline distillation | Planned |

Settled in A1: tenants **bring their own keys** (write-only, encrypted), and nothing goes to an external provider while
the tenant's local-only lock is on. Still open: which external providers, and whether training on tenant data is in
scope at all.

## Module.Ai A1, as built

Module id, route prefix and schema `ai`, on Module SDK 1.0.0-preview.8, in Clean Architecture layers, for the four
database engines, in English and Arabic. Version 0.1 does **SEO**: it proposes the post field's `seo` group (search and
sharing values) with a local model (Ollama). It does not search: Search is not built yet.

| Permission | Allows |
| --- | --- |
| `ai.use` | Ask the assistant: start, follow, read and cancel your own jobs. Shows the studio's Assistant panel |
| `ai.configure` | The tenant's providers, keys, policies, budget and usage |
| `ai.escalate`, `ai.behavior.read` | Reserved for A4 and A5 |

| Method and route (under `/api/ai`) | Permission | What it does |
| --- | --- | --- |
| `POST /seo/optimize` `{ postId, culture }` | `ai.use` | 202 `{ jobId, capability, servedBy, note }`; 503 when no model is available or the queue is full |
| `GET /jobs/{id}?after=` | `ai.use` | Status and the streamed chunks after `after` (catch-up) |
| `GET /jobs/{id}/result` | `ai.use` | `{ patches: SuggestionPatch[], issues, servedBy, note }`; 409 while it runs |
| `POST /jobs/{id}/cancel` | `ai.use` | 204 |
| `GET`/`POST /providers`, `PUT`/`DELETE /providers/{id}` | `ai.configure` | The tenant's providers, never with the key |
| `PUT /providers/{id}/secret`, `POST /providers/{id}/secret/clear` | `ai.configure` | Set or remove a key (write-only: only its last 4 characters are shown) |
| `POST /providers/{id}/test`, `POST /providers/default/test` | `ai.configure` | A tiny call to the provider, or to the operator's Ollama |
| `GET`/`PUT /settings` | `ai.configure` | The local-only lock, the monthly external token budget, a policy per capability |
| `GET /usage?year=&month=` | `ai.configure` | Calls, failures, tokens and average time per capability and model |

A job belongs to the user who started it: anyone else, in any tenant, gets 404.

How a suggestion is made:

1. The **router** refuses a capability whose policy is Off (403). With the local-only lock on, an external policy is
   served locally. An external route needs an enabled provider with a key, a kind this version can call, and budget
   left; otherwise the call is served locally and `note` says why.
2. The job waits at the **GPU gate** (`Ai:Ollama:Parallel` calls at a time; interactive before structured before
   background).
3. It reads the post from Content and the field's schema from DynamicFields **as the user**, so their permissions apply.
4. The model answers with JSON constrained by the `seo` group's JSON Schema, and DynamicFields `validate-node` checks
   it, with one repair round.
5. Text streams to the user's `user:{id}` topic on the live hub as `ai.chunk { jobId, seq, text }`, then `ai.done` or
   `ai.failed`. The proposal is one `values` SuggestionPatch, with the current values beside the proposed ones.
   **Nothing is saved**: the editor applies it.
6. Usage is recorded (tokens, time, outcome), never the text.

The client has an **AI settings** page (`/ai`) and an **Assistant** dock panel in the Content Designer studio. The
panel's **Apply** stays disabled until the studio offers `IStudioCommands` (A0); until then **Copy values** puts the
proposal on the clipboard. The module passes an unload test (it uses a module HTTP handler so it can unload), and the
repository has an SEO evaluation set of 32 Arabic and 32 English posts with a scorer; the models have not been scored
on a GPU yet.

Main settings: `Ai:Ollama:Endpoint` (empty: no local model), `Ai:Ollama:ChatModel` (`qwen3:8b`) and `StructuredModel`,
`Ai:Ollama:Parallel` (2), `Ai:ApiBaseUrl` (the gateway, for Content and DynamicFields), `Ai:MaxQueuedJobs` (50),
`Ai:JobRetentionMinutes` (10), `Ai:ChunkIntervalMs` (120), `Ai:MaxContextChars` (6000), `Ai:CallTimeoutSeconds` (180).

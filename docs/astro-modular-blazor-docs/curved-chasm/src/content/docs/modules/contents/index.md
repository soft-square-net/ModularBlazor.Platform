---
title: Contents
description: Domain boundaries and design for tenant-scoped posts whose type-specific payloads are defined by Dynamic Fields forms.
---

> **Status: implemented** in the repository
> [`ModularBlazor.Module.Content`](https://github.com/soft-square-net/ModularBlazor.Module.Content) (branch `main`,
> wave 4 C1–C4 and the 3c fixes), on Platform preview.8. `Content.Contracts` 1.0.0-preview.1 is published.

The **Contents** module owns the company's editorial posts and the rules that govern them. A post has a stable Contents-owned envelope (tenant, press, category, author, state, revision, scheduling, and publication data) and a versioned document (`content` and `style` JSON plus nested child nodes) whose `content` is meant to be validated against an immutable form version from **Dynamic Fields**. Post forms can define articles, video posts, image posts, document posts, albums, or mixed posts containing ordered paragraph/image/video/document blocks.

In this strategy, **Post** is the Contents aggregate for any editorial item, whether it is a video, paragraph-led article, document, image, album, or mixed composition. Dynamic Fields defines payload shape and validation, not the Post identity, editorial lifecycle, authorization, or media storage.

This is a domain module, not a generic file store or workflow engine. It can be omitted from an ERP deployment without changing Identity, Tenancy, or unrelated modules. Shifts and Elsa are optional collaborators; the Content module remains the authority for content state even when neither is deployed.

## Agreed boundaries

| Concern | Owner | Boundary |
| --- | --- | --- |
| Global accounts, authentication, tenant membership | Identity service | Content stores stable actor IDs, never credentials or account copies. |
| Company/tenant directory and tenant resolution | Tenancy and host | Every query and mutation runs inside one resolved tenant. |
| Presses, categories, posts, payload-version references, editorial state, revisions, and history | Contents | The module validates all post-level invariants and transitions. |
| Published form definitions, nested payload schemas, reusable fields, and payload validation | Dynamic Fields | It validates versioned payload JSON; it does not own posts or their submitted values. |
| Org units (Org → Sector → Department → Section), their members and the categories each Section serves | [Organization](/modules/organization/) | Contents stores a post's and a content's unit id and path; it never owns units. |
| Rota, shift clock, check-in/out, shift grants on unit scopes | [Shifts](/modules/shifts/) | It produces time-bounded grants (from check-in to check-out); it does not own Post data. |
| Proposals, post projects, routing to a Section | [NewsRoom](/modules/newsroom/) (planned) | It asks Contents to create the draft posts; Contents keeps their state. |
| Timers, reminders, delayed assignment, scheduled publication, handover orchestration | Elsa | Elsa requests operations from Contents and Shifts APIs; it does not update their databases directly. |

Modules communicate through contracts and HTTP APIs. They do not share a `DbContext`, join each other's tables, or take cross-module database dependencies. In an all-in-one deployment, calls may be routed through a local gateway; in a distributed deployment, they use the service routes. The module boundaries stay the same in both modes.

## Recommended section structure

- [Posts, dynamic forms, and structure rules](/modules/contents/posts/) defines the Post envelope, form-defined payloads, invariants, and authorization checks.
- [Workflow and module integrations](/modules/contents/workflow-and-integrations/) describes the authoring lifecycle, Shifts/Elsa responsibilities, deployment choices, and rollout sequence.

Contents selects the published form version for each post kind and persists that version reference with its payload. The optional [Dynamic Fields module](/modules/dynamic-fields/) owns the definitions and validation; Contents owns the post values and lifecycle.

For canonical topics/entities, multilingual aliases, and tag resolution, see the [Keywords module](/modules/keyword/) and the [Taxonomies module](/modules/taxonomies/). Contents remains the authority for tags assigned to each Post.

The existing [authentication and authorization design](/backend/auth-design/) records the agreed identity, grant, and shift workflow decisions. This section focuses on the Content module's own domain and its contracts with those modules.

## What exists versus what is planned

The code (repository `ModularBlazor.Module.Content`, module `content`) already has:

- **Clean Architecture layers** (see the [template](/backend/module-template-clean-architecture/)):
  `WebApi.Modules.Content.Core` (the `PostAggregate`, `PressAggregate` and `CategoryAggregate`),
  `WebApi.Modules.Content.UseCases` (Mediator commands and queries returning an Ardalis `Result`),
  `WebApi.Modules.Content.Infrastructure` (`ContentDbContext`, tenant migrations) and `WebApi.Modules.Content.Web`
  (the entry assembly, one REPR endpoint per route). Its build output is what you upload.

- **The `Post` aggregate**, whose **kind is a DynamicFields root post field key** (`post.article`, `post.news`, `post.review`, `post.video`, ... or a tenant's own). The database still uses the deployed names: table `articles`, the kind in column `Type`, history in `article_history`. See [Post kinds and URLs](#post-kinds-and-urls).
- **Shared contents.** A post references contents from a shared library (`ContentItem`: paragraph, image, video, ...). Each content has its own stage in a pipeline stored as data, and the post pins the accepted versions. Every change is written to a change feed (`GET /events?after=`).
- **Categories and units.** A post has a primary category and up to 20 categories in its press, and may belong to an [Organization](/modules/organization/) unit. See [Categories and units](#categories-and-units).
- **Work items, guards and events.** Each content is a [work item](/backend/work-items/); DynamicFields checks the content before review and the post before approve, schedule and publish; every change goes out as an [integration event](/backend/integration-events/). See [Work items, checks and events](#work-items-checks-and-events).
- **A stable public ID.** Every new Post gets a random `PublicId`. Rows that existed before migration `AddPostDocumentAndPublicId` were backfilled with `legacy-{Id}`, the permalink they already had.
- **A versioned Post document.** `SchemaId` and `SchemaVersion` identify the field contract. `DocumentJson` holds a `PostDocument` with separate `content` and `style` JSON objects and a tree of nested `PostNode` children (see [Post document](/modules/contents/posts/#post-document-implemented)). A new post gets a root of its kind, with `headline` taken from the title; its values are edited with `PUT /posts/{id}/root`.
- **A published snapshot.** Publishing copies the title, body and document into `PublishedTitle`, `PublishedBody` and `PublishedDocumentJson`. Anonymous readers get only that snapshot from `GET /api/content/public/{publicId}`.
- **The newsroom workflow**, its permissions, the `Content:Newsroom:WorkflowOwnsAssignment` setting and the automation endpoints Elsa still calls. See [Workflow and module integrations](/modules/contents/workflow-and-integrations/#serviceapi-boundary).
- **Live updates and a lookup.** The post topic is declared with `LiveTopic.AnyOf("post", ...)` (author plus every permission that can see a post), and the `content.categories` [lookup](/backend/lookups/) lists a press's categories in Arabic and English.
- **`Content.Contracts`**, the published package (`soft.square.net.Content.Contracts` 1.0.0-preview.1): event types and records, `ContentWorkItems`, `ContentLiveTopic`. The module itself runs on a Platform **preview.8** host.
- **The browser side**, as two client modules:
  - `WebApi.Modules.Content.Client`, the generated API client, ships as a hidden client module.
  - `Module.Content` is the **Contents Management** UI, with its menu item and pages under `/contents`:
    - a post list by view (mine, queue, review, desk) and state;
    - a post page with the workflow actions (submit, accept, assign, approve, return, schedule, publish), editing and history;
    - presses and categories.

  It needs a tenant sign-in and the `content.*` permissions in that tenant. The UI is in Arabic and English (see
  [Languages](/guides/i18n/)).
- **Translations** of a post's document, described [below](#translations).
- **A playground** (`playground/`) that runs the platform with the module for development. It grants the dev accounts
  their `content.*` permissions through `Access:DevGrants`, and `--Playground:DynamicFields=<path>` loads the
  DynamicFields module beside it. How to build both parts and the playground for a module of your own:
  [Create a module, A to Z](/backend/create-a-module/).

### Translations

A post's document is written in one language. Each other language is a **translation overlay** kept with the post, in
the format the [DynamicFields module](/modules/dynamic-fields/) defines: per node id and value key, the translated
text and a hash of the source it was made from. Only textual values are translated; ids, numbers, choices, style and
the tree's shape always come from the source.

| Route (under `/api/content`) | Does |
| --- | --- |
| `PUT /posts/{id}/translations/{language}` `{ version, nodes }` | Adds or replaces one language. Whoever may edit the post now may translate it; the workflow state does not change |
| `DELETE /posts/{id}/translations/{language}?version=` | Removes one language |
| `GET /public/{publicId}?culture=ar-EG` | The published document with its text from the `ar-EG` translation, then `ar`, then its own; `languages` lists what it is translated into, `servedLanguages` what this answer used |

The translations are published with the document: on a post whose live version is the current one, a translation
change is live at once; otherwise it goes live with the next publication. When the source text changes, a translation
is kept and shown as stale. The post page lists the translations and opens `Module.DynamicFields/translate-content`
to add or edit one, when that module is installed.

### Post kinds and URLs

A post's kind is the key of a **DynamicFields root post field**. Contents reads the published ones from
`GET /api/dynamicfields/catalog/root-post-fields` (cached per tenant for `Content:PostKindCacheSeconds`, 60 s) and
serves them to its own picker at `GET /api/content/kinds`. Each root field has a static **URL prefix** (`news`,
`articles`, `reviews`, ...).

| Rule | Result |
| --- | --- |
| Create with a kind that is not published | 400 |
| Publish or schedule a kind with no published root field | 409: the post can be edited but has no URL yet |
| DynamicFields unreachable | 503 |
| URL | `/{urlPrefix}/{slug}`, the prefix stored at publish (`PublishedUrlPrefix`); `canonicalPath` on the detail and the public read |

The old enum names (`Article`, `News`, `Video`, `Reportage`, `PhotoStory`, `Other`) were migrated to `post.article`,
`post.news`, `post.video`, `post.reportage`, `post.photo-story` and `post.other` on all four engines, and are still
accepted as input for one release. A tenant must install the DynamicFields starter fields before these kinds can be
published. Posts made before a kind root existed keep their `legacy.basic` root until someone fills in the kind's
values ("Fill in the values" on the post page); nothing is migrated automatically.

### Categories and units

- **Categories.** `PrimaryCategoryId` (column `articles.CategoryId`) plus `post_categories` rows: the primary and up to
  20 categories of the post's press. `PUT /posts/{id}/categories { version, primaryCategoryId, categoryIds }`.
  Categories have `NameEn` and `NameAr`.
- **Units.** A post and a content may have an Organization `UnitId` and its stored `UnitPath`. `PUT /posts/{id}/unit`
  and `PUT /contents/{id}/unit` need **`content.unit.assign`** (granted to nobody by default; it is meant for the
  workflow's service identity). A unit must serve one of the post's categories (or `*`): 409 otherwise, 400 for an
  unknown unit, 503 when Organization is down. Stored paths are rebased on `org.unit-moved.v1`.
- **Scopes.** A post's grants match `press:`, `category:` for every category, and `unit:{id}` of its unit and every
  ancestor (`UnitScopes.For`). A content with a unit also matches `section:{unit id}`.
- Contents handles `shifts.section-moved.v1`: contents of a moved legacy section get the section's new unit.

### Work items, checks and events

- **Stages are work-item states.** The default pipeline is `assigned → in-progress → submitted → in-review → accepted
  | returned`; actions `start`, `submit`, `review`, `accept`, `return` (note required), `reopen`. The old keys `draft`,
  `desk`, `rejected` and the action `reject` were renamed (migration `WorkItemStages`) and are still accepted on
  input.
- **Guards.** An action that moves a content to a review or accepted stage first runs DynamicFields `validate-node`;
  approve, schedule and publish run `validate` on the post. A refusal is **422** with `problems`
  (`[{ path, message, nodeId }]`), recorded in its own save as a feed row and a `content.validation-failed.v1` event.
- **Events** (source `content`, written to the outbox in the same save): `content.post-created.v1`,
  `post-categories-changed`, `post-unit-changed`, `validation-failed`, `work-item-assigned`,
  `work-item-state-changed`, and one event per feed type (`content-created`, `content-changed`, ...,
  `post-state-changed`). The records are in `Content.Contracts`.

Still planned:

- More Post kinds (document, image, album, mixed story), immutable revisions, media assets and Keyword tags.
- Moving `Title`/`Body` into the document. Both are still required columns, and the list views and search depend on them.
- A service token for the DynamicFields and Organization calls: today they forward the caller's token, so they work
  only on authenticated requests.
- Removing the shift-based accept and assign, `AssignedShiftId` and `/automation/*` once Elsa's workflows replace the
  poller (wave 5, E3).

The complete production model is still a design target. Media and asset lifecycle, audit retention, publication channels, moderation and editorial configuration, and the final API versioning strategy need separate analysis before they are built. Do not infer these features from the starter schema.

## Design principles

1. Tenant isolation is mandatory; a press always belongs to exactly one tenant.
2. Every Post belongs to one press, with a primary category and optional further categories in that press. An Organization Section serves a list of categories (or `*`), which may come from several presses.
3. Content state is changed through application use cases and version-checked commands, never by direct table updates from Elsa or Shifts.
4. Authorization is checked at request time against tenant grants and the Post's press/category scopes. A role name alone does not authorize an action.
5. Shift access starts at check-in and ends at check-out or by the shift clock. A long-lived token does not extend an expired shift grant.
6. Keep editorial identity and lifecycle fields in the Contents Post envelope; let an immutable Dynamic Fields form version define the shape of the document `content`.
7. Preserve the published snapshot while a new edit waits for Desk approval. A draft edit must not silently change live output.

These boundaries allow the platform to ship one image and run on one machine, scale selected modules into separate pools, or omit the newsroom modules entirely.

---
title: ContentDesigner
description: The ContentDesigner runtime module. It arranges a post of a kind in four views (Mobile, Tablet, Desktop, AMP), checks them against the kind's schema, fills JSON-LD and renders the published post; the studio is unit-scoped and follows Content live.
---

> **Status: implemented** in the repository
> [`ModularBlazor.Module.ContentDesigner`](https://github.com/soft-square-net/ModularBlazor.Module.ContentDesigner)
> (branch `main`): the core and rendering (phase 4), the studio v1 and v2, hot reload on the live channel (6a) and the
> newsroom content model (**6b**), plus the 3c fixes (F2, F5, F6), DynamicFields warnings (PR #11) and problem marks on
> the canvas (PR #12). It still builds on Platform preview.7. **Planned:** 6c, where NewsRoom contributes the Workflow
> status and Elsa Studio dock panes, and the move to preview.8; then [editing content values in the
> studio](#planned-editing-content-in-the-studio).

**ContentDesigner** is the newsroom's post designer. It opens a post of a known **kind** with the
[contents](/modules/contents/) its units work on, and arranges them in four **views**: Mobile, Tablet, Desktop and AMP.
It checks each view against the kind's DynamicFields schema, fills the JSON-LD, saves block and document templates,
and renders the published post as HTML and as an AMP page.

The module's id, route prefix and database schema are `contentdesigner`. Its permissions are `contentdesigner.read`,
`contentdesigner.arrange`, `contentdesigner.templates` and `contentdesigner.publish`.

## The model

- A **post design** belongs to one Content post. It has a draft and, once published, a published state; both hold the
  four views. A view is a tree of **regions** (section, row, column; at most four deep) with **placements** of the
  post's parts: a root value of the kind (`prop:headline`), one content (`content:{id}`), or `items` (everything not
  placed by name).
- Regions and placements carry **theme tokens** only (width, align, spacing, tone, size, ...), never free CSS. Mobile,
  Tablet and Desktop share one tree: they reorder, resize and hide, but never move a part to another region.
- A post's **kind** is the key of its DynamicFields root post field (`post.news`, `post.article`, ... or a tenant's own
  kind), which also owns the kind's URL prefix. The migration `PostKindsAsFieldKeys` turned the old enum names into
  keys, and old names are still read as keys.
- **Document templates** (a design for a kind, one default per kind) and **block templates** (how one block field shows
  in a view).

**Checks** run on `POST .../design/check` and before every publish: parts exist, required values show in every view,
tokens match field styles, heading and reading order (warnings), AMP renderers and AMP rules, and DynamicFields
validates the post and builds its JSON-LD. Errors block publishing. Publishing pins the content revisions it was
checked against.

A post made before Contents gave new posts a root of their kind has a `legacy.basic` root. The check reports one clear
error for it: fill in the kind's values in Contents Management first.

## Rights follow the unit and the shift

`arrange` and `publish` are checked on **this post**, with the caller's grants at that moment. A grant counts when its
scope is the post's unit or a unit above it (`unit:{id}`), the post's press or category (kept for compatibility), or
the whole tenant.

- Shift rights start at check-in and end at check-out, so the studio opens and closes with the shift. When rights end
  mid-edit, the studio shows **"Your shift ended"**: drafts are kept in the browser, rights are asked again every 15 s,
  and a refused save is retried. A retried save never overwrites a newer design.
- A post in **no unit yet** belongs to the moderator and the Desk: besides the ContentDesigner permission, the caller
  needs one of `ContentDesigner:UnitlessEditorPermissions` (default `newsroom.moderate,content.accept,content.publish`).
- Templates and the read routes (check, preview) are not unit-scoped.

`GET /posts/{postId}/rights` answers `canArrange`, `canPublish` and why not (`no-unit`, `no-right`, `post-hidden`).

## The studio

The studio (`/contentdesigner/studio/{postId}`) is built on MudDock from UI.MudTail and its panels use the
[studio panel contract](/guides/studio-panels/).

- Canvas per view, drag and drop, focus mode, undo and redo, the block template library, AMP checks per block, and
  "add an existing content" from Content's shared library (filtered by unit).
- The **tray** shows each content's unit, work-item stage and assignee, with filters by state and unit. The header shows
  the post's unit and categories, read only.
- The **Problems** panel merges the designer's own findings with Content's refusals (`content.validation-failed` from
  the feed, the live nudge and 422 `problems`), deduplicated by node id, path and message. DynamicFields' `issues` are
  errors; its `warnings` (v2 phase 0) are shown as warnings and do not make a design invalid. An older DynamicFields
  sends none.
- **Problem marks**: after a Check, each part with a problem gets a dashed outline on the canvas, in the theme's error
  colour or warning colour (an error wins). A problem in a node inside a content marks that content block; a problem of
  another view does not mark the current one. Selecting a part with problems brings the Problems pane forward and
  narrows it to that part ("Only …" chip, **Show all**). Not checked in a browser yet.
- The style tokens are the same table as DynamicFields' `StyleVocabulary` and MudTail's `_style-tokens.scss`; a test
  in each repository pins it.
- **Hot reload**: the studio joins Content's post [live topic](/backend/live-updates/) and reads the feed again until it
  reaches the nudged event (F2), or polls when the topic cannot be joined.

## API

Under `/api/contentdesigner`; every call except the render routes needs a signed-in caller in a tenant. Edits send
the `version` they read and get 409 if the design changed.

| Route | Does |
| --- | --- |
| `GET`, `POST`, `PUT`, `DELETE /posts/{postId}/design`; `PUT .../design/views/{view}` | Read, create, replace, delete a design, or arrange one view |
| `GET /posts/{postId}/workspace` | What the studio opens: post (unit, categories, URL prefix), contents (unit, stage, assignee, revisions), design, rights |
| `GET /posts/{postId}/rights`, `GET /kinds`, `GET /units` | Rights on the post; post kinds with URL prefixes; unit names for filters |
| `POST .../design/check`, `.../preview`, `.../publish`, `.../unpublish` | Check, preview, publish and take down |
| `/document-templates`, `/block-templates`, `POST .../save-as-template` | Templates |
| `GET /library`, `POST /posts/{postId}/contents` | Content's shared library; add a content to the post |
| `GET /render/{publicId}`, `GET /render/{publicId}/amp` | Anonymous: the published post as HTML or AMP |

The designer reads Content, DynamicFields and Organization over HTTP through the gateway with the caller's token, never
in process. Organization is optional: without it the studio shows ids instead of unit and member names.

## Planned (6c)

- Move to Platform preview.8.
- NewsRoom contributes the **Workflow status** and **Elsa Studio** dock panes.

## Planned: editing content in the studio

Agreed on 2026-11-28; no code yet. Today the studio edits the design only. It will also edit content values in place,
always saving through Content's own endpoints (`PUT /contents/{id}`, `PUT /posts/{id}/root`, translations), so it never
bypasses Content's rights.

- Editing a content value needs a permission. Style tokens are allowed to whoever may arrange the post.
- Rich text has two permissions, `content.richtext.basic` (words, bold, italic) and `content.richtext.full` (links,
  headings, lists, quotes). Content enforces and cleans both on save.
- Grants come from the platform grant store: Desk and moderators through their shift roles, the editor in chief as a
  standing grant, and a writer on their own work until it is submitted. An Elsa "Grant permission" activity follows a
  reassignment and grants until the assignee's shift ends.
- Order: E1 plain text, E1b rich text, then files, other values and live checks; the Elsa activity last.
- The studio's Publish works only while the post project is `InProduction` or `ReadyForDesk`.

See [NewsRoom](/modules/newsroom/) for the flow these panes belong to.

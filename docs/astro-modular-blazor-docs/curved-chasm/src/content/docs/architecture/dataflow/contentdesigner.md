---
title: ContentDesigner
description: "Level-1 dataflow of Module.ContentDesigner: post designs with four views, document and block templates, the content library, public rendering (HTML and AMP), and its calls to Content and DynamicFields."
sidebar:
  order: 16
---

`contentdesigner` (Module.ContentDesigner, v0.2, **core pool**) stores the design of each post: its views,
including AMP. It also stores document and block templates, gives the studio a library of shared contents, and
renders published posts for readers. It reads posts and contents from Content, and checks designs with
DynamicFields. The studio's hot reload listens to Content's live topic `content:post:{id}`.

```mermaid
flowchart LR
  Ed(["Designer / editor<br/>(Studio)"]):::ext
  Reader(["Reader"]):::ext

  subgraph CD["contentdesigner (/api/contentdesigner)"]
    direction TB
    X1("16.1 Designs"):::custom
    X2("16.2 Workspace + library"):::custom
    X3("16.3 Templates"):::custom
    X4("16.4 Catalog"):::custom
    X5("16.5 Render"):::custom
    X6("16.6 Gateway client"):::custom
  end

  Db[("schema contentdesigner")]:::store
  CO("content"):::custom
  DF("dynamicfields"):::custom
  Org("organization<br/>local directory"):::custom
  Live("Live hub"):::core

  Ed <-->|F1| X1
  Ed <-->|F2| X2
  Ed <-->|F3| X3
  Ed <-->|F4| X4
  Reader <-->|F5| X5
  Ed <-->|F6| Live
  X1 & X2 & X3 & X5 <--> Db
  X1 & X2 & X4 & X5 --> X6
  X6 <-->|F7| CO
  X6 <-->|F8| DF
  X6 <-->|F9| Org

  classDef core fill:#28243f,stroke:#8b7cf6,color:#f4f2ff
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
```

| Flow | Route or channel | Payload |
|---|---|---|
| F1 | `GET designs`, `posts/{id}/design` (`GET`, `POST` open, `PUT` save, `DELETE`), `design/views/{view}`, `apply-template`, `check`, `preview`, `publish`, `unpublish` | `OpenRequest`, `SaveRequest`, `ArrangeRequest`, `ApplyRequest`, `CheckRequest`, `PreviewRequest`, `PublishRequest`, `UnpublishRequest` → `PostDesignDto`, `DesignPage`, `DesignReport`, `PreviewDto` |
| F2 | `posts/{id}/workspace`, `library`, `library/{contentId}`, `POST posts/{id}/contents`, `posts/{id}/events` | `AttachRequest` → `WorkspaceDto`, `LibraryPage`, `LibraryItemDto`, `FeedPage` |
| F3 | `document-templates` (+ `{id}`, `{id}/default`), `block-templates` (+ `{id}`), `design/save-as-template`, `views/{view}/save-block-as-template` | `NewDocumentTemplate`, `DocumentTemplateEdit`, `NewBlockTemplate`, `BlockTemplateEdit`, `SaveAsTemplateRequest`, `SaveBlockRequest`, `DefaultRequest` → `DocumentTemplateDto`, `BlockTemplateDto` |
| F4 | `GET kinds`, `units`, `posts/{id}/rights` | → `PostKindInfo[]`, `UnitName[]`, `PostRightsDto` |
| F5 | `GET render/{publicId}`, `render/{publicId}/amp` (anonymous) | → an HTML page, or an AMP page |
| F6 | `ILiveChannel`, topic `content:post:{id}` (`StudioHotReload`; catches up from `posts/{id}/events`) | `LiveMessage` |
| F7 | `/api/content/categories`, `contents?`, `contents/{id}`, `posts/{id}`, `posts/{id}/contents`, `posts/{id}/events?after`, `public/{publicId}` | → `CategoryDto[]`, `ContentSummary[]`, `ContentDetail`, `PostDetail`, `FeedPage`, `PublishedPost` |
| F8 | `/api/dynamicfields/catalog/root-post-fields`, `fields/{key}/versions/{n}`, `content/jsonld`, `content/validate` | `JsonLdRequest`, `ContentTree` → `RootPostFieldDto[]`, `FieldVersionDto`, `JsonLdReport`, `ContentReport` |
| F9 | `/api/organization/local/units` | → `DirectoryUnitDto[]` |

**Tables (schema `contentdesigner`):** `post_designs`, `document_templates`, `block_templates`.

The module publishes and consumes no integration events. **References:** `Organization.Contracts` and
`ModularBlazor.Abstractions` (the studio's dock panels and dialogs).

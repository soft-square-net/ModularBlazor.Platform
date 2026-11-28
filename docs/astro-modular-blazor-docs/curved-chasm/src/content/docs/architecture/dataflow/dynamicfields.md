---
title: DynamicFields
description: "Level-1 dataflow of Module.DynamicFields: versioned field definitions on schema.org, the catalog, validation, JSON-LD and translations, and the modules that call it."
sidebar:
  order: 15
---

`dynamicfields` (Module.DynamicFields, v0.1.0, **core pool**) owns the versioned field definitions: post kinds,
content kinds, the proposal form and the SEO group. They are built on the schema.org catalog. The module validates
content trees against those definitions and builds their JSON-LD. It publishes and consumes no events: the other
modules call it over HTTP.

```mermaid
flowchart LR
  Admin(["Field designer"]):::ext
  Callers(["content, newsroom,<br/>contentdesigner, ai"]):::custom

  subgraph DF["dynamicfields (/api/dynamicfields)"]
    direction TB
    D1("15.1 Fields"):::custom
    D2("15.2 Catalog"):::custom
    D3("15.3 Validate"):::custom
    D4("15.4 JSON-LD"):::custom
    D5("15.5 Translations"):::custom
  end

  Db[("schema dynamicfields<br/>fields, field_versions")]:::store

  Admin <-->|F1| D1
  Admin <-->|F2| D2
  Admin <-->|F5| D5
  Callers <-->|F1| D1
  Callers <-->|F2| D2
  Callers <-->|F3| D3
  Callers <-->|F4| D4
  D1 & D2 & D3 & D4 & D5 <--> Db

  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
```

| Flow | Route | Payload |
|---|---|---|
| F1 | `GET fields`, `fields/{key}`, `fields/{key}/versions/{n}`; `POST fields`, `PUT {key}/draft`, `POST {key}/publish`, `DELETE {key}` | `NewField`, `DraftEdit`, `PublishRequest` → `FieldDetailDto`, `FieldPage`, `FieldVersionDto` |
| F2 | `GET catalog/schema-org`, `catalog/root-post-fields`, `catalog/starter-fields`, `POST starter-fields/install` | `StarterInstall` → `SchemaOrgCatalogDto`, `RootPostFieldDto[]`, `StarterFieldDto[]`, `StarterInstallReport` |
| F3 | `POST content/validate`, `content/validate-node` | `ContentTree`, `NodeValues` → `ContentReport` |
| F4 | `POST content/jsonld` | `JsonLdRequest` → `JsonLdReport` |
| F5 | `POST content/translations/check`, `translations/apply` | `TranslationCheck`, `TranslationMerge` → `TranslationReport`, `LocalizedContent` |

| Caller | Routes it uses |
|---|---|
| content | `catalog/root-post-fields`, `fields/{key}/versions/{n}`, `content/validate`, `content/validate-node` |
| newsroom | `catalog/root-post-fields`, `fields/...`, `content/validate-node` |
| contentdesigner | `catalog/root-post-fields`, `fields/{key}/versions/{n}`, `content/jsonld`, `content/validate` |
| ai | `fields/{key}/versions/{n}`, `content/validate-node` |

---
title: Taxonomies and Keywords
description: "Level-1 dataflow of Module.Taxonomies (schemes and terms) and Module.Keywords (concepts and labels): the leaf vocabulary modules, with their lookup and JSON-LD endpoints."
sidebar:
  order: 18
---

These are two leaf modules in the **core pool**. They call no other module and use no events. Pickers in other
client modules reach them through their `lookup` endpoints, which take ids and return short summaries.

```mermaid
flowchart LR
  Ed(["Editor / vocabulary admin"]):::ext
  Pickers(["Pickers in other<br/>client modules"]):::client

  subgraph Tx["taxonomies"]
    direction TB
    T1("18.1 Schemes + terms"):::custom
    T3("18.2 Term search + lookup"):::custom
  end
  subgraph Kw["keywords"]
    direction TB
    K1("18.3 Concepts"):::custom
    K2("18.4 Concept lookup"):::custom
    K3["18.5 A0 planned:<br/>concepts/match"]:::planned
  end

  TDb[("schema taxonomies<br/>schemes, terms")]:::store
  KDb[("schema keywords<br/>concepts, concept_labels")]:::store

  Ed <-->|F1| T1
  Pickers <-->|F2| T3
  Ed <-->|F3| K1
  Pickers <-->|F4| K2
  T1 & T3 <--> TDb
  K1 & K2 <--> KDb
  K3 -.-o K1

  classDef client fill:#173b35,stroke:#45c2a1,color:#e5fff5
  classDef custom fill:#202f43,stroke:#5da9e9,color:#e5f0ff
  classDef ext fill:#3a1f2b,stroke:#e07a9a,color:#ffe5ee
  classDef store fill:#1f2630,stroke:#9ab0c8,color:#e6eef8
  classDef planned fill:#16161d,stroke:#9a9bb3,stroke-dasharray:6 4,color:#b8b8c8
```

| Flow | Route (`/api/taxonomies`, `/api/keywords`) | Payload |
|---|---|---|
| F1 | `schemes` (CRUD), `schemes/{key}/jsonld`, `schemes/{key}/terms`, `terms/{id}` (`PUT`, `DELETE`), `terms/{id}/move` | `NewScheme`, `SchemeEdit`, `NewTerm`, `TermEdit`, `TermMove` → `SchemeDto`, `SchemeSummaryDto[]`, `JsonLdDto` |
| F2 | `GET terms`, `terms/lookup?ids`, `terms/{id}/jsonld` | ids → `TermRefDto[]`, `JsonLdDto` |
| F3 | `concepts` (list, get, create, edit), `{id}/retire`, `{id}/restore`, `types` | `ConceptValues`, `ConceptEdit`, `VersionRead` → `ConceptDto`, `ConceptPage`, `ConceptType[]` |
| F4 | `GET concepts/lookup?ids`, `concepts/{id}/jsonld` | ids → `ConceptSummaryDto[]`, `ConceptJsonLdDto` |

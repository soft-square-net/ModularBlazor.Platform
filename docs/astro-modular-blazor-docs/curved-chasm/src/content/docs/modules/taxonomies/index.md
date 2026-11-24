---
title: Taxonomies
description: The taxonomies runtime module. It keeps each tenant's controlled vocabularies (site sections, categories, genres) as trees of terms, published as schema.org DefinedTermSets and linked to Keywords concepts.
---

> **Status: implemented** in the repository
> [`ModularBlazor.Module.Taxonomies`](https://github.com/soft-square-net/ModularBlazor.Module.Taxonomies) (branch
> `master`). The client module is in Arabic and English.

The **Taxonomies** module keeps the tenant's **schemes**: controlled vocabularies such as site sections, product
categories or genres. Each scheme is a tree of **terms** (one parent per term), written out as a schema.org
`DefinedTermSet` of `DefinedTerm`s. A term can stand for a concept of the [Keywords module](/modules/keyword/) (by id,
with a name snapshot), so content classified with the term is about that concept too. Other client modules choose
terms with the `pick-terms` dialog.

The module's id, route prefix and database schema are `taxonomies`. Its permissions are `taxonomies.read` and
`taxonomies.manage`.

## Layout

It follows the [Clean Architecture layout](/backend/module-template-clean-architecture/):

| Project | Holds |
| --- | --- |
| `WebApi.Modules.Taxonomies.Core` | `SchemeAggregate`: the scheme and its term tree, the rules, JSON-LD, search keys |
| `WebApi.Modules.Taxonomies.UseCases` | Mediator commands and queries, returning an Ardalis `Result` |
| `WebApi.Modules.Taxonomies.Infrastructure` | `TaxonomiesDbContext`, tenant migrations per database provider |
| `WebApi.Modules.Taxonomies.Web` | The entry assembly: `IRuntimeModule`, one REPR endpoint per route |
| `WebApi.Modules.Taxonomies.Client` | The NSwag API client, a hidden client module |
| `Module.Taxonomies` | The Taxonomies pages (`/taxonomies`) and the `pick-terms` dialog |

## Schemes and terms

- **A scheme** has a unique **key** (`news-sections`, its id in routes and in other modules), a name, an optional
  description and a language.
- **Terms** form a tree, at most 8 levels deep and 2000 terms per scheme. A term has a name, a slug unique in the
  scheme, an optional code (schema.org `termCode`, for example an IPTC code; the slug when empty), a description and
  an optional Keywords concept.
- **One aggregate.** Every change to a scheme or its terms carries the scheme's `version` and returns the whole scheme,
  so two editors never overwrite each other (409).
- A move never makes a cycle. Only a term without children can be removed, and only an empty scheme can be deleted.

## The API

Under `/api/taxonomies`. Reads need `taxonomies.read`, changes `taxonomies.manage`.

| Route | Does |
| --- | --- |
| `GET /schemes`, `POST /schemes` | List the schemes with their term counts; create one (a taken key is a 409) |
| `GET`, `PUT`, `DELETE /schemes/{key}` | The scheme with its terms in tree order; rename or describe it; delete an empty one |
| `GET /schemes/{key}/jsonld` | The scheme as a `DefinedTermSet` |
| `POST /schemes/{key}/terms` | Add a term, last among its siblings |
| `PUT`, `DELETE /schemes/{key}/terms/{termId}` | Edit a term (name, slug, code, description, concept); remove one without children |
| `POST /schemes/{key}/terms/{termId}/move` | Move a term with its subtree |
| `GET /terms?search=&scheme=&limit=` | Search terms by name, slug, code or concept, ignoring case, accents and Arabic letter forms |
| `GET /terms/lookup?ids=`, `GET /terms/{id}/jsonld` | Terms by id, with their scheme and path; one term as a `DefinedTerm` |

Every call needs a signed-in caller in a tenant. The setting `Taxonomies:MaxPageSize` (default 100) caps the search
limit and the ids per lookup.

## The client and the pick-terms dialog

The **Taxonomies** menu entry opens the scheme list and the scheme editor (`/taxonomies/scheme?key=`): the tree with
up, down, indent and outdent moves, a side panel to edit a term and choose its concept (with the Keywords module's
`pick-concepts` dialog when it is installed), and the scheme's JSON-LD.

Other client modules open the picker through [module dialogs](/guides/module-dialogs/):

```csharp
if (ModuleDialogs.IsAvailable("Module.Taxonomies/pick-terms"))
{
    var result = await ModuleDialogs.ShowAsync("Module.Taxonomies/pick-terms",
        new { multiple = true, selected = new[] { "3" }, scheme = "news-sections" });
    // result.Data: { ids: ["3"], terms: [{ id, name, path, scheme, conceptId }] }
}
```

Keep the id and the name or path: they are a snapshot for display, the id is the link.

## Developing

The playground (`dotnet run --project playground --launch-profile dev`) runs the platform with the module and grants
`taxonomies.*` to `acme-admin@modular.local` and `taxonomies.read` to `editor@modular.local` through `Access:DevGrants`.
`--Playground:Keywords=<path>/Module.Keywords` loads the Keywords module beside it, to try terms with concepts. See
[Create a module, A to Z](/backend/create-a-module/) for the playground and install steps.

---
title: Cheat sheet
description: Astro, Blazor and MudTail equivalents on one page.
sidebar:
  order: 1
---

## Concepts

| Astro | Blazor (this project) | MudTail |
| --- | --- | --- |
| `.astro` component | `.razor` component | CSS classes + `data-mud` |
| Layout (`<slot />`) | `LayoutComponentBase` + `[LayoutKey]` (`@Body`); a page picks one with `[UseLayout]` ([layouts](/guides/layouts/)) | `mud-site`, `mud-site-header`, `mud-site-footer` |
| Page in `src/pages/` | `@page` in a module | — |
| Island + `client:*` | `RegisterForJavaScript` + `<BlazorIsland load=…>` | `autoInit(root)` |
| `<script>` in a component | `js/module.js` of a module (`moduleHost`) | `registerBehavior(name, factory)` |
| Content collections | API + module data | — |
| i18n routing (`/ar/…`) | `CultureInfo`, `IStringLocalizer`, `i18n/{culture}.json`, `ILocaleService` ([languages](/guides/i18n/)) | `setLocale`, `t`, `translateElement` |
| Sidebar / nav config | `[MenuLink]`, `[MenuGroup]`, `<MenuZone>`, `IMenuBadges` ([menus](/guides/menus/)) | — |
| Named slots in a layout | `<PageHeader>`, `<SectionContent SectionName="@LayoutSections.…">` ([layouts](/guides/layouts/#the-pages-own-header)) | — |
| — | `[HeaderAction]`, `[Dock]`, `[ContextPanel]`, `IPageContext`, `IBranding`, `IUserPermissions` ([layouts](/guides/layouts/#what-modules-add-to-the-shell)) | — |
| `dir="rtl"` on `<html>` | same | follows `dir`, logical properties |
| — | `[ClientDialog("Module.X/name")]`, `IModuleDialogs.ShowAsync` ([dialogs](/guides/module-dialogs/)) | `mud-dialog-*` frame |

## Island loading

| Astro | `<BlazorIsland>` | Starts |
| --- | --- | --- |
| `client:load` | `load="eager"` | immediately |
| `client:idle` | `load="idle"` | when the browser is idle |
| `client:visible` | `load="visible"` | when it scrolls into view (200px margin) |
| `client:media="q"` | `load="media" media="q"` | when the query matches |
| `client:only` | empty slot | immediately, no fallback |

## Blazor JavaScript APIs used here

| API | Where | Purpose |
| --- | --- | --- |
| `builder.RootComponents.RegisterForJavaScript<T>("name")` | islands host `Program.cs` | expose a component to JS |
| `<script autostart="false">` + `Blazor.start({ loadBootResource })` | `src/lib/blazor.ts` | start on demand, from `/islands/` |
| `Blazor.rootComponents.add(el, "name", params)` | `<BlazorIsland>` | mount; returns `setParameters` and `dispose` |

## MudTail exports used in Astro

| Export | Use |
| --- | --- |
| `mudtail/mudtail.css` | styles and tokens |
| `mudtail/tailwind.css` | Tailwind v4 preset |
| `initTheme()`, `updateTheme()` | saved theme, shared with Blazor on one origin |
| `autoInit(document.body)` | all `data-mud` behaviours |
| `setLocale('ar')`, `setDirection('rtl', 'ar')` | language and direction |
| `MudTabs`, `MudDataTable`, `MudDialog`, … | components built in JS |

## Paths

| Path | Served by |
| --- | --- |
| `/`, `/news/…`, `/_astro/…` | Astro build |
| `/app/…` | Blazor client (SPA fallback to `/app/index.html`) |
| `/islands/…` | islands host (optional) |
| `http://localhost:5080/api/…` | WebApi gateway (module files: `api/client-modules/files/…`) |
| `http://localhost:5090/connect/…` | identity service (OpenID Connect) |

## Keeping backend modules separable

Check these before merging a module change, so the module can later move to its own pool
([deployment scenarios](/backend/deployment-scenarios/#build-for-b-deploy-as-a)):

| Rule | Check |
| --- | --- |
| Contracts only | References other modules only through their `*.Contracts` assemblies (architecture test in CI) |
| Own data | Own schema and `DbContext`; no joins or transactions across modules |
| Remote-ready calls | Async signatures, DTOs only, no entities or shared state across the contract |
| Reliable messages | State changes publish through the outbox; consumers are idempotent (inbox) |
| Real broker | Messages go through the broker even when both modules share a process |
| Tenant everywhere | Tenant id in gRPC metadata and message headers |
| Nothing local | No in-memory caches, timers or files that another replica would need |
| Endpoints | `ICarterModule` classes; nothing registered only by startup scanning |

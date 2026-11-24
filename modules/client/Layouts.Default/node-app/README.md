# Layouts.Default assets

This folder builds the Default layout module's `wwwroot`. The `.csproj` embeds that `wwwroot` into the module DLL. The module provides two layouts:

| Layout key | Component | For |
| --- | --- | --- |
| `default` | `DefaultLayout.razor` | the application shell (2.0): nav drawer, toolbar, context panel, dock bar and theme drawer (`Shell/`, `_shell.css`) |
| `public` | `PublicLayout.razor` | the public site (news, sport, culture): masthead, mega-nav and footer, built entirely from MudTail's `mud-site` classes |

A module's pages use the public layout when its settings say `Layout = "public"`.

| Source | Output | What it is |
| --- | --- | --- |
| `src/framework/main.css` | `../wwwroot/css/main.min.css` | The layouts' own shell (`_shell.css`), on top of UI.MudTail's stylesheet |

The MudTail runtime, its stylesheet and the `autoInit` behaviours live in the **UI.MudTail** module, which this module
depends on (`Dependencies` in `ModuleInfo.cs`). The host activates UI.MudTail first, so its stylesheet comes before
this one. This module has no script of its own.

## The application shell (2.0)

`DefaultLayout.razor` places the parts in `Shell/`. Modules fill it through metadata only, so it shows their entries
before their code is downloaded:

| Part | Filled by | Notes |
| --- | --- | --- |
| Nav drawer (`NavTree`) | the `main` and `secondary` menu zones (`MenuLink` / `MenuGroup`) | `Icon` is a Lucide name from UI.MudTail's sprite; without one (or an unknown one) the entry shows its first letter. Groups collapse and the group holding the current page opens by itself. |
| Toolbar | `LayoutSections` (`<PageHeader>`), `[HeaderAction]` | toggle, breadcrumbs and title, page actions, header actions, context toggle, theme, account menu (the `user` zone, language, sign out) |
| Context panel (`ContextPanel`) | `LayoutSections.ContextPanel`, `[ContextPanel]` | the page's own content first, then a tab per panel that applies to the route or to a key the page set in `IPageContext` |
| Dock bar (`DockBar`) | `[Dock]` | a tab per dock; the module loads when its dock first opens, and an opened dock stays rendered so it keeps its state across pages |
| Theme drawer (`ThemeDrawer`) | the layout itself | mode (light, dark, system), colours and corners when the brand allows them, density, digits |

Every contribution renders inside an `ErrorBoundary` (`ContributionHost`), so a failing module shows "unavailable"
instead of breaking the shell.

| Width | Nav drawer | Context panel |
| --- | --- | --- |
| below 1024px | overlay from the inline start | sheet from the inline end |
| 1024–1279px | icon rail (mini) unless the user expanded it | sheet |
| 1280px and up | expanded unless the user minimised it | inline |

In mini mode the titles are CSS tooltips and an open group shows its entries as a flyout beside the rail. The layout
learns the width through UI.MudTail's `watchMedia`; the stylesheet's media queries make the first paint right anyway.
The user's choices (drawer size, open groups, context panel, density, pinned dock) are kept in localStorage
(`mb.shell`); colours and mode are MudTail's theme (`mud_vanilla_theme_config`, `mode` included).

## Public layout navigation

The mega-nav is built from the module menu (`IModuleNavigation.Items`) by URL, in `PublicNavigation.cs`. An item whose href is `sports/football` goes into the panel of the item whose href is `sports`. Items without such a parent stay at the top level.

## One token set

Every colour, radius and z-index comes from MudTail's `--mud-palette-*` tokens, loaded by UI.MudTail. This folder
defines no palette of its own; it emits only the Tailwind utilities its own Razor markup uses. `_shell.css`
is imported **unlayered**, like MudTail's CSS, so plain host rules don't override it.

## RTL

Both layouts use logical properties only. With `dir="rtl"` on `<html>`, everything mirrors. MudTail's `setDirection()` switches it at runtime.

## Build

```
npm install
npm run build      # builds MudTail first, then this module's wwwroot
npm run dev        # rebuild on change (MudTail must already be built)
```

The stylesheet is declared in `ModuleInfo.cs` (`Styles = css/main.min.css`). Seeding a new release adds a core
module's new dependencies to existing installations and keeps the admin's other settings.

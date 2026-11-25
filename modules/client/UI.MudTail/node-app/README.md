# UI.MudTail assets

This folder builds the UI.MudTail module's `wwwroot`. The `.csproj` embeds that `wwwroot` into the module DLL, and the
WebApi serves each file from a real URL (`api/client-modules/files/{id}/{version}/{token}/...`) with an integrity hash.

UI.MudTail is a hidden startup module with no pages. It holds the one copy of the MudTail runtime and stylesheet that
every layout and module shares, so a layout only adds its own shell on top.

| Source | Output | What it is |
| --- | --- | --- |
| `src/framework/mudtail.css` | `../wwwroot/css/mudtail.min.css` | Tailwind utilities (no preflight), the MudTail preset, MudTail's core CSS and `_host.css`. The site pack (editorial, sport, gallery, the `mud-site` shell) is Layouts.Public's |
| `src/framework/packs/*.css` | `../wwwroot/css/packs/*.min.css` | The stylesheets of `dock`, `tree`, `image-editor`, `explorer`, `designer`, `editorial` and `monaco`, added when the component is first used |
| `src/js/module.ts` | `../wwwroot/js/module.js` + `js/chunks/` | On activation: applies the saved MudTail theme and starts `MudTail.autoInit(document.body)`. Returns the MudTail API. The large components (dock, tree, image editor, explorer, the design canvas, the code editor and Monaco itself) are chunks it imports on first use; a few chunks (`i18n`, `icons`, …) it shares with them and imports up front |

| MudTail's `dist/monaco/` | `../wwwroot/js/monaco/` | Monaco's web workers (editor, JSON, CSS, HTML), copied as they are. `module.ts` points MudTail at them (`setMonacoBase`) |

The build empties `wwwroot/js/chunks` and `wwwroot/js/monaco` first, since the chunk names carry hashes and old ones would be embedded in the
package.

## Code loaded on first use

`create('dock' | 'tree' | 'image-editor' | 'explorer', …)` and `load(area)` import the chunk and add the pack's
`<link>` from `css/packs/` next to the module's stylesheet. Offline, when the host runs the package from memory,
moduleHost turns the chunks into blob urls as well and rewrites the imports to them (the host passes the package's
other scripts with `module.js`). The packs are not loaded then: a component used for the first time offline has its
code but not its stylesheet. Monaco's workers aren't either, so an editor opened offline edits and colours its text
but doesn't validate it.

The Content Designer's packs are on-demand like the rest:
- `create('canvas', …)` loads the `designer` and `editorial` packs;
- `create('monaco', …)` loads the `monaco` pack. Its first editor imports Monaco (about 4 MB, 1 MB gzipped) and
  starts its workers from `js/monaco/`, through a same-origin blob, since the WebApi is another origin;
- pages that only render `data-mb-*` tokens call `loadPack('editorial')`.

Users who never open a designer download none of it.

## Shell helpers

Besides MudTail's own interop, the API has what a layout needs without a script of its own:

| Member | What it does |
| --- | --- |
| `iconNames()` | the names in the icon sprite (`src/js/icons.ts`, a short list of Lucide icons plus a few aliases such as `chat` and `tasks`) |
| `addIcons({ name: lucideNode \| '<path …/>' })` | adds icons to the sprite, for a module's own |
| `mode()`, `setMode('light' \| 'dark' \| 'system')` | the colour mode; `system` follows the operating system and is saved as `mode` in MudTail's theme |
| `watchMedia(queries, dotNetRef)` | calls `OnMedia(bool[])` now and whenever a media query changes; returns a handle with `dispose()` |

The sprite is an inline `<svg>` at the top of `<body>`; draw an icon with `<svg><use href="#mt-i-users"/></svg>`.

## Using the API

C# code uses `MudTailRuntime` (ModularBlazor.Abstractions), which reads the API with `moduleHost.get('UI.MudTail')`.
`moduleHost.get('Layouts.Default')` still returns the same object, for modules written against Layouts.Default 1.x.

## Behaviours without interop

Because `module.js` runs `autoInit` on `<body>`, any module's Razor page gets the interactive components just by
rendering the markup: a ticker (`data-mud="ticker"`), live scores (`data-mud="score-strip"`), a gallery lightbox
(`data-mud="lightbox"`), scroll reveal (`data-mud-reveal`), `mega-nav`, `reading-progress` and `theme-toggle`. The
markup and rules are in `src/MudTail/README.md`.

## One token set

Every colour, radius and z-index comes from MudTail's `--mud-palette-*` tokens (`src/MudTail`, a `file:` dependency).
`_host.css` points the host's own `app.css` variables (`--bg`, `--accent`, …) at those tokens, so host pages follow the
same theme, dark mode included, whatever layout is active.

MudTail's CSS and `_host.css` are imported **unlayered** on purpose. The host's `app.css` is unlayered, and unlayered
CSS always beats layered CSS. If they were layered, plain host rules such as `a { color }` would override MudTail's
components.

## Load order

A layout or module that uses MudTail classes or the API declares `Dependencies = new[] { "UI.MudTail" }` in its
`[assembly: ClientModule]`. The host then activates UI.MudTail first, so its stylesheet comes before the layout's.

## Build

```
npm install
npm run build      # builds MudTail first, then this module's wwwroot
npm run dev        # rebuild on change (MudTail must already be built)
```

`preserveEntrySignatures` in `vite.config.ts` keeps `module.js`'s exports; without it the module tree-shakes down to
nothing.

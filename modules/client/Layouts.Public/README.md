# Layouts.Public

The public site's layout (layout key `public`): masthead, mega-nav, reading progress and footer, built from MudTail's
`mud-site` classes. Its one asset is `css/site.min.css`, MudTail's **site pack** (editorial, sports, gallery and the
`mud-site` shell), which UI.MudTail's core stylesheet leaves out. The tokens, components and `data-mud` behaviours
(`mega-nav`, `reading-progress`, `theme-toggle`) come with **UI.MudTail**, which it depends on.

`node-app` copies the pack: `npm run build` there builds MudTail and copies `src/MudTail/dist/packs/site.css` to
`wwwroot/css/site.min.css` (`npm run copy` copies only). A module page that uses the editorial or sport classes
outside this layout can list the same file in its own `Styles`.

It is not a startup module. A page asks for the layout with `[UseLayout("public")]`, or its module's settings say
`Layout = "public"`; the host sees in the manifest that this module provides `public` (`ProvidesLayouts`), loads it,
and only then renders the page. Tenants with no public pages never download it.

## Sections

The mega-nav and the footer's section list come from the **`public` menu zone** (`MenuZones.Public`):

```csharp
[assembly: MenuGroup("sport", "Sport", Zone = "public", Href = "sport", Order = 20)]
[assembly: MenuLink("football", "Football", "sport/football", Zone = "public", Parent = "sport")]
```

A top-level entry is a section; its links fill the section's panel, and a group inside a section becomes a column
with its own heading. Permissions, ordering, translations and badges work as in any menu. When no module fills the
`public` zone, the layout shows the `main` zone instead.

The logo and the name in the footer come from the tenant's brand (`IBranding`).

# Layouts.Public

The public site's layout (layout key `public`): masthead, mega-nav, reading progress and footer, built from MudTail's
`mud-site` classes. It has no assets of its own: the classes and the `data-mud` behaviours (`mega-nav`,
`reading-progress`, `theme-toggle`) come with **UI.MudTail**, which it depends on.

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

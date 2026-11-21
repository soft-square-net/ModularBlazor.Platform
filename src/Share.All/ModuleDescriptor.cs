using System.ComponentModel.DataAnnotations;

namespace soft.square.net.Share.All;

public enum ModuleKind
{
    /// <summary>A single assembly served as a file.</summary>
    Dll,

    /// <summary>A NuGet package; everything under lib/&lt;tfm&gt;/ is loaded.</summary>
    NuPkg
}

/// <summary>
/// A menu entry contributed by a module. Href is relative to the app base. <see cref="Titles"/> holds the title in other
/// languages (culture → text, read from the module's i18n files when it is uploaded); <see cref="Title"/> is the English one.
/// </summary>
public sealed record ModuleNavItem(string Title, string Href, int Order = 100, IReadOnlyDictionary<string, string>? Titles = null)
{
    /// <summary>The title in the current UI culture (ar-EG, then ar), else <see cref="Title"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string LocalTitle => TitleIn(System.Globalization.CultureInfo.CurrentUICulture);

    public string TitleIn(System.Globalization.CultureInfo culture)
    {
        if (Titles is { Count: > 0 })
            for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
                if (Titles.TryGetValue(c.Name, out var title) && !string.IsNullOrWhiteSpace(title)) return title;
        return Title;
    }
}

/// <summary>Metadata the host needs to know about a module before its code is downloaded.</summary>
public sealed class ModuleDescriptor
{
    [Key]
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public ModuleKind Kind { get; set; } = ModuleKind.Dll;

    /// <summary>Where to download the .dll / .nupkg from (relative to the app base or absolute).</summary>
    public string Url { get; set; } = "";

    /// <summary>Optional hex SHA-256 of the downloaded file; the load fails if it does not match.</summary>
    public string? Sha256 { get; set; }

    /// <summary>True: loaded before the app starts. False: loaded the first time a route needs it.</summary>
    public bool LoadOnStartup { get; set; }

    /// <summary>Hide the module's nav items (layout-only modules, plumbing).</summary>
    public bool Hidden { get; set; }

    public bool RequiresAuth { get; set; }
    public string? RequiredRole { get; set; }

    /// <summary>Base-relative URL prefixes (no leading slash) that trigger loading, e.g. "reports".</summary>
    public string[] RoutePrefixes { get; set; } = [];

    /// <summary>Layout key used for this module's pages (see LayoutKeyAttribute).</summary>
    public string? Layout { get; set; }

    /// <summary>Ids of modules that must be loaded first (layout modules, shared libraries).</summary>
    public string[] Dependencies { get; set; } = [];

    /// <summary>Extra private assemblies (urls) for a Dll module that has its own dependencies.</summary>
    public string[] ExtraAssemblies { get; set; } = [];

    /// <summary>
    /// Stylesheets. Dll modules: urls. NuPkg modules: paths inside the package's staticwebassets/ folder.
    /// </summary>
    public string[] Styles { get; set; } = [];
    public string[] Scripts { get; set; } = [];

    public ModuleNavItem[] Nav { get; set; } = [];

    /// <summary>The module's menu groups and links (its MenuGroup and MenuLink attributes), in every zone.</summary>
    public MenuItem[] Menu { get; set; } = [];
}

public sealed class ModuleManifest
{
    public List<ModuleDescriptor> Modules { get; set; } = [];
}

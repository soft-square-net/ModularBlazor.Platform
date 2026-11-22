namespace soft.square.net.ModularBlazor.Abstractions;

/// <summary>
/// Put on a LayoutComponentBase in any module to register it under a key.
/// Usage in Razor: @attribute [LayoutKey("default")]
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class LayoutKeyAttribute(string key) : Attribute
{
  public string Key { get; } = key;
}

/// <summary>
/// Put on a page to pick a registered layout by key, overriding the module's default layout.
/// Usage in Razor: @attribute [UseLayout("admin")]  (use "none" for no layout)
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class UseLayoutAttribute(string key) : Attribute
{
  public string Key { get; } = key;
}

/// <summary>
/// The default settings of a client module (or of a module that only provides layouts), written once in the module itself:
/// <c>[assembly: ClientModule(Name = "Reports", RoutePrefixes = new[] { "reports" }, NavTitle = "Reports", NavHref = "reports")]</c>.
/// When an administrator uploads the file, the Module Management page reads this (from the assembly metadata, no code is run) and
/// pre-fills the module settings page. Everything is optional; routes are found from the pages' @page directives.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ClientModuleAttribute : Attribute
{
  /// <summary>Catalog id, e.g. "Module.Reports". Default: the assembly name without the "soft.square.net." prefix.</summary>
  public string? Id { get; set; }
  public string? Name { get; set; }

  /// <summary>Core module: downloaded at startup, before sign-in. Default false: a user module, downloaded on navigation.</summary>
  public bool LoadOnStartup { get; set; }

  public bool Hidden { get; set; }

  /// <summary>Default true.</summary>
  public bool RequiresAuth { get; set; } = true;

  public string? RequiredRole { get; set; }

  /// <summary>Default layout key (see <see cref="LayoutKeyAttribute"/>) for the module's pages.</summary>
  public string? Layout { get; set; }

  public string[] AvailableToRoles { get; set; } = [];

  /// <summary>Default: the first segment of every page route.</summary>
  public string[] RoutePrefixes { get; set; } = [];

  /// <summary>Client modules (ids) that must be loaded first, e.g. the layout module.</summary>
  public string[] Dependencies { get; set; } = [];

  public string[] Styles { get; set; } = [];
  public string[] Scripts { get; set; } = [];

  /// <summary>Host modules this module needs: "Package@Version" or "Package@Version|https://source/file.nupkg".</summary>
  public string[] HostDependencies { get; set; } = [];

  public string? NavTitle { get; set; }
  public string? NavHref { get; set; }
  public int NavOrder { get; set; } = 100;
}

/// <summary>
/// A link the module adds to a menu, read from its metadata when it is uploaded (no code is run), so it is in the menu
/// before the module is downloaded and leaves it when the module is unloaded or deactivated:
/// <c>[assembly: MenuLink("reports", "Reports", "reports", Zone = "main", Parent = "insights", Order = 20)]</c>.
/// The title is English; the module's i18n files translate it like any other text.
/// </summary>
/// <param name="id">Local to the module (letters, digits, '-', '_', '.'); the menu knows it as "{moduleId}:{id}".</param>
/// <param name="href">A page of this module, relative to the app base (under its route prefixes).</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MenuLinkAttribute(string id, string title, string href) : Attribute
{
  public string Id { get; } = id;
  public string Title { get; } = title;
  public string Href { get; } = href;

  /// <summary>"main" (default), "secondary" or "user" (see MenuZones).</summary>
  public string Zone { get; set; } = "main";

  /// <summary>The group it goes in: a local id ("insights") or another module's ("Module.Admin:admin").</summary>
  public string? Parent { get; set; }

  public string? Icon { get; set; }
  public int Order { get; set; } = 100;

  /// <summary>
  /// The module's own API url ("api/content/posts/review-count") the host asks for the entry's badge, with the user's
  /// token: a number, an array (its length) or { count, text, tone, label } (see MenuBadge). Works before the module's
  /// code is downloaded.
  /// </summary>
  public string? Badge { get; set; }

  /// <summary>Seconds between two asks for the badge (15-3600; default 60).</summary>
  public int BadgeRefresh { get; set; }
}

/// <summary>
/// A group (submenu) the module adds to a menu; links and groups of this or other modules go in it with Parent.
/// <c>[assembly: MenuGroup("insights", "Insights", Order = 40)]</c>. A group with no link in it is not shown, unless it
/// links somewhere itself (Href).
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class MenuGroupAttribute(string id, string title) : Attribute
{
  public string Id { get; } = id;
  public string Title { get; } = title;

  /// <summary>Optional: a page of this module the group's heading links to.</summary>
  public string? Href { get; set; }

  public string Zone { get; set; } = "main";
  public string? Parent { get; set; }
  public string? Icon { get; set; }
  public int Order { get; set; } = 100;

  /// <summary>An API url for the group's badge, like <see cref="MenuLinkAttribute.Badge"/>.</summary>
  public string? Badge { get; set; }

  public int BadgeRefresh { get; set; }
}

/// <summary>
/// The default settings of a host (API) module, written once in the module itself:
/// <c>[assembly: HostModule(Name = "Billing", DefaultConfiguration = "{\"Currency\":\"EUR\"}")]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class HostModuleAttribute : Attribute
{
  public string? Name { get; set; }

  /// <summary>A JSON object injected as configuration section Modules:&lt;Name&gt;.</summary>
  public string? DefaultConfiguration { get; set; }
}

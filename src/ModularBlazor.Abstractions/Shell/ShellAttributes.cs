namespace soft.square.net.ModularBlazor.Abstractions.Shell;

/// <summary>
/// What the shell contribution attributes share. Read from the module's metadata when it is uploaded (no code is run),
/// so the layout knows the entry before the module is downloaded. The id is local to the module (letters, digits, '-',
/// '_', '.'); the layout knows it as "{moduleId}:{id}". The title is English; the module's i18n files translate it.
/// </summary>
public abstract class ShellContributionAttribute(string id, string title, Type component) : Attribute
{
  public string Id { get; } = id;
  public string Title { get; } = title;

  /// <summary>A component of the module's own assembly. It may take a <c>[Parameter] ContributionContext Context</c>.</summary>
  public Type Component { get; } = component;

  /// <summary>An icon name (Lucide, e.g. "messages-square").</summary>
  public string? Icon { get; set; }

  /// <summary>Lower first; ties go by module id, then id.</summary>
  public int Order { get; set; } = 100;

  /// <summary>Permissions (any one of them) the user needs to see it. None: everyone who gets the module.</summary>
  public string[] Permissions { get; set; } = [];
}

/// <summary>
/// A small component in the layout's top bar, before the user menu:
/// <c>[assembly: HeaderAction("bell", "Notifications", typeof(NotificationBell), Icon = "bell", Order = 10)]</c>.
/// It renders once the module is loaded, so a module that contributes one usually loads with the Preload strategy.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class HeaderActionAttribute(string id, string title, Type component) : ShellContributionAttribute(id, title, component);

/// <summary>
/// A panel the layout opens from its footer and keeps open across pages (chat, tasks, notes):
/// <c>[assembly: Dock("chat", "Chat", typeof(ChatDock), Icon = "messages-square", Badge = "api/chat/unread")]</c>.
/// Its tab and badge show before the module is downloaded; the module is loaded when the dock is first opened.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class DockAttribute(string id, string title, Type component) : ShellContributionAttribute(id, title, component)
{
  /// <summary>The module's own API url ("api/...") for the tab's badge, like <see cref="MenuLinkAttribute.Badge"/>.</summary>
  public string? Badge { get; set; }

  /// <summary>Seconds between two asks for the badge (15-3600; default 60).</summary>
  public int BadgeRefresh { get; set; }
}

/// <summary>
/// A panel beside the page, for some pages or for the item a page has selected, so a module can add to another
/// module's pages without referencing it:
/// <c>[assembly: ContextPanel("comments", "Comments", typeof(PostComments), ForContext = new[] { "content.post" })]</c>.
/// It is shown when its route matches (<see cref="ForRoutes"/>) or the page has set one of its context keys
/// (<see cref="ForContext"/>, see <see cref="IPageContext"/>). Its tab shows before the module is downloaded.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ContextPanelAttribute(string id, string title, Type component) : ShellContributionAttribute(id, title, component)
{
  /// <summary>Base-relative page paths: "content/posts/*" ('*' one segment), "content/**" (everything under content).</summary>
  public string[] ForRoutes { get; set; } = [];

  /// <summary>Page-context keys, "{module}.{entity}" (e.g. "content.post"); the owning module declares them as constants.</summary>
  public string[] ForContext { get; set; } = [];

  /// <summary>An API url for the tab's badge, like <see cref="DockAttribute.Badge"/>.</summary>
  public string? Badge { get; set; }

  public int BadgeRefresh { get; set; }
}

/// <summary>
/// What a layout offers, read from its metadata when the module is uploaded, so the catalog can warn about a module
/// that fills a place no installed layout has. Put it next to [LayoutKey]:
/// <c>@attribute [LayoutInfo(LayoutRoles.App, Zones = new[] { "main", "secondary", "user" }, Contributions = new[] { "header-action", "dock" })]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class LayoutInfoAttribute(string role) : Attribute
{
  /// <summary>One of <see cref="LayoutRoles"/>.</summary>
  public string Role { get; } = role;

  /// <summary>The <see cref="LayoutSections"/> the layout places an outlet for.</summary>
  public string[] Sections { get; set; } = [];

  /// <summary>The menu zones it shows (MenuZones).</summary>
  public string[] Zones { get; set; } = [];

  /// <summary>The kinds of module contributions it shows (ShellContributionKinds).</summary>
  public string[] Contributions { get; set; } = [];
}

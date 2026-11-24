namespace soft.square.net.Share.All;

/// <summary>
/// Where a module may add to the layout besides the menus (MenuLink / MenuGroup): the kinds of
/// <see cref="ShellContribution"/>. A layout that has no place for a kind does not show it.
/// </summary>
public static class ShellContributionKinds
{
    /// <summary>A small component in the layout's top bar, before the user menu (a notification bell, a search box).</summary>
    public const string HeaderAction = "header-action";

    /// <summary>A panel opened from the layout's footer (chat, tasks, notes); it stays open across pages.</summary>
    public const string Dock = "dock";

    /// <summary>A panel beside the page, shown for some pages or for the item a page has selected (comments, workflow).</summary>
    public const string ContextPanel = "context-panel";

    public static readonly IReadOnlyList<string> All = [HeaderAction, Dock, ContextPanel];
}

/// <summary>
/// A component a module adds to the layout (<c>[assembly: HeaderAction]</c>, <c>[assembly: Dock]</c>,
/// <c>[assembly: ContextPanel]</c>), read from its metadata when it is uploaded. Like the menus, it is known before the
/// module's code is downloaded: the layout can show its tab, icon and badge, and the module is loaded when the
/// component is first needed. It goes when the module is unloaded or deactivated.
/// </summary>
/// <param name="Id">Unique across modules: "{moduleId}:{localId}", e.g. "Module.Chat:chat".</param>
/// <param name="Kind">One of <see cref="ShellContributionKinds"/>.</param>
/// <param name="Title">The English title (a tab, a tooltip, an accessible name); <see cref="Titles"/> holds the other languages.</param>
/// <param name="Component">The full name of the component type in the module's own assembly.</param>
/// <param name="Icon">An icon name (Lucide) for layouts that show icons.</param>
/// <param name="Permissions">The user needs any one of them to see it (none: everyone who gets the module).</param>
/// <param name="Badge">An API url ("api/...") the host asks for the entry's badge (see <see cref="MenuBadge"/>).</param>
/// <param name="BadgeRefresh">Seconds between two asks; 0: the default (60).</param>
/// <param name="ForRoutes">Context panels: base-relative page paths it is shown on; '*' matches one segment, a trailing "/**" any rest.</param>
/// <param name="ForContext">Context panels: page-context keys (IPageContext) it is shown for, e.g. "content.post".</param>
public sealed record ShellContribution(
    string Id,
    string Kind,
    string Title,
    string Component,
    string? Icon = null,
    int Order = 100,
    IReadOnlyDictionary<string, string>? Titles = null,
    IReadOnlyList<string>? Permissions = null,
    string? Badge = null,
    int BadgeRefresh = 0,
    IReadOnlyList<string>? ForRoutes = null,
    IReadOnlyList<string>? ForContext = null)
{
    /// <summary>The module that declares it: the part of <see cref="Id"/> before ':'.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ModuleId => Id.Split(':', 2)[0];

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

    /// <summary>
    /// Whether a context panel belongs on the page at <paramref name="path"/> (base-relative, no query) whose context
    /// holds <paramref name="contextKeys"/>: its route matches, or one of its context keys is set. Other kinds: always.
    /// </summary>
    public bool AppliesTo(string path, IEnumerable<string> contextKeys)
    {
        if (Kind != ShellContributionKinds.ContextPanel) return true;
        if (ForContext is { Count: > 0 } keys && contextKeys.Any(k => keys.Contains(k, StringComparer.OrdinalIgnoreCase))) return true;
        return ForRoutes is { Count: > 0 } routes && routes.Any(r => RouteMatches(r, path));
    }

    /// <summary>"content/posts/*" matches "content/posts/12"; "content/**" matches "content" and everything under it.</summary>
    public static bool RouteMatches(string pattern, string path)
    {
        var p = pattern.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var s = path.Split('?', '#')[0].Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < p.Length; i++)
        {
            if (p[i] == "**") return i == p.Length - 1;
            if (i >= s.Length) return false;
            if (p[i] != "*" && !p[i].Equals(s[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return p.Length == s.Length;
    }
}

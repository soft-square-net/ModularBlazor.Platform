namespace soft.square.net.Share.All;

/// <summary>The menus (zones) every layout can show. A module may name another zone; a layout that has no such zone ignores it.</summary>
public static class MenuZones
{
    /// <summary>The main navigation: the modules' pages.</summary>
    public const string Main = "main";

    /// <summary>A second, smaller menu: settings, administration, help.</summary>
    public const string Secondary = "secondary";

    /// <summary>The signed-in user's menu: profile, preferences, sign out.</summary>
    public const string User = "user";

    public static readonly IReadOnlyList<string> All = [Main, Secondary, User];
}

/// <summary>The kinds of <see cref="MenuItem"/>.</summary>
public static class MenuItemKinds
{
    /// <summary>A heading with children; it may also link somewhere itself (<see cref="MenuItem.Href"/>).</summary>
    public const string Group = "group";

    public const string Link = "link";
}

/// <summary>
/// One entry of a menu, declared by a module (<c>[assembly: MenuLink(...)]</c>, <c>[assembly: MenuGroup(...)]</c>) and read
/// from its metadata when it is uploaded, so the menus are known before its code is downloaded.
/// </summary>
/// <param name="Id">Unique across modules: "{moduleId}:{localId}", e.g. "Module.Admin:admin".</param>
/// <param name="Kind"><see cref="MenuItemKinds.Group"/> or <see cref="MenuItemKinds.Link"/>.</param>
/// <param name="Zone">The menu it goes in (<see cref="MenuZones"/>). A child is shown in its parent's zone.</param>
/// <param name="Title">The English title; <see cref="Titles"/> holds the other languages (culture → text).</param>
/// <param name="Href">Relative to the app base (a page of the module), or an absolute http(s) url when <see cref="External"/>.</param>
/// <param name="Parent">The id of the group it is in, of this module or of another one; none: the zone's top level.</param>
/// <param name="Icon">An icon name for layouts that show icons.</param>
public sealed record MenuItem(
    string Id,
    string Kind,
    string Zone,
    string Title,
    string? Href = null,
    string? Parent = null,
    string? Icon = null,
    int Order = 100,
    IReadOnlyDictionary<string, string>? Titles = null,
    bool External = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsGroup => Kind == MenuItemKinds.Group;

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

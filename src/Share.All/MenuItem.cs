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
/// <param name="Badge">An API url ("api/...") the host asks for the entry's badge (see <see cref="MenuBadge"/>).</param>
/// <param name="BadgeRefresh">Seconds between two asks; 0: the default (60).</param>
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
    bool External = false,
    string? Badge = null,
    int BadgeRefresh = 0)
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

/// <summary>The look of a <see cref="MenuBadge"/>.</summary>
public static class MenuBadgeTones
{
    public const string Neutral = "neutral";
    public const string Info = "info";
    public const string Success = "success";
    public const string Warning = "warning";
    public const string Error = "error";

    public static readonly IReadOnlyList<string> All = [Neutral, Info, Success, Warning, Error];

    public static string Normalize(string? tone) =>
        All.FirstOrDefault(t => t.Equals(tone?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Info;
}

/// <summary>
/// A small mark next to a menu entry: a count ("3", "99+") or a short word ("new"), with a tone and an accessible label.
/// The host asks the entry's <see cref="MenuItem.Badge"/> url for it, which answers one of: a number (the count), an
/// array (its length is the count), or an object <c>{ "count": 3, "text": "new", "tone": "warning", "label": "…" }</c>.
/// A count of 0 and no text: no badge. A loaded module can also set one itself (IMenuBadges).
/// </summary>
/// <param name="Label">Read by screen readers and shown as a tooltip, e.g. "3 posts to review".</param>
public sealed record MenuBadge(string Text, string Tone = MenuBadgeTones.Info, string? Label = null)
{
    /// <summary>The badge for a count: none for 0 or less, "99+" above 99.</summary>
    public static MenuBadge? Count(int count, string tone = MenuBadgeTones.Info, string? label = null) =>
        count <= 0 ? null : new MenuBadge(count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture), MenuBadgeTones.Normalize(tone), label);

    /// <summary>Reads a badge url's answer (see the type's summary); null when it shows no badge or is not understood.</summary>
    public static MenuBadge? FromJson(System.Text.Json.JsonElement answer)
    {
        switch (answer.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Number when answer.TryGetInt32(out var number):
                return Count(number);
            case System.Text.Json.JsonValueKind.Array:
                return Count(answer.GetArrayLength());
            case System.Text.Json.JsonValueKind.Object:
                string? Text(string name) =>
                    answer.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: System.Text.Json.JsonValueKind.String } p
                        ? p.Value.GetString() : null;
                var tone = MenuBadgeTones.Normalize(Text("tone"));
                var label = Text("label") is { Length: > 0 and <= 200 } l ? l : null;
                if (Text("text") is { Length: > 0 } text)
                    return new MenuBadge(text.Length > 12 ? text[..12] : text, tone, label);
                var count = answer.EnumerateObject().FirstOrDefault(p => p.Name.Equals("count", StringComparison.OrdinalIgnoreCase));
                return count.Value.ValueKind == System.Text.Json.JsonValueKind.Number && count.Value.TryGetInt32(out var n) ? Count(n, tone, label) : null;
            default:
                return null;
        }
    }
}

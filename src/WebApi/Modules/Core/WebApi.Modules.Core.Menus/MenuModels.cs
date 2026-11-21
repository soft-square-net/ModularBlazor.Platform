namespace soft.square.net.WebApi.Modules.Core.Menus;

/// <summary>
/// A change to one menu entry, a module's ("Module.Admin:admin"), the app's own ("host:client-modules") or a link an
/// administrator added ("@platform:help"). Null fields keep what the entry has.
/// </summary>
/// <param name="Id">The entry's id.</param>
/// <param name="Hidden">True: left out of the menu, with everything in it.</param>
/// <param name="Zone">Another menu: "main", "secondary", "user" (or a zone a layout shows).</param>
/// <param name="Parent">Another group (its id); "" puts the entry at the top of its zone.</param>
/// <param name="Title">A new title, in the default text of the app (English); <paramref name="Titles"/> its translations.</param>
public sealed record MenuOverride(
    string Id,
    bool? Hidden = null,
    string? Zone = null,
    string? Parent = null,
    int? Order = null,
    string? Title = null,
    IReadOnlyDictionary<string, string>? Titles = null,
    string? Icon = null);

/// <summary>Who sees a link an administrator added.</summary>
public static class MenuAudiences
{
    public const string Everyone = "everyone";
    public const string SignedIn = "signed-in";
    public const string Anonymous = "anonymous";

    public static readonly IReadOnlyList<string> All = [Everyone, SignedIn, Anonymous];
}

/// <summary>
/// A group or link an administrator added. Its id is local to the layer it is in: the menu knows it as
/// "@platform:{id}" or "@tenant:{id}". A link goes to a page of the app ("contents/new") or anywhere (https://…).
/// </summary>
/// <param name="Parent">A group: "@platform:{id}", a module's ("Module.Admin:admin"), or a local id of this layer.</param>
/// <param name="Audience"><see cref="MenuAudiences"/>.</param>
public sealed record MenuCustomItem(
    string Id,
    string Kind,
    string Zone,
    string Title,
    IReadOnlyDictionary<string, string>? Titles = null,
    string? Href = null,
    string? Parent = null,
    string? Icon = null,
    int Order = 100,
    string Audience = MenuAudiences.Everyone);

/// <summary>One layer of changes: the platform's (every tenant) or one tenant's (on top of the platform's).</summary>
public sealed record MenuCustomization
{
    /// <summary>Raised on every save; a save must send the version it started from.</summary>
    public int Version { get; init; }

    public IReadOnlyList<MenuOverride> Overrides { get; init; } = [];
    public IReadOnlyList<MenuCustomItem> Items { get; init; } = [];
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}

/// <summary>A save: the whole layer, and the version it was read at (409 when it changed since).</summary>
public sealed record SaveMenuCustomization(int Version, IReadOnlyList<MenuOverride> Overrides, IReadOnlyList<MenuCustomItem> Items);

/// <summary>A layer for the admin page: the one being edited, and below it what it is applied on.</summary>
/// <param name="Scope">"platform" or the tenant id.</param>
/// <param name="Inherited">The platform's layer, when <paramref name="Scope"/> is a tenant.</param>
public sealed record MenuAdminView(string Scope, MenuCustomization Customization, MenuCustomization? Inherited);

/// <summary>
/// A link or group as the client shows it: an administrator's item with its full id ("@platform:help"), its parent
/// resolved, and whether it leaves the app.
/// </summary>
public sealed record MenuEntry(
    string Id,
    string Kind,
    string Zone,
    string Title,
    IReadOnlyDictionary<string, string>? Titles,
    string? Href,
    string? Parent,
    string? Icon,
    int Order,
    bool External);

/// <summary>
/// What the client applies to the modules' menus for the caller: the platform's changes, then the tenant's (one
/// override per entry, the tenant's fields winning), and the items both added that the caller may see.
/// </summary>
/// <param name="CanManage">The caller may change the menus here (the admin page).</param>
public sealed record EffectiveMenus(string ETag, IReadOnlyList<MenuOverride> Overrides, IReadOnlyList<MenuEntry> Items, bool CanManage);

/// <summary>A rule the menus refuse; <see cref="StatusCode"/> is the HTTP answer.</summary>
public sealed class MenuException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

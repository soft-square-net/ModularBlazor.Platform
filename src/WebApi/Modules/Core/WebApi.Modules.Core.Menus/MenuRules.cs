using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace soft.square.net.WebApi.Modules.Core.Menus;

/// <summary>The rules of a layer of menu changes (checked on save) and how the layers combine. Pure.</summary>
public static partial class MenuRules
{
    public const string PlatformLayer = "@platform";
    public const string TenantLayer = "@tenant";

    public const int MaxOverrides = 300;
    public const int MaxItems = 100;
    public const int MaxTitle = 100;
    public const int MaxHref = 2000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The full id of an item an administrator added in a layer.</summary>
    public static string Qualify(string layer, string localId) => $"{layer}:{localId}";

    /// <summary>
    /// Checks a save of one layer (<see cref="PlatformLayer"/> or <see cref="TenantLayer"/>) and returns it tidied:
    /// overrides that change nothing dropped, a parent given as a local id made full ("@tenant:news").
    /// </summary>
    public static (IReadOnlyList<MenuOverride> Overrides, IReadOnlyList<MenuCustomItem> Items) Validate(string layer, SaveMenuCustomization save)
    {
        var overrides = save.Overrides ?? [];
        var items = save.Items ?? [];
        if (overrides.Count > MaxOverrides) throw new MenuException($"At most {MaxOverrides} changed entries.");
        if (items.Count > MaxItems) throw new MenuException($"At most {MaxItems} added links and groups.");

        var tidyItems = new List<MenuCustomItem>();
        var localIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var id = item.Id?.Trim() ?? "";
            if (!LocalId().IsMatch(id)) throw new MenuException($"'{id}': an id is 1-64 letters, digits, '.', '_' or '-'.");
            if (!localIds.Add(id)) throw new MenuException($"Two added entries have the id '{id}'.");
        }
        var groups = items.Where(i => i.Kind == "group").Select(i => i.Id.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var id = item.Id.Trim();
            var kind = item.Kind?.Trim().ToLowerInvariant();
            if (kind is not ("group" or "link")) throw new MenuException($"'{id}': the kind is group or link.");
            var href = Href(id, item.Href, required: kind == "link");
            var audience = string.IsNullOrWhiteSpace(item.Audience) ? MenuAudiences.Everyone : item.Audience.Trim().ToLowerInvariant();
            if (!MenuAudiences.All.Contains(audience)) throw new MenuException($"'{id}': the audience is {string.Join(", ", MenuAudiences.All)}.");

            var parent = Blank(item.Parent);
            if (parent is not null && !parent.Contains(':'))
            {
                if (parent.Equals(id, StringComparison.OrdinalIgnoreCase)) throw new MenuException($"'{id}' cannot be in itself.");
                if (!groups.Contains(parent)) throw new MenuException($"'{id}': there is no added group '{parent}'.");
                parent = Qualify(layer, parent);
            }
            else if (parent is not null) RequireEntryId(id, parent);

            tidyItems.Add(new MenuCustomItem(id, kind, Zone(id, item.Zone) ?? "main", Title(id, item.Title)!, Titles(id, item.Titles), href,
                parent, Icon(id, item.Icon), Order(id, item.Order), audience));
        }

        var tidyOverrides = new List<MenuOverride>();
        var overridden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in overrides)
        {
            var id = o.Id?.Trim() ?? "";
            RequireEntryId("override", id);
            if (!overridden.Add(id)) throw new MenuException($"'{id}' is changed twice.");
            var parent = o.Parent is null ? null : o.Parent.Trim();
            if (parent is { Length: > 0 })
            {
                RequireEntryId(id, parent);
                if (parent.Equals(id, StringComparison.OrdinalIgnoreCase)) throw new MenuException($"'{id}' cannot be in itself.");
            }
            var tidy = new MenuOverride(id, o.Hidden, Zone(id, o.Zone), parent, o.Order is { } order ? Order(id, order) : null,
                o.Title is null ? null : Title(id, o.Title), Titles(id, o.Titles), Icon(id, o.Icon));
            if (tidy with { Id = "" } != new MenuOverride("")) tidyOverrides.Add(tidy);
        }
        return (tidyOverrides, tidyItems);
    }

    /// <summary>
    /// The changes the client applies for a caller: the platform's then the tenant's overrides (per entry, the tenant's
    /// set fields win), and the items of both layers the caller may see.
    /// </summary>
    public static EffectiveMenus Combine(MenuCustomization platform, MenuCustomization? tenant, bool signedIn, bool canManage)
    {
        var overrides = new Dictionary<string, MenuOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in platform.Overrides.Concat(tenant?.Overrides ?? []))
            overrides[o.Id] = overrides.TryGetValue(o.Id, out var below)
                ? new MenuOverride(o.Id, o.Hidden ?? below.Hidden, o.Zone ?? below.Zone, o.Parent ?? below.Parent, o.Order ?? below.Order,
                    o.Title ?? below.Title, o.Title is not null ? o.Titles : o.Titles ?? below.Titles, o.Icon ?? below.Icon)
                : o;

        bool Visible(MenuCustomItem i) => i.Audience switch
        {
            MenuAudiences.SignedIn => signedIn,
            MenuAudiences.Anonymous => !signedIn,
            _ => true
        };
        var items = platform.Items.Where(Visible).Select(i => Entry(PlatformLayer, i))
            .Concat((tenant?.Items ?? []).Where(Visible).Select(i => Entry(TenantLayer, i)))
            .ToList();

        var list = overrides.Values.OrderBy(o => o.Id, StringComparer.Ordinal).ToList();
        var etag = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { list, items, canManage }, Json)))[..32];
        return new EffectiveMenus(etag, list, items, canManage);
    }

    private static MenuEntry Entry(string layer, MenuCustomItem i) =>
        new(Qualify(layer, i.Id), i.Kind, i.Zone, i.Title, i.Titles, i.Href, i.Parent, i.Icon, i.Order, IsExternal(i.Href));

    public static bool IsExternal(string? href) =>
        href is not null && Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    // ---------------------------------------------------------------- fields

    /// <summary>A page of the app ("contents/new", no leading slash) or an http(s) address; nothing else (javascript:, //host).</summary>
    private static string? Href(string id, string? value, bool required)
    {
        var href = Blank(value);
        if (href is null)
            return required ? throw new MenuException($"'{id}': a link needs an address.") : null;
        if (href.Length > MaxHref) throw new MenuException($"'{id}': the address is longer than {MaxHref} characters.");
        if (IsExternal(href)) return href;
        if (href.StartsWith("//", StringComparison.Ordinal) || href.Contains('\\') || href.Any(char.IsControl)
            || href.Split(['/', '?', '#'], 2)[0].Contains(':'))
            throw new MenuException($"'{id}': '{href}' is neither a page of the app (such as contents/new) nor an http(s) address.");
        return href.TrimStart('/');
    }

    private static string? Zone(string id, string? value)
    {
        var zone = Blank(value)?.ToLowerInvariant();
        if (zone is not null && !LocalId().IsMatch(zone)) throw new MenuException($"'{id}': '{zone}' is not a menu.");
        return zone;
    }

    private static string? Title(string id, string? value)
    {
        var title = value?.Trim() ?? "";
        if (title.Length is 0 or > MaxTitle) throw new MenuException($"'{id}': a title is 1-{MaxTitle} characters.");
        return title;
    }

    private static IReadOnlyDictionary<string, string>? Titles(string id, IReadOnlyDictionary<string, string>? titles)
    {
        if (titles is null) return null;
        if (titles.Count > 20) throw new MenuException($"'{id}': at most 20 translations of a title.");
        var tidy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (culture, text) in titles)
        {
            if (!Culture().IsMatch(culture ?? "")) throw new MenuException($"'{id}': '{culture}' is not a language tag.");
            if (string.IsNullOrWhiteSpace(text)) continue;
            tidy[culture!] = Title(id, text)!;
        }
        return tidy.Count == 0 ? null : tidy;
    }

    private static string? Icon(string id, string? value)
    {
        var icon = Blank(value);
        if (icon is not null && !IconName().IsMatch(icon)) throw new MenuException($"'{id}': '{icon}' is not an icon name.");
        return icon;
    }

    private static int Order(string id, int order) =>
        order is >= -10000 and <= 10000 ? order : throw new MenuException($"'{id}': the order is between -10000 and 10000.");

    private static void RequireEntryId(string id, string entry)
    {
        if (!EntryId().IsMatch(entry)) throw new MenuException($"'{id}': '{entry}' is not a menu entry id.");
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex LocalId();

    /// <summary>"Module.Admin:admin", "host:client-modules", "@platform:help".</summary>
    [GeneratedRegex("^@?[A-Za-z0-9][A-Za-z0-9._-]{0,99}:[A-Za-z0-9][A-Za-z0-9._/-]{0,99}$")]
    private static partial Regex EntryId();

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex Culture();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,50}$")]
    private static partial Regex IconName();
}

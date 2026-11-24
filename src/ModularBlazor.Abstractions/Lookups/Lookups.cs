namespace soft.square.net.ModularBlazor.Abstractions.Lookups;

// The client side of the WebApi's lookups (key/text lists over modules' entities). The wire format matches the
// server's soft.square.net.WebApi.Contracts.Lookups types; see the docs page "Lookups".

/// <param name="Key">The stored value, as a string.</param>
public sealed record LookupItem(string Key, string Text)
{
    public string? ParentKey { get; init; }
    public bool? HasChildren { get; init; }
    public IReadOnlyDictionary<string, string?>? Extra { get; init; }
}

public sealed record LookupPage(IReadOnlyList<LookupItem> Items, bool HasMore, int? Total = null);

/// <summary>A filter chip. Op: eq, ne, contains, startsWith, gt, gte, lt, lte, between, in, isEmpty.</summary>
public sealed record LookupFilter(string Field, string Op, string? Value = null, IReadOnlyList<string>? Values = null)
{
    /// <summary>What the chip shows for the value (e.g. the text of a lookup key); not sent.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string? Display { get; init; }
}

public sealed record LookupQuery
{
    public string? Q { get; init; }
    public IReadOnlyList<string>? Fields { get; init; }
    public string? Parent { get; init; }
    public IReadOnlyList<LookupFilter>? Filters { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 20;
    public bool IncludeTotal { get; init; }
}

/// <param name="Type">text, number, date, bool or key.</param>
public sealed record LookupFilterField(string Name, string Label, string Type, IReadOnlyList<string> Ops, string? ValueLookup = null);

/// <param name="Key">"{prefix}.{name}".</param>
/// <param name="ParentKind">null, "dependent" or "tree".</param>
/// <param name="Url">Relative to the gateway: /api/{prefix}/lookups/{name}.</param>
public sealed record LookupDescriptor(
    string Key,
    string Module,
    string Name,
    string Label,
    string? Description,
    string? Permission,
    IReadOnlyList<string> SearchFields,
    IReadOnlyList<LookupFilterField> FilterFields,
    IReadOnlyList<string> Columns,
    string? ParentField,
    string? ParentKind,
    string Url);

/// <summary>
/// The lookups of every module, for the browser (the host provides it; modules inject it). Requests go through the
/// gateway to the module that owns the lookup, with the user's token and language.
/// </summary>
public interface ILookupService
{
    /// <summary>The lookups the user may use (read once per session, unless <paramref name="refresh"/>).</summary>
    Task<IReadOnlyList<LookupDescriptor>> CatalogAsync(bool refresh = false, CancellationToken ct = default);

    /// <summary>One lookup of the catalog, or null when it is unknown or the user may not use it.</summary>
    Task<LookupDescriptor?> FindAsync(string key, CancellationToken ct = default);

    /// <summary>A page of items: a search (GET), or a query with filters and a total (POST) when it has either.</summary>
    Task<LookupPage> SearchAsync(string key, LookupQuery query, CancellationToken ct = default);

    /// <summary>The items with these keys (the text of saved values); missing keys are absent.</summary>
    Task<IReadOnlyList<LookupItem>> ResolveAsync(string key, IReadOnlyCollection<string> keys, IReadOnlyList<LookupFilter>? filters = null, CancellationToken ct = default);
}

/// <summary>Operator names as people read them in a filter chip.</summary>
public static class LookupOps
{
    public static string Symbol(string op) => op switch
    {
        "eq" => "=",
        "ne" => "≠",
        "gt" => ">",
        "gte" => "≥",
        "lt" => "<",
        "lte" => "≤",
        "contains" => "contains",
        "startsWith" => "starts with",
        "between" => "between",
        "in" => "in",
        "isEmpty" => "is empty",
        _ => op
    };

    public static bool TakesValues(string op) => op is "in" or "between";

    public static bool TakesNoValue(string op) => op == "isEmpty";
}

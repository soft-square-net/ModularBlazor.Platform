using System.Linq.Expressions;

namespace soft.square.net.WebApi.Contracts.Lookups;

/// <summary>
/// Declares a module's lookups: key/text lists over its own entities, for autocompletes, cascades and filter pickers.
/// Put one implementation in the module's entry (Web) assembly, with a parameterless constructor; the host finds it
/// when the module is installed (to list the lookups in the catalog) and when it is loaded (to serve them).
/// <code>
/// public sealed class InventoryLookups : ILookupModule
/// {
///     public void Register(ILookupBuilder l) =>
///         l.For&lt;StockItem&gt;("stock-items").Key(i => i.Id).Text(i => i.Sku + " - " + i.Name).SearchOn("Sku", "Name");
/// }
/// </code>
/// The host maps <c>GET /api/{prefix}/lookups/{name}</c>, <c>GET .../{name}/keys</c> and <c>POST .../{name}/query</c>.
/// The lookup's key in the catalog is <c>{prefix}.{name}</c>.
/// </summary>
public interface ILookupModule
{
    void Register(ILookupBuilder lookups);
}

public interface ILookupBuilder
{
    /// <param name="name">Lowercase a-z, 0-9 and '-', unique in the module.</param>
    ILookupDefinitionBuilder<T> For<T>(string name) where T : class;
}

/// <summary>
/// One lookup over entity <typeparamref name="T"/>. Field names are property names of <typeparamref name="T"/>; they are
/// checked when the module is installed and loaded, so a typo fails there, not on the first request.
/// Expressions are translated to SQL: search, filters, order and paging run in the database and only the key, the text
/// and the declared columns are read.
/// </summary>
public interface ILookupDefinitionBuilder<T> where T : class
{
    /// <summary>The stored value. Sent as a string; parsed back to the property's type.</summary>
    ILookupDefinitionBuilder<T> Key<TKey>(Expression<Func<T, TKey>> key);

    /// <summary>The text people see. An expression, so SQL builds it (and it can be searched and ordered).</summary>
    ILookupDefinitionBuilder<T> Text(Expression<Func<T, string>> text);

    /// <summary>The text for one culture ("ar"); the request's UI culture picks it, falling back to the default text.</summary>
    ILookupDefinitionBuilder<T> Text(string culture, Expression<Func<T, string>> text);

    /// <summary>The text is this string property (the default text when <see cref="Text(Expression{Func{T, string}})"/> is not called).</summary>
    ILookupDefinitionBuilder<T> TextField(string property);

    /// <summary>
    /// Formats each item of a page in memory, after SQL: for what SQL cannot express (dates, enum names). The item has
    /// the key, the SQL text and the <see cref="Columns"/>. Search still runs in SQL on <see cref="SearchOn"/>.
    /// </summary>
    ILookupDefinitionBuilder<T> Render(Func<LookupItem, string> render);

    /// <summary>
    /// The string properties free-text search may use (the allowlist; a caller can narrow it with <c>fields=</c>, never
    /// widen it). Without it, search uses the text.
    /// </summary>
    ILookupDefinitionBuilder<T> SearchOn(params string[] properties);

    /// <summary>A fixed filter, always applied: to search, to queries and to resolving keys.</summary>
    ILookupDefinitionBuilder<T> Where(Expression<Func<T, bool>> predicate);

    /// <summary>The order of the list (default: the text).</summary>
    ILookupDefinitionBuilder<T> OrderBy<TValue>(Expression<Func<T, TValue>> order, bool descending = false);

    /// <summary>
    /// A dependent lookup: <c>?parent=</c> filters this property (e.g. a city's CountryId), whose value comes from another
    /// lookup. Without <c>parent</c>, nothing is filtered.
    /// </summary>
    ILookupDefinitionBuilder<T> ParentField(string property);

    /// <summary>
    /// A tree over the entity itself (e.g. Category.ParentId): <c>?parent=</c> filters this property, and without it
    /// the roots (null parent) are listed. Items report <c>hasChildren</c>, so a cascade knows whether to show a next
    /// level. Index the property in the module's migration.
    /// </summary>
    ILookupDefinitionBuilder<T> Hierarchy(string parentProperty, string? leafProperty = null);

    /// <summary>
    /// A property the picker may filter on. The operators come from its type (text: eq, contains, startsWith; number and
    /// date: eq, ne, gt, gte, lt, lte, between; bool: eq; any: in, isEmpty). <paramref name="valueLookup"/>: the key of
    /// the lookup that values are picked from ("inventory.kinds").
    /// </summary>
    ILookupDefinitionBuilder<T> FilterOn(string property, string? label = null, string? valueLookup = null);

    /// <summary>Extra properties sent with each item (<c>extra</c>), e.g. for the picker's result columns.</summary>
    ILookupDefinitionBuilder<T> Columns(params string[] properties);

    /// <summary>The permission needed to search and resolve; declare it in the manifest like any permission.</summary>
    ILookupDefinitionBuilder<T> Permission(string permission);

    ILookupDefinitionBuilder<T> Label(string label, string? description = null);

    /// <summary>
    /// Where the entities come from (default: <c>DbContext.Set&lt;T&gt;()</c> of the module's tenant DbContext, untracked).
    /// Called per request with the module's request scope.
    /// </summary>
    ILookupDefinitionBuilder<T> Source(Func<IServiceProvider, IQueryable<T>> source);
}

/// <param name="Key">The stored value, as a string.</param>
/// <param name="ParentKey">For a lookup with a parent or hierarchy field: the item's parent key.</param>
/// <param name="HasChildren">For a hierarchy: whether a cascade shows a next level under this item.</param>
/// <param name="Extra">The lookup's columns, formatted with the invariant culture.</param>
public sealed record LookupItem(string Key, string Text)
{
    public string? ParentKey { get; init; }
    public bool? HasChildren { get; init; }
    public IReadOnlyDictionary<string, string?>? Extra { get; init; }
}

/// <param name="HasMore">More items after this page (read as take + 1 rows, no COUNT).</param>
/// <param name="Total">Only when asked for (<see cref="LookupQuery.IncludeTotal"/>).</param>
public sealed record LookupPage(IReadOnlyList<LookupItem> Items, bool HasMore, int? Total = null);

/// <summary>One filter chip: <c>Name contains "SKU"</c>, <c>KindId in [25, 26]</c>, <c>Quantity between [1, 10]</c>.</summary>
/// <param name="Op">eq, ne, contains, startsWith, gt, gte, lt, lte, between, in, isEmpty.</param>
/// <param name="Value">The value for one-value operators.</param>
/// <param name="Values">The values for in (up to 100) and between (two).</param>
public sealed record LookupFilter(string Field, string Op, string? Value = null, IReadOnlyList<string>? Values = null);

/// <summary>A search or a filtered query. Filters combine with AND; OR goes inside one filter with <c>in</c>.</summary>
public sealed record LookupQuery
{
    public string? Q { get; init; }

    /// <summary>Narrows the search fields (a subset of the lookup's <c>SearchOn</c>).</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>For a dependent or hierarchy lookup: the parent key. Null on a hierarchy lists the roots.</summary>
    public string? Parent { get; init; }

    public IReadOnlyList<LookupFilter>? Filters { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; } = 20;

    public bool IncludeTotal { get; init; }
}

/// <summary>A field the picker may filter on, with the operators its type allows.</summary>
/// <param name="Type">text, number, date, bool or key (anything else compared by equality).</param>
public sealed record LookupFilterField(string Name, string Label, string Type, IReadOnlyList<string> Ops, string? ValueLookup = null);

/// <summary>A lookup as the catalog lists it: metadata only, recorded when the module is installed.</summary>
/// <param name="Key">"{prefix}.{name}", e.g. "inventory.stock-items".</param>
/// <param name="ParentKind">null, "dependent" (<c>ParentField</c>) or "tree" (<c>Hierarchy</c>).</param>
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
    string? ParentKind)
{
    /// <summary>Where to send the requests, relative to the API root: <c>/api/{prefix}/lookups/{name}</c>.</summary>
    public string Url { get; init; } = "";
}

/// <summary>The owner of a lookup could not answer: it is not active anywhere this host can reach.</summary>
public sealed class LookupUnavailableException(string key, string? reason = null)
    : Exception($"Lookup '{key}' is not available{(reason is null ? "" : $": {reason}")}.")
{
    public string Key { get; } = key;
}

/// <summary>
/// Another module's lookups, for server-side checks (does this key exist, what is its text). The same call works when
/// the owner runs in this process and when it runs in another pool. Calls carry the current user, so a user cannot
/// reference an item they may not see. Throws <see cref="LookupUnavailableException"/> when the owner cannot answer,
/// <see cref="UnauthorizedAccessException"/> when the user lacks the lookup's permission and
/// <see cref="ArgumentException"/> for an unknown lookup or an invalid query.
/// </summary>
public interface ILookupClient
{
    Task<IReadOnlyList<LookupDescriptor>> CatalogAsync(CancellationToken ct = default);

    Task<LookupPage> SearchAsync(string key, LookupQuery query, CancellationToken ct = default);

    /// <summary>
    /// The items with these keys that exist and pass the lookup's fixed filter and <paramref name="filters"/> (e.g. an
    /// author's preset filters); missing keys are simply absent from the result.
    /// </summary>
    Task<IReadOnlyList<LookupItem>> ResolveAsync(string key, IReadOnlyCollection<string> keys, IReadOnlyList<LookupFilter>? filters = null, CancellationToken ct = default);
}

using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using soft.square.net.WebApi.Contracts.Lookups;

namespace soft.square.net.WebApi.Lookups;

/// <summary>A lookup the host can serve, without knowing its entity type.</summary>
internal interface ILookupDefinition
{
    string Name { get; }
    string? RequiredPermission { get; }
    LookupDescriptor Describe(string modulePrefix, string moduleId);
    Task<LookupPage> SearchAsync(IServiceProvider services, LookupQuery query, CancellationToken ct);
    Task<IReadOnlyList<LookupItem>> ResolveAsync(IServiceProvider services, IReadOnlyCollection<string> keys, IReadOnlyList<LookupFilter>? filters, CancellationToken ct);
}

/// <summary>Request limits; a request over them is answered with 400.</summary>
internal static class LookupLimits
{
    public const int MaxTake = 100;
    public const int MaxQueryLength = 200;
    public const int MaxFilters = 10;
    public const int MaxValues = 100;
    public const int MaxValueLength = 200;
    public const int MaxKeys = 200;
    public const int MaxColumns = 8;
}

/// <summary>
/// The host's engine for one lookup over <typeparamref name="T"/>: builds parameterized expression trees from the
/// definition and the request, so EF translates search, filters, order and paging to SQL and reuses its query plans.
/// <para>Unload safety: a definition lives in the module's container (<see cref="LookupSet"/>), never in a host
/// singleton, and keeps no static caches keyed by type. Values are parsed without TypeDescriptor, which would pin
/// the module's types.</para>
/// </summary>
internal sealed partial class LookupDefinition<T>(string name) : ILookupDefinitionBuilder<T>, ILookupDefinition where T : class
{
    private LambdaExpression? _key;
    private LambdaExpression? _text;
    private readonly Dictionary<string, LambdaExpression> _cultureTexts = new(StringComparer.OrdinalIgnoreCase);
    private Func<LookupItem, string>? _render;
    private string[] _searchOn = [];
    private readonly List<LambdaExpression> _where = [];
    private LambdaExpression? _order;
    private bool _descending;
    private PropertyInfo? _parent;
    private string? _parentKind;
    private PropertyInfo? _leaf;
    private readonly List<(PropertyInfo Property, string Label, string? ValueLookup)> _filters = [];
    private PropertyInfo[] _columns = [];
    private string? _label;
    private string? _description;
    private Func<IServiceProvider, IQueryable<T>>? _source;

    public string Name { get; } = name;
    public string? RequiredPermission { get; private set; }

    // ───────────── builder ─────────────

    public ILookupDefinitionBuilder<T> Key<TKey>(Expression<Func<T, TKey>> key)
    {
        if (!IsScalar(Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey)))
            throw Invalid($"the key must be a string, number, Guid, date or enum, not {typeof(TKey).Name}");
        _key = key;
        return this;
    }

    public ILookupDefinitionBuilder<T> Text(Expression<Func<T, string>> text) { _text = text; return this; }

    public ILookupDefinitionBuilder<T> Text(string culture, Expression<Func<T, string>> text)
    {
        _cultureTexts[culture] = text;
        return this;
    }

    public ILookupDefinitionBuilder<T> TextField(string property)
    {
        var p = Property(property, mustBeString: true);
        var x = Expression.Parameter(typeof(T), "x");
        _text = Expression.Lambda<Func<T, string>>(Expression.Property(x, p), x);
        return this;
    }

    public ILookupDefinitionBuilder<T> Render(Func<LookupItem, string> render) { _render = render; return this; }

    public ILookupDefinitionBuilder<T> SearchOn(params string[] properties)
    {
        foreach (var p in properties) Property(p, mustBeString: true);
        _searchOn = properties.Select(p => Property(p).Name).Distinct().ToArray();
        return this;
    }

    public ILookupDefinitionBuilder<T> Where(Expression<Func<T, bool>> predicate) { _where.Add(predicate); return this; }

    public ILookupDefinitionBuilder<T> OrderBy<TValue>(Expression<Func<T, TValue>> order, bool descending = false)
    {
        _order = order;
        _descending = descending;
        return this;
    }

    public ILookupDefinitionBuilder<T> ParentField(string property)
    {
        _parent = ScalarProperty(property);
        _parentKind = "dependent";
        return this;
    }

    public ILookupDefinitionBuilder<T> Hierarchy(string parentProperty, string? leafProperty = null)
    {
        _parent = ScalarProperty(parentProperty);
        if (!CanBeNull(_parent.PropertyType)) throw Invalid($"the parent property '{parentProperty}' must be nullable (roots have no parent)");
        _parentKind = "tree";
        if (leafProperty is not null)
        {
            _leaf = Property(leafProperty);
            if (_leaf.PropertyType != typeof(bool)) throw Invalid($"the leaf property '{leafProperty}' must be a bool");
        }
        return this;
    }

    public ILookupDefinitionBuilder<T> FilterOn(string property, string? label = null, string? valueLookup = null)
    {
        var p = ScalarProperty(property);
        _filters.RemoveAll(f => f.Property == p);
        _filters.Add((p, label ?? p.Name, valueLookup));
        return this;
    }

    public ILookupDefinitionBuilder<T> Columns(params string[] properties)
    {
        if (properties.Length > LookupLimits.MaxColumns) throw Invalid($"at most {LookupLimits.MaxColumns} columns");
        _columns = properties.Select(ScalarProperty).Distinct().ToArray();
        return this;
    }

    public ILookupDefinitionBuilder<T> Permission(string permission) { RequiredPermission = permission; return this; }

    public ILookupDefinitionBuilder<T> Label(string label, string? description = null)
    {
        _label = label;
        _description = description;
        return this;
    }

    public ILookupDefinitionBuilder<T> Source(Func<IServiceProvider, IQueryable<T>> source) { _source = source; return this; }

    /// <summary>Checks the definition is complete; called once Register has run.</summary>
    public void Complete()
    {
        if (!NameText().IsMatch(Name)) throw Invalid("the name is lowercase a-z, 0-9 and '-'");
        if (_key is null) throw Invalid("call Key(...)");
        if (_text is null) throw Invalid("call Text(...) or TextField(...)");
        if (_parentKind == "tree" && _leaf is null && _parent!.PropertyType != KeyType
            && Nullable.GetUnderlyingType(_parent.PropertyType) != (Nullable.GetUnderlyingType(KeyType) ?? KeyType))
            throw Invalid($"the parent property '{_parent.Name}' must have the key's type");
    }

    private Type KeyType => _key!.ReturnType;

    // ───────────── catalog ─────────────

    public LookupDescriptor Describe(string modulePrefix, string moduleId) => new(
        $"{modulePrefix}.{Name}", moduleId, Name, _label ?? Name, _description, RequiredPermission,
        _searchOn.Length > 0 ? _searchOn : ["text"],
        [.. _filters.Select(f => new LookupFilterField(f.Property.Name, f.Label, Kind(f.Property.PropertyType), Ops(f.Property.PropertyType), f.ValueLookup))],
        [.. _columns.Select(c => c.Name)],
        _parent?.Name,
        _parentKind);

    // ───────────── search ─────────────

    public async Task<LookupPage> SearchAsync(IServiceProvider services, LookupQuery query, CancellationToken ct)
    {
        var take = query.Take;
        if (take is < 1 or > LookupLimits.MaxTake) throw new ArgumentException($"take is 1 to {LookupLimits.MaxTake}.");
        if (query.Skip < 0) throw new ArgumentException("skip cannot be negative.");
        if (query.Q is { Length: > LookupLimits.MaxQueryLength }) throw new ArgumentException($"q is at most {LookupLimits.MaxQueryLength} characters.");

        var x = Expression.Parameter(typeof(T), "x");
        var text = Bind(PickText(), x);
        var source = Source(services);
        var filtered = Filtered(source, x, query.Filters);

        if (_parent is not null && (query.Parent is not null || _parentKind == "tree"))
            filtered = filtered.Where(Lambda(ParentEquals(x, query.Parent), x));

        if (!string.IsNullOrWhiteSpace(query.Q))
            filtered = filtered.Where(Lambda(Search(x, text, query.Q.Trim(), query.Fields), x));

        var total = query.IncludeTotal ? await CountAsync(filtered, ct) : (int?)null;

        var ordered = Order(filtered, x, text);
        var rows = await ListAsync(ordered.Skip(query.Skip).Take(take + 1).Select(Projection(x, text, source)), ct);
        var hasMore = rows.Count > take;
        return new LookupPage([.. rows.Take(take).Select(ToItem)], hasMore, total);
    }

    public async Task<IReadOnlyList<LookupItem>> ResolveAsync(IServiceProvider services, IReadOnlyCollection<string> keys, IReadOnlyList<LookupFilter>? filters, CancellationToken ct)
    {
        if (keys.Count == 0) return [];
        if (keys.Count > LookupLimits.MaxKeys) throw new ArgumentException($"At most {LookupLimits.MaxKeys} keys.");

        var x = Expression.Parameter(typeof(T), "x");
        var keyBody = Bind(_key!, x);
        var parsed = new List<object?>();
        foreach (var key in keys.Distinct())
            if (TryParse(key, keyBody.Type, out var value)) parsed.Add(value); // an unparsable key simply does not exist

        if (parsed.Count == 0) return [];
        var source = Source(services);
        var filtered = Filtered(source, x, filters).Where(Lambda(In(keyBody, parsed), x));
        var text = Bind(PickText(), x);
        var rows = await ListAsync(filtered.Select(Projection(x, text, source)), ct);
        return [.. rows.Select(ToItem)];
    }

    // ───────────── query building ─────────────

    private IQueryable<T> Source(IServiceProvider services) =>
        _source?.Invoke(services) ?? services.GetRequiredService<DbContext>().Set<T>().AsNoTracking();

    private IQueryable<T> Filtered(IQueryable<T> source, ParameterExpression x, IReadOnlyList<LookupFilter>? filters)
    {
        var query = source;
        foreach (var predicate in _where) query = query.Where(Lambda(Bind(predicate, x), x));
        if (filters is null) return query;
        if (filters.Count > LookupLimits.MaxFilters) throw new ArgumentException($"At most {LookupLimits.MaxFilters} filters.");
        foreach (var filter in filters) query = query.Where(Lambda(Filter(x, filter), x));
        return query;
    }

    private IQueryable<T> Order(IQueryable<T> query, ParameterExpression x, Expression text)
    {
        var first = _order is null ? text : Bind(_order, x);
        var ordered = Sort(query, Expression.Lambda(first, x), _descending, then: false);
        return Sort(ordered, Expression.Lambda(Bind(_key!, x), x), false, then: true); // stable pages
    }

    private static IOrderedQueryable<T> Sort(IQueryable<T> query, LambdaExpression by, bool descending, bool then)
    {
        var method = (then, descending) switch
        {
            (false, false) => nameof(Queryable.OrderBy),
            (false, true) => nameof(Queryable.OrderByDescending),
            (true, false) => nameof(Queryable.ThenBy),
            (true, true) => nameof(Queryable.ThenByDescending)
        };
        var call = Expression.Call(typeof(Queryable), method, [typeof(T), by.ReturnType], query.Expression, Expression.Quote(by));
        return (IOrderedQueryable<T>)query.Provider.CreateQuery<T>(call);
    }

    private Expression<Func<T, LookupRow>> Projection(ParameterExpression x, Expression text, IQueryable<T> source)
    {
        var bindings = new List<MemberBinding>
        {
            Expression.Bind(LookupRow.Member(nameof(LookupRow.Key)), Expression.Convert(Bind(_key!, x), typeof(object))),
            Expression.Bind(LookupRow.Member(nameof(LookupRow.Text)), text)
        };
        if (_parent is not null)
            bindings.Add(Expression.Bind(LookupRow.Member(nameof(LookupRow.Parent)), Expression.Convert(Expression.Property(x, _parent), typeof(object))));
        if (_parentKind == "tree")
            bindings.Add(Expression.Bind(LookupRow.Member(nameof(LookupRow.HasChildren)), HasChildren(x, source)));
        for (var i = 0; i < _columns.Length; i++)
            bindings.Add(Expression.Bind(LookupRow.Member($"C{i}"), Expression.Convert(Expression.Property(x, _columns[i]), typeof(object))));
        return Expression.Lambda<Func<T, LookupRow>>(Expression.MemberInit(Expression.New(typeof(LookupRow)), bindings), x);
    }

    /// <summary>From the leaf property, or <c>EXISTS (child whose parent is this key)</c> in the same SQL query.</summary>
    private Expression HasChildren(ParameterExpression x, IQueryable<T> source)
    {
        if (_leaf is not null) return Expression.Not(Expression.Property(x, _leaf));
        var c = Expression.Parameter(typeof(T), "c");
        var parent = Expression.Property(c, _parent!);
        var key = Bind(_key!, x);
        var match = Expression.Equal(parent, key.Type == parent.Type ? key : Expression.Convert(key, parent.Type));
        return Expression.Call(typeof(Queryable), nameof(Queryable.Any), [typeof(T)], source.Expression, Expression.Quote(Expression.Lambda(match, c)));
    }

    private Expression ParentEquals(ParameterExpression x, string? parent)
    {
        var property = Expression.Property(x, _parent!);
        if (parent is null) return Expression.Equal(property, Expression.Constant(null, property.Type));
        if (!TryParse(parent, property.Type, out var value)) throw new ArgumentException($"'{parent}' is not a valid parent key.");
        return Expression.Equal(property, Parameter(value, property.Type));
    }

    private Expression Search(ParameterExpression x, Expression text, string q, IReadOnlyList<string>? fields)
    {
        IEnumerable<Expression> targets;
        if (_searchOn.Length == 0)
        {
            targets = [text];
        }
        else
        {
            var allowed = fields is { Count: > 0 }
                ? _searchOn.Where(f => fields.Contains(f, StringComparer.OrdinalIgnoreCase)).ToArray()
                : _searchOn;
            if (allowed.Length == 0) throw new ArgumentException($"fields must be among: {string.Join(", ", _searchOn)}.");
            targets = allowed.Select(f => (Expression)Expression.Property(x, typeof(T).GetProperty(f)!));
        }
        var term = Parameter(q.ToLowerInvariant(), typeof(string));
        return targets.Select(t => Like(t, nameof(string.Contains), term)).Aggregate(Expression.OrElse);
    }

    private Expression Filter(ParameterExpression x, LookupFilter filter)
    {
        var entry = _filters.FirstOrDefault(f => f.Property.Name.Equals(filter.Field, StringComparison.OrdinalIgnoreCase));
        if (entry.Property is null)
            throw new ArgumentException($"'{filter.Field}' cannot be filtered; use one of: {string.Join(", ", _filters.Select(f => f.Property.Name))}.");
        var property = Expression.Property(x, entry.Property);
        var type = property.Type;
        var op = filter.Op;
        if (!Ops(type).Contains(op, StringComparer.Ordinal))
            throw new ArgumentException($"'{filter.Field}' does not support '{op}'; use one of: {string.Join(", ", Ops(type))}.");

        if (op == "isEmpty")
            return type == typeof(string)
                ? Expression.OrElse(Expression.Equal(property, Expression.Constant(null, type)), Expression.Equal(property, Expression.Constant("")))
                : Expression.Equal(property, Expression.Constant(null, type));

        if (op is "in" or "between")
        {
            var values = filter.Values ?? [];
            if (op == "between" && values.Count != 2) throw new ArgumentException($"'{filter.Field}' between needs two values.");
            if (values.Count is 0 or > LookupLimits.MaxValues) throw new ArgumentException($"'{filter.Field}' in needs 1 to {LookupLimits.MaxValues} values.");
            var parsed = values.Select(v => Value(filter.Field, v, type)).ToList();
            return op == "in"
                ? In(type == typeof(string) ? Lower(property) : property, type == typeof(string) ? parsed.Select(v => (object?)((string?)v)?.ToLowerInvariant()).ToList() : parsed)
                : Expression.AndAlso(
                    Expression.GreaterThanOrEqual(property, Parameter(parsed[0], type)),
                    Expression.LessThanOrEqual(property, Parameter(parsed[1], type)));
        }

        var value = Value(filter.Field, filter.Value, type);
        if (type == typeof(string))
        {
            var lowered = Parameter(((string?)value)?.ToLowerInvariant(), typeof(string));
            return op switch
            {
                "contains" => Like(property, nameof(string.Contains), lowered),
                "startsWith" => Like(property, nameof(string.StartsWith), lowered),
                "ne" => Expression.NotEqual(Lower(property), lowered),
                _ => Expression.Equal(Lower(property), lowered)
            };
        }
        var constant = Parameter(value, type);
        return op switch
        {
            "ne" => Expression.NotEqual(property, constant),
            "gt" => Expression.GreaterThan(property, constant),
            "gte" => Expression.GreaterThanOrEqual(property, constant),
            "lt" => Expression.LessThan(property, constant),
            "lte" => Expression.LessThanOrEqual(property, constant),
            _ => Expression.Equal(property, constant)
        };
    }

    private static object? Value(string field, string? text, Type type)
    {
        if (text is null) throw new ArgumentException($"'{field}' needs a value.");
        if (text.Length > LookupLimits.MaxValueLength) throw new ArgumentException($"'{field}': a value is at most {LookupLimits.MaxValueLength} characters.");
        return TryParse(text, type, out var value) ? value : throw new ArgumentException($"'{text}' is not a valid {Kind(type)} for '{field}'.");
    }

    /// <summary><c>member != null &amp;&amp; member.ToLower().Contains(term)</c> (lowered on both sides: the same on every engine).</summary>
    private static Expression Like(Expression member, string method, Expression loweredTerm) =>
        Expression.AndAlso(
            Expression.NotEqual(member, Expression.Constant(null, typeof(string))),
            Expression.Call(Lower(member), typeof(string).GetMethod(method, [typeof(string)])!, loweredTerm));

    private static Expression Lower(Expression member) => Expression.Call(member, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);

    /// <summary><c>values.Contains(member)</c> with a typed list, which EF sends as one parameter.</summary>
    private static Expression In(Expression member, IReadOnlyList<object?> values)
    {
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(member.Type))!;
        foreach (var value in values) list.Add(value);
        var holder = Activator.CreateInstance(typeof(Box<>).MakeGenericType(list.GetType()), list)!;
        var listExpression = Expression.Property(Expression.Constant(holder), nameof(Box<object>.Value));
        return Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [member.Type], listExpression, member);
    }

    /// <summary>A value EF turns into a SQL parameter (a closure member), so query plans are reused.</summary>
    private static Expression Parameter(object? value, Type type)
    {
        var holder = Activator.CreateInstance(typeof(Box<>).MakeGenericType(type), value)!;
        return Expression.Property(Expression.Constant(holder), nameof(Box<object>.Value));
    }

    private LambdaExpression PickText()
    {
        var culture = CultureInfo.CurrentUICulture;
        return _cultureTexts.GetValueOrDefault(culture.Name) ?? _cultureTexts.GetValueOrDefault(culture.TwoLetterISOLanguageName) ?? _text!;
    }

    private static Expression Bind(LambdaExpression lambda, ParameterExpression x) => new Rebind(lambda.Parameters[0], x).Visit(lambda.Body);

    private static Expression<Func<T, bool>> Lambda(Expression body, ParameterExpression x) => Expression.Lambda<Func<T, bool>>(body, x);

    private LookupItem ToItem(LookupRow row)
    {
        Dictionary<string, string?>? extra = null;
        if (_columns.Length > 0)
        {
            extra = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var i = 0; i < _columns.Length; i++) extra[_columns[i].Name] = Format(row.Column(i));
        }
        var item = new LookupItem(Format(row.Key) ?? "", row.Text ?? "")
        {
            ParentKey = _parent is null ? null : Format(row.Parent),
            HasChildren = _parentKind == "tree" ? row.HasChildren : null,
            Extra = extra
        };
        return _render is null ? item : item with { Text = _render(item) };
    }

    private static async Task<List<LookupRow>> ListAsync(IQueryable<LookupRow> query, CancellationToken ct) =>
        query.Provider is IAsyncQueryProvider ? await query.ToListAsync(ct) : query.ToList();

    private static async Task<int> CountAsync(IQueryable<T> query, CancellationToken ct) =>
        query.Provider is IAsyncQueryProvider ? await query.CountAsync(ct) : query.Count();

    // ───────────── types and values ─────────────

    private PropertyInfo Property(string name, bool mustBeString = false)
    {
        var property = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                       ?? throw Invalid($"{typeof(T).Name} has no public property '{name}'");
        if (mustBeString && property.PropertyType != typeof(string)) throw Invalid($"'{name}' must be a string property");
        return property;
    }

    private PropertyInfo ScalarProperty(string name)
    {
        var property = Property(name);
        if (!IsScalar(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType))
            throw Invalid($"'{name}' must be a string, number, bool, Guid, date or enum");
        return property;
    }

    private ArgumentException Invalid(string message) => new($"Lookup '{Name}': {message}.");

    private static bool IsScalar(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly);

    private static bool CanBeNull(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static string Kind(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string)) return "text";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(DateOnly) || t == typeof(TimeOnly)) return "date";
        if (t.IsEnum || t == typeof(Guid) || t == typeof(char)) return "key";
        return t.IsPrimitive || t == typeof(decimal) ? "number" : "key";
    }

    private static IReadOnlyList<string> Ops(Type type)
    {
        List<string> ops = Kind(type) switch
        {
            "text" => ["eq", "ne", "contains", "startsWith", "in"],
            "number" or "date" => ["eq", "ne", "gt", "gte", "lt", "lte", "between", "in"],
            "bool" => ["eq"],
            _ => ["eq", "ne", "in"]
        };
        if (CanBeNull(type)) ops.Add("isEmpty");
        return ops;
    }

    /// <summary>Parses request text into a property's type, with the invariant culture; enums by name or number.</summary>
    internal static bool TryParse(string text, Type type, out object? value)
    {
        value = null;
        var t = Nullable.GetUnderlyingType(type) ?? type;
        var c = CultureInfo.InvariantCulture;
        try
        {
            if (t == typeof(string)) { value = text; return true; }
            if (t == typeof(Guid)) { if (!Guid.TryParse(text, out var g)) return false; value = g; }
            else if (t == typeof(bool)) { if (!bool.TryParse(text, out var b)) return false; value = b; }
            else if (t == typeof(DateTimeOffset)) { if (!DateTimeOffset.TryParse(text, c, DateTimeStyles.AssumeUniversal, out var d)) return false; value = d; }
            else if (t == typeof(DateTime)) { if (!DateTime.TryParse(text, c, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)) return false; value = d; }
            else if (t == typeof(DateOnly)) { if (!DateOnly.TryParse(text, c, out var d)) return false; value = d; }
            else if (t == typeof(TimeOnly)) { if (!TimeOnly.TryParse(text, c, out var d)) return false; value = d; }
            else if (t.IsEnum) { if (!Enum.TryParse(t, text, ignoreCase: true, out var e) || !Enum.IsDefined(t, e!)) return false; value = e; }
            else if (t.IsPrimitive || t == typeof(decimal)) value = Convert.ChangeType(text, t, c);
            else return false;
            return true;
        }
        catch (Exception e) when (e is FormatException or OverflowException or InvalidCastException)
        {
            return false;
        }
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex NameText();

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}

/// <summary>Holds a request value, so EF sees a closure member and makes it a SQL parameter.</summary>
internal sealed class Box<TValue>(TValue value)
{
    public TValue Value { get; } = value;
}

/// <summary>One projected row: the key, the text and up to <see cref="LookupLimits.MaxColumns"/> columns.</summary>
internal sealed class LookupRow
{
    public object? Key { get; set; }
    public string? Text { get; set; }
    public object? Parent { get; set; }
    public bool HasChildren { get; set; }
    public object? C0 { get; set; }
    public object? C1 { get; set; }
    public object? C2 { get; set; }
    public object? C3 { get; set; }
    public object? C4 { get; set; }
    public object? C5 { get; set; }
    public object? C6 { get; set; }
    public object? C7 { get; set; }

    public object? Column(int index) => index switch
    {
        0 => C0, 1 => C1, 2 => C2, 3 => C3, 4 => C4, 5 => C5, 6 => C6, 7 => C7,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public static MemberInfo Member(string name) => typeof(LookupRow).GetProperty(name)!;
}

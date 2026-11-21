using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;

namespace soft.square.net.Share.All.Localization;

/// <summary>
/// The platform's <see cref="IStringLocalizerFactory"/>, on the client (Blazor WebAssembly) and on the server (WebApi,
/// identity service) alike. Every assembly carries its own translations as embedded JSON files, one per culture:
/// <code>
///   i18n/ar.json   { "Save": "حفظ", "{0} concepts": "{0} مفهوم" }
///   &lt;EmbeddedResource Include="i18n\*.json" /&gt;
/// </code>
/// The key is the English text as written in the code (<c>L["Save"]</c>), so a missing translation shows English,
/// never a raw key, and an English file is only needed to reword the English text.
/// <para>
/// Why not .resx: those put each language in a satellite assembly (<c>ar/x.resources.dll</c>), while a module is one
/// uploaded assembly loaded into its own, unloadable load context. Embedded JSON travels inside the module, is found
/// wherever the module is loaded (any pool, the browser), and no module ever reads another module's texts.
/// </para>
/// <para>
/// Lookup for <c>L["Save"]</c> in a type of assembly A, culture ar-EG: A's ar-EG, then A's ar, then the shared texts
/// (see <see cref="SharedAssemblies"/>) in ar-EG and ar, then the key itself.
/// </para>
/// </summary>
public sealed class EmbeddedJsonStringLocalizerFactory : IStringLocalizerFactory
{
    /// <param name="sharedAssemblies">
    /// Assemblies whose texts every other assembly falls back to (the platform's common words: Save, Cancel, ...).
    /// </param>
    public EmbeddedJsonStringLocalizerFactory(params Assembly[] sharedAssemblies) => SharedAssemblies = sharedAssemblies;

    /// <summary>Assemblies whose texts are the fallback for every assembly.</summary>
    public IReadOnlyList<Assembly> SharedAssemblies { get; }

    public IStringLocalizer Create(Type resourceSource)
    {
        ArgumentNullException.ThrowIfNull(resourceSource);
        return new EmbeddedJsonStringLocalizer(resourceSource.Assembly, SharedAssemblies);
    }

    /// <summary><paramref name="location"/> is an assembly name; the base name is not used (one catalog per assembly).</summary>
    public IStringLocalizer Create(string baseName, string location)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, location, StringComparison.OrdinalIgnoreCase))
            ?? Assembly.Load(new AssemblyName(location));
        return new EmbeddedJsonStringLocalizer(assembly, SharedAssemblies);
    }
}

/// <summary>Texts of one assembly, with the shared texts behind them. Reads the culture at each lookup, so one instance follows a culture switch.</summary>
public sealed class EmbeddedJsonStringLocalizer(Assembly assembly, IReadOnlyList<Assembly> shared) : IStringLocalizer
{
    public LocalizedString this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            var value = Find(name, CultureInfo.CurrentUICulture);
            return new LocalizedString(name, value ?? name, resourceNotFound: value is null, searchedLocation: assembly.GetName().Name);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var text = this[name];
            return new LocalizedString(name, string.Format(CultureInfo.CurrentCulture, text.Value, arguments), text.ResourceNotFound, text.SearchedLocation);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in Chain(CultureInfo.CurrentUICulture, includeParentCultures))
        foreach (var (key, value) in TextCatalog.Of(assembly).For(culture))
            if (seen.Add(key)) yield return new LocalizedString(key, value, resourceNotFound: false, assembly.GetName().Name);
    }

    private string? Find(string name, CultureInfo culture)
    {
        foreach (var c in Chain(culture, parents: true))
            if (TextCatalog.Of(assembly).For(c).TryGetValue(name, out var own)) return own;
        foreach (var other in shared)
        {
            if (other == assembly) continue;
            foreach (var c in Chain(culture, parents: true))
                if (TextCatalog.Of(other).For(c).TryGetValue(name, out var common)) return common;
        }
        return null;
    }

    private static IEnumerable<string> Chain(CultureInfo culture, bool parents)
    {
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            yield return c.Name;
            if (!parents) yield break;
        }
    }
}

/// <summary>
/// The translations embedded in one assembly: resources named <c>…i18n.{culture}.json</c> (or <c>i18n/{culture}.json</c>
/// with a LogicalName). Held weakly per assembly, so an unloaded module's load context is never kept alive by it.
/// </summary>
public sealed partial class TextCatalog
{
    private static readonly ConditionalWeakTable<Assembly, TextCatalog> Catalogs = new();
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _cultures = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, string> _resources; // culture -> manifest resource name
    private readonly WeakReference<Assembly> _assembly;

    private TextCatalog(Assembly assembly)
    {
        _assembly = new WeakReference<Assembly>(assembly);
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in assembly.GetManifestResourceNames())
            if (ResourceName().Match(name) is { Success: true } match) resources[match.Groups["culture"].Value] = name;
        _resources = resources;
    }

    public static TextCatalog Of(Assembly assembly) => Catalogs.GetValue(assembly, a => new TextCatalog(a));

    /// <summary>The cultures this assembly has a file for (e.g. "ar", "en").</summary>
    public IReadOnlyCollection<string> Cultures => _resources.Keys.ToArray();

    /// <summary>The texts of exactly this culture (no parent, no fallback); empty when there is no file.</summary>
    public IReadOnlyDictionary<string, string> For(string culture) => _cultures.GetOrAdd(culture, Load);

    private IReadOnlyDictionary<string, string> Load(string culture)
    {
        if (!_resources.TryGetValue(culture, out var resource) || !_assembly.TryGetTarget(out var assembly)) return Empty;
        using var stream = assembly.GetManifestResourceStream(resource);
        return stream is null ? Empty : Parse(stream, $"{assembly.GetName().Name}:{resource}");
    }

    /// <summary>
    /// A flat JSON object of strings. Keys starting with "$" are comments ("$comment": "..."). A nested object is read
    /// as a group whose keys are joined with "." ({ "Nav": { "Home": "…" } } gives "Nav.Home").
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(Stream json, string source = "texts")
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            Read(document.RootElement, prefix: "", texts);
            return texts;
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"The translations {source} are not valid JSON: {e.Message}", e);
        }
    }

    private static void Read(JsonElement element, string prefix, Dictionary<string, string> texts)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.StartsWith('$')) continue;
            var key = prefix + property.Name;
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String: texts[key] = property.Value.GetString()!; break;
                case JsonValueKind.Object: Read(property.Value, key + ".", texts); break;
            }
        }
    }

    [GeneratedRegex(@"(?:^|[./\\])i18n[./\\](?<culture>[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*)\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex ResourceName();
}

/// <summary>Writing direction of a culture.</summary>
public static class TextDirection
{
    private static readonly HashSet<string> RightToLeft = new(StringComparer.OrdinalIgnoreCase) { "ar", "he", "fa", "ur", "ps", "sd", "ug", "yi", "dv", "ckb" };

    /// <summary>True for Arabic, Hebrew, Persian, Urdu, ... (by the language part of the name: ar-EG is right-to-left).</summary>
    public static bool IsRightToLeft(string? culture) =>
        !string.IsNullOrWhiteSpace(culture) && RightToLeft.Contains(culture.Split('-', 2)[0]);

    /// <summary>"rtl" or "ltr", for the HTML dir attribute.</summary>
    public static string Of(string? culture) => IsRightToLeft(culture) ? "rtl" : "ltr";
}

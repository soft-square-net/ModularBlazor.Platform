using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace soft.square.net.WebApi.Modules.Core.ClientModules;

/// <summary>A file of a package as it will be stored.</summary>
internal sealed record PackageFile(string Name, byte[] Content)
{
    public string Sha256 { get; } = Convert.ToHexStringLower(SHA256.HashData(Content));
    public ClientModuleFile Describe() => new(Name, Sha256, Content.LongLength);
}

/// <summary>A checked package: what to store (<see cref="Files"/>, primary first) and what was read from it.</summary>
internal sealed record InspectedPackage(
    ClientModuleKind Kind,
    string Id,
    string Version,
    ClientModuleSettings Detected,
    IReadOnlyList<PackageFile> Files,
    IReadOnlyList<string> Routes,
    IReadOnlyList<string> Layouts,
    IReadOnlyList<ClientModuleMenuItem> Menu,
    IReadOnlyList<string> Warnings)
{
    /// <summary>What the module adds to the layout besides menus ([assembly: HeaderAction / Dock / ContextPanel]).</summary>
    public IReadOnlyList<ClientModuleContribution> Contributions { get; init; } = [];

    /// <summary>What each of the module's layouts offers ([LayoutInfo] next to [LayoutKey]).</summary>
    public IReadOnlyList<ClientModuleLayout> LayoutDetails { get; init; } = [];

    /// <summary>The platform contracts (ModularBlazor.Abstractions) the module was built against, "major.minor.patch".</summary>
    public string? PlatformVersion { get; init; }

    /// <summary>
    /// SHA-256 over the files' names and hashes: equal for the same files, whatever the upload's format. A .nupkg is
    /// hashed by its content (the entries NuGet does not regenerate on every pack), so packing the same code again is
    /// not a new package.
    /// </summary>
    public string PackageHash => Hash(Kind == ClientModuleKind.NuPkg && Files.Count == 1 // computed: `with` copies would keep a stale field
        ? NuPkgContent(Files[0].Content)
        : Files.Select(f => (f.Name, f.Sha256)));

    private static string Hash(IEnumerable<(string Name, string Sha256)> files) => Convert.ToHexStringLower(SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(string.Join('\n', files.OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => $"{f.Name}:{f.Sha256}")))));

    /// <summary>The package's entries without the packaging parts that change on every pack (random ids, timestamps).</summary>
    private static IEnumerable<(string, string)> NuPkgContent(byte[] content)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(content), System.IO.Compression.ZipArchiveMode.Read);
        return zip.Entries
            .Where(e => e.Length > 0
                        && !e.FullName.StartsWith("_rels/", StringComparison.OrdinalIgnoreCase)
                        && !e.FullName.StartsWith("package/", StringComparison.OrdinalIgnoreCase)
                        && !e.FullName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
            .Select(e =>
            {
                using var stream = e.Open();
                return (e.FullName, Convert.ToHexStringLower(SHA256.HashData(stream)));
            })
            .ToList();
    }
}

/// <summary>
/// Reads client module packages with System.Reflection.Metadata: types and attributes are read as data, nothing is
/// loaded or run on the server. Accepts
/// <list type="bullet">
/// <item>a .dll with <c>[assembly: ClientModule]</c> (static files embedded as "module-assets/..." resources);</item>
/// <item>a .nupkg (lib/&lt;tfm&gt;/*.dll and staticwebassets/*), stored and served as is;</item>
/// <item>a .zip (or a seed folder) of assemblies: the one with [ClientModule] is the module, the others its private
/// assemblies (e.g. a generated API client). An optional client-module.json overrides the attribute's settings.</item>
/// </list>
/// </summary>
internal static partial class ClientModuleInspector
{
    private const string ClientAttribute = "soft.square.net.ModularBlazor.Abstractions.ClientModuleAttribute";
    private const string LayoutKeyAttribute = "soft.square.net.ModularBlazor.Abstractions.LayoutKeyAttribute";
    private const string MenuLinkAttribute = "soft.square.net.ModularBlazor.Abstractions.MenuLinkAttribute";
    private const string MenuGroupAttribute = "soft.square.net.ModularBlazor.Abstractions.MenuGroupAttribute";
    private const string ShellNamespace = "soft.square.net.ModularBlazor.Abstractions.Shell.";
    private const string LayoutInfoAttribute = ShellNamespace + "LayoutInfoAttribute";
    private const string AbstractionsAssembly = "soft.square.net.ModularBlazor.Abstractions";

    /// <summary>The shell contribution attributes and the kind each declares (ShellContributionKinds).</summary>
    private static readonly Dictionary<string, string> ContributionAttributes = new(StringComparer.Ordinal)
    {
        [ShellNamespace + "HeaderActionAttribute"] = "header-action",
        [ShellNamespace + "DockAttribute"] = "dock",
        [ShellNamespace + "ContextPanelAttribute"] = "context-panel",
    };

    /// <summary>
    /// The version of the platform contracts this server runs with (WebApi.Contracts ships with ModularBlazor.Abstractions
    /// under one version): a module built against a newer major is refused, a newer minor gets a warning.
    /// </summary>
    internal static Version HostPlatformVersion { get; set; } =
        typeof(soft.square.net.WebApi.Contracts.Tenancy.ITenantContext).Assembly.GetName().Version ?? new Version(1, 0);
    private const string RouteAttribute = "Microsoft.AspNetCore.Components.RouteAttribute";
    private const string AssemblyPrefix = "soft.square.net.";
    public const string SettingsFile = "client-module.json";

    /// <summary>Server-side frameworks: a browser module referencing them fails when it runs.</summary>
    private static readonly string[] ServerOnly =
        ["soft.square.net.WebApi.Contracts", "Microsoft.AspNetCore.Mvc.Core", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore.Server.Kestrel.Core"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static InspectedPackage Inspect(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".dll" => FromAssemblies([new PackageFile(SafeName(fileName), content)], settingsJson: null),
            ".nupkg" => FromNuPkg(content),
            ".zip" => FromZip(content),
            _ => throw new ClientModuleException($"'{fileName}': upload a .dll, .nupkg or .zip.")
        };
    }

    /// <summary>A seed folder: its assemblies form one module, like a .zip.</summary>
    public static InspectedPackage InspectFolder(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .Select(f => new PackageFile(SafeName(Path.GetFileName(f)), File.ReadAllBytes(f)))
            .ToList();
        var settings = Path.Combine(folder, SettingsFile);
        return FromAssemblies(files, File.Exists(settings) ? File.ReadAllText(settings) : null);
    }

    // ---------------------------------------------------------------- formats

    private static InspectedPackage FromZip(byte[] content)
    {
        using var zip = OpenZip(content);
        var files = new List<PackageFile>();
        string? settings = null;
        foreach (var entry in zip.Entries.Where(e => e.Length > 0))
        {
            if (entry.Name.Equals(SettingsFile, StringComparison.OrdinalIgnoreCase))
                settings = new StreamReader(entry.Open()).ReadToEnd();
            else if (entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !entry.Name.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            {
                var name = SafeName(entry.Name);
                if (files.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new ClientModuleException($"The .zip has two files named '{name}'.");
                files.Add(new PackageFile(name, Read(entry)));
            }
        }
        if (files.Count == 0) throw new ClientModuleException("The .zip has no assemblies (.dll).");
        return FromAssemblies(files, settings);
    }

    private static InspectedPackage FromNuPkg(byte[] content)
    {
        using var zip = OpenZip(content);
        string? id = null, version = null, title = null;
        var nuspec = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/'))
                     ?? throw new ClientModuleException("The package has no .nuspec: not a NuGet package.");
        using (var stream = nuspec.Open())
        {
            var metadata = XDocument.Load(stream).Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata");
            string? Value(string name) => metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
            (id, version, title) = (Value("id"), Value("version"), Value("title"));
        }

        // The newest target framework folder under lib/.
        var libs = zip.Entries
            .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        && !e.Name.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.FullName.Split('/')[1], StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? throw new ClientModuleException("The package has no assemblies under lib/.");

        var assemblies = libs.Select(e => new PackageFile(SafeName(e.Name), Read(e))).ToList();
        var inspected = FromAssemblies(assemblies, settingsJson: null, preferredName: id);

        var packageVersion = NormalizeVersion(version) ?? inspected.Version;
        var detected = inspected.Detected with { Name = inspected.Detected.Name ?? title };
        var stored = new PackageFile(SafeName($"{inspected.Id}.{packageVersion}.nupkg"), content);
        return inspected with { Kind = ClientModuleKind.NuPkg, Version = packageVersion, Detected = detected, Files = [stored] };
    }

    private static InspectedPackage FromAssemblies(List<PackageFile> files, string? settingsJson, string? preferredName = null)
    {
        if (files.Count == 0) throw new ClientModuleException("No assemblies to install.");

        var read = files.Select(f => (File: f, Info: ReadAssembly(f))).ToList();
        var warnings = new List<string>();

        foreach (var (file, info) in read)
            foreach (var reference in info.References.Where(r => ServerOnly.Contains(r, StringComparer.OrdinalIgnoreCase)))
            {
                if (reference.Equals(ServerOnly[0], StringComparison.OrdinalIgnoreCase))
                    throw new ClientModuleException($"{file.Name} is a WebApi (server) module: install it under Host modules, not as a client module.");
                warnings.Add($"{file.Name} references {reference}, a server-side framework; it will fail in the browser when that code runs.");
            }

        // The module: the assembly with [ClientModule]; otherwise the one with pages or layouts.
        var marked = read.Where(r => r.Info.Attribute is not null).ToList();
        if (marked.Count > 1)
            throw new ClientModuleException($"More than one assembly has [assembly: ClientModule]: {string.Join(", ", marked.Select(m => m.File.Name))}. Upload one module per package.");
        var primary = marked.FirstOrDefault();
        if (primary.File is null)
        {
            var candidates = read.Where(r => r.Info.Routes.Count > 0 || r.Info.Layouts.Count > 0).ToList();
            if (preferredName is not null && candidates.Count > 1)
                candidates = candidates.Where(c => Path.GetFileNameWithoutExtension(c.File.Name).Equals(preferredName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count != 1)
                throw new ClientModuleException("No assembly has [assembly: ClientModule], and pages or layouts do not single one out. Add the attribute to the module.");
            primary = candidates[0];
            warnings.Add($"{primary.File.Name} has no [assembly: ClientModule]; its settings are guessed from its pages.");
        }
        if (!primary.Info.References.Any(r => r.StartsWith("Microsoft.AspNetCore.Components", StringComparison.OrdinalIgnoreCase)))
            warnings.Add($"{primary.File.Name} does not reference Blazor (Microsoft.AspNetCore.Components).");
        CheckPlatformVersion(primary.File.Name, primary.Info.PlatformVersion, warnings);

        var attribute = primary.Info.Attribute ?? new Dictionary<string, object?>();
        var id = attribute.GetValueOrDefault("Id") as string
                 ?? (primary.Info.Name.StartsWith(AssemblyPrefix, StringComparison.Ordinal) ? primary.Info.Name[AssemblyPrefix.Length..] : primary.Info.Name);
        RequireId(id);

        var detected = Detect(attribute, primary.Info.Routes, primary.Info.Texts);
        if (settingsJson is not null) detected = Overlay(detected, settingsJson);
        if (detected.LoadStrategy == ClientModuleLoadStrategy.Startup && detected.RequiresAuth)
            warnings.Add("Startup module that requires sign-in: it is only loaded after sign-in. Set RequiresAuth = false for layouts and themes.");
        if (attribute.GetValueOrDefault("HostDependencies") is string[] { Length: > 0 } hostDependencies)
            warnings.Add($"Needs these host modules: {string.Join(", ", hostDependencies)}. Install them under Host modules.");
        if (primary.Info.Routes.Count == 0 && primary.Info.Layouts.Count == 0)
            warnings.Add("No pages (@page) and no layouts ([LayoutKey]) were found.");

        var menu = Menu(id, primary.Info.Menu, detected.RoutePrefixes, primary.Info.Texts, warnings);
        var contributions = Contributions(id, primary.Info.Contributions, primary.Info.Types, primary.Info.Texts, warnings);

        var ordered = new List<PackageFile> { primary.File };
        ordered.AddRange(files.Where(f => f != primary.File).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase));

        return new InspectedPackage(ClientModuleKind.Dll, id, primary.Info.Version, detected, ordered,
            primary.Info.Routes, primary.Info.Layouts, menu, warnings)
        {
            Contributions = contributions,
            LayoutDetails = primary.Info.LayoutDetails,
            PlatformVersion = primary.Info.PlatformVersion?.ToString(3)
        };
    }

    /// <summary>
    /// The module was compiled against ModularBlazor.Abstractions <paramref name="built"/>: a newer major than this
    /// server's contracts is refused (what it calls may not exist here); a newer minor or an older major is installed with
    /// a warning. A module that does not reference the contracts is not checked.
    /// </summary>
    internal static void CheckPlatformVersion(string file, Version? built, List<string> warnings)
    {
        if (built is null) return;
        var host = HostPlatformVersion;
        if (built.Major > host.Major)
            throw new ClientModuleException($"{file} is built for platform {built.ToString(3)}, newer than this server ({host.ToString(3)}). Update the platform first.");
        if (built.Major < host.Major)
            warnings.Add($"{file} is built for platform {built.ToString(3)}, an older major version than this server ({host.ToString(3)}); rebuild it against the current Module SDK.");
        else if (built.Minor > host.Minor)
            warnings.Add($"{file} is built for platform {built.ToString(3)}, newer than this server ({host.ToString(3)}); features it uses may be missing.");
    }

    // ---------------------------------------------------------------- settings

    private static ClientModuleSettings Detect(IReadOnlyDictionary<string, object?> a, IReadOnlyList<string> routes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts)
    {
        string[] Strings(string name) => a.GetValueOrDefault(name) as string[] ?? [];
        var prefixes = Strings("RoutePrefixes");
        if (prefixes.Length == 0)
            prefixes = routes.Select(FirstSegment).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var nav = a.GetValueOrDefault("NavTitle") is string title && a.GetValueOrDefault("NavHref") is string href
            ? new[] { new ClientModuleNavItem(title, href, a.GetValueOrDefault("NavOrder") as int? ?? 100, Translations(title, texts)) }
            : [];

        return new ClientModuleSettings
        {
            Name = a.GetValueOrDefault("Name") as string,
            LoadStrategy = a.GetValueOrDefault("LoadOnStartup") is true ? ClientModuleLoadStrategy.Startup : ClientModuleLoadStrategy.Lazy,
            Hidden = a.GetValueOrDefault("Hidden") is true,
            RequiresAuth = a.GetValueOrDefault("RequiresAuth") as bool? ?? true,
            RequiredRole = a.GetValueOrDefault("RequiredRole") as string,
            AvailableToRoles = Strings("AvailableToRoles"),
            Layout = a.GetValueOrDefault("Layout") as string,
            RoutePrefixes = prefixes,
            Dependencies = Strings("Dependencies"),
            Styles = Strings("Styles"),
            Scripts = Strings("Scripts"),
            Nav = nav
        };
    }

    /// <summary>client-module.json: the properties it sets replace the detected ones.</summary>
    private static ClientModuleSettings Overlay(ClientModuleSettings detected, string json)
    {
        try
        {
            var target = JsonSerializer.SerializeToNode(detected, Json)!.AsObject();
            foreach (var (name, value) in JsonNode.Parse(json)?.AsObject() ?? [])
            {
                var key = target.Select(p => p.Key).FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
                target[key] = value?.DeepClone();
            }
            return target.Deserialize<ClientModuleSettings>(Json) ?? detected;
        }
        catch (JsonException e)
        {
            throw new ClientModuleException($"{SettingsFile} is not valid: {e.Message}");
        }
    }

    private static string? FirstSegment(string template)
    {
        var segment = template.Trim('/').Split('/', 2)[0];
        return segment.Length == 0 || segment.StartsWith('{') ? null : segment;
    }

    // ---------------------------------------------------------------- menu

    /// <summary>One [assembly: MenuLink] or [assembly: MenuGroup] as written: its constructor arguments and named ones.</summary>
    private sealed record MenuDeclaration(string Kind, IReadOnlyList<string?> Arguments, IReadOnlyDictionary<string, object?> Named);

    /// <summary>
    /// The module's menu, checked: ids are made unique across modules ("{module}:{id}"), a parent without ':' is one of
    /// the module's own groups, a link goes to one of the module's pages (under its route prefixes). What does not fit is
    /// left out with a warning; the module still installs.
    /// </summary>
    private static List<ClientModuleMenuItem> Menu(string moduleId, IReadOnlyList<MenuDeclaration> declarations, IReadOnlyList<string> prefixes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts, List<string> warnings)
    {
        var items = new List<ClientModuleMenuItem>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in declarations)
        {
            var group = d.Kind == "group";
            var what = group ? "MenuGroup" : "MenuLink";
            var local = d.Arguments.ElementAtOrDefault(0)?.Trim() ?? "";
            var title = d.Arguments.ElementAtOrDefault(1)?.Trim() ?? "";
            var href = (group ? d.Named.GetValueOrDefault("Href") as string : d.Arguments.ElementAtOrDefault(2))?.Trim();
            string? Named(string name) => d.Named.GetValueOrDefault(name) is string { Length: > 0 } s ? s.Trim() : null;

            if (!MenuIdPattern().IsMatch(local)) { warnings.Add($"{what} '{local}': the id must be 1-64 letters, digits, '-', '_' or '.'; left out."); continue; }
            if (title.Length is 0 or > 100) { warnings.Add($"{what} '{local}': the title must be 1-100 characters; left out."); continue; }
            if (!ids.Add(local)) { warnings.Add($"{what} '{local}': another menu entry of the module has this id; left out."); continue; }

            if (href is { Length: > 0 })
            {
                if (Uri.TryCreate(href, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https" || href.StartsWith("//", StringComparison.Ordinal))
                {
                    warnings.Add($"{what} '{local}': '{href}' is an external link; a module's menu links go to its own pages (an administrator adds external links). Left out.");
                    continue;
                }
                if (href.Contains(':') || href.Contains('\\'))
                {
                    warnings.Add($"{what} '{local}': '{href}' is not a page of the module; left out.");
                    continue;
                }
                href = href.TrimStart('/');
                var segment = href.Split(['/', '?', '#'], 2)[0];
                if (!prefixes.Any(p => p.Equals(segment, StringComparison.OrdinalIgnoreCase)))
                {
                    warnings.Add($"{what} '{local}': '{href}' is not under the module's routes ({string.Join(", ", prefixes.DefaultIfEmpty("none"))}); left out.");
                    continue;
                }
            }
            else if (!group) { warnings.Add($"MenuLink '{local}': it has no link; left out."); continue; }
            else href = null;

            var zone = Named("Zone")?.ToLowerInvariant() ?? "main";
            if (!MenuIdPattern().IsMatch(zone)) { warnings.Add($"{what} '{local}': '{zone}' is not a valid zone; left out."); continue; }
            if (zone is not ("main" or "secondary" or "user"))
                warnings.Add($"{what} '{local}': zone '{zone}' is not one of main, secondary, user; only layouts that show it will.");

            var parent = Named("Parent");
            if (parent is not null && !parent.Contains(':')) parent = $"{moduleId}:{parent}";

            // The badge: the module's own API (relative "api/..."), asked by the client with the user's token.
            var badge = BadgeUrl(what, local, Named("Badge"), warnings);
            var refresh = badge is null ? 0 : d.Named.GetValueOrDefault("BadgeRefresh") as int? ?? 0;
            if (refresh != 0) refresh = Math.Clamp(refresh, 15, 3600);

            items.Add(new ClientModuleMenuItem($"{moduleId}:{local}", group ? "group" : "link", zone, title, href, parent, Named("Icon"),
                d.Named.GetValueOrDefault("Order") as int? ?? 100, Translations(title, texts), Badge: badge, BadgeRefresh: refresh,
                Permissions: Permissions(what, local, d.Named, warnings)));
        }

        // A parent of the module's own that it does not declare: shown at the zone's top level instead.
        var own = items.Where(i => i.Kind == "group").Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Where(i => i.Parent is { } p && p.StartsWith(moduleId + ":", StringComparison.OrdinalIgnoreCase) && !own.Contains(p)))
            warnings.Add($"Menu '{item.Id}': the module has no MenuGroup '{item.Parent![(moduleId.Length + 1)..]}'; it is shown at the top of its zone.");
        return items;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex MenuIdPattern();

    /// <summary>
    /// A Permissions argument, checked: invalid names are dropped with a warning. Null when there are none; when every
    /// name is invalid the entry gets an unknown permission, so it is hidden rather than shown to everyone.
    /// </summary>
    private static IReadOnlyList<string>? Permissions(string what, string local, IReadOnlyDictionary<string, object?> named, List<string> warnings)
    {
        if (named.GetValueOrDefault("Permissions") is not string[] { Length: > 0 } values) return null;
        var kept = new List<string>();
        foreach (var value in values.Select(v => v.Trim()))
        {
            if (!PermissionPattern().IsMatch(value)) warnings.Add($"{what} '{local}': '{value}' is not a valid permission name; it is ignored.");
            else if (!kept.Contains(value, StringComparer.Ordinal)) kept.Add(value);
        }
        if (kept.Count > 0) return kept;
        warnings.Add($"{what} '{local}': none of its permissions is valid, so nobody will see it.");
        return [InvalidPermission];
    }

    /// <summary>A permission nobody holds (every name a module gave was invalid).</summary>
    internal const string InvalidPermission = "invalid-permission";

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,99}$")]
    private static partial Regex PermissionPattern();

    /// <summary>A badge url, checked: the module's own API (relative "api/..."), asked with the user's token. Null when invalid.</summary>
    private static string? BadgeUrl(string what, string local, string? value, List<string> warnings)
    {
        var badge = value?.Trim().TrimStart('/');
        if (string.IsNullOrEmpty(badge)) return null;
        if (!badge.StartsWith("api/", StringComparison.OrdinalIgnoreCase) || badge.Contains("//") || badge.Contains('\\')
            || badge.Split(['?', '#'], 2)[0].Contains(':') || badge.Length > 500)
        {
            warnings.Add($"{what} '{local}': the badge '{badge}' is not an API url of the WebApi (api/...); the entry is kept without it.");
            return null;
        }
        return badge;
    }

    // ---------------------------------------------------------------- shell contributions

    /// <summary>One [assembly: HeaderAction / Dock / ContextPanel] as written: its constructor arguments and named ones.</summary>
    private sealed record ContributionDeclaration(string Kind, IReadOnlyList<string?> Arguments, IReadOnlyDictionary<string, object?> Named);

    /// <summary>
    /// The module's header actions, docks and context panels, checked like its menu: ids made unique ("{module}:{id}"),
    /// the component must be a type of the module's own assembly, routes and context keys well formed. What does not fit
    /// is left out with a warning; the module still installs.
    /// </summary>
    private static List<ClientModuleContribution> Contributions(string moduleId, IReadOnlyList<ContributionDeclaration> declarations,
        IReadOnlySet<string> types, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts, List<string> warnings)
    {
        var items = new List<ClientModuleContribution>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in declarations)
        {
            var what = d.Kind switch { "header-action" => "HeaderAction", "dock" => "Dock", _ => "ContextPanel" };
            var local = d.Arguments.ElementAtOrDefault(0)?.Trim() ?? "";
            var title = d.Arguments.ElementAtOrDefault(1)?.Trim() ?? "";
            var component = d.Arguments.ElementAtOrDefault(2)?.Split(',', 2)[0].Trim() ?? "";
            string? Named(string name) => d.Named.GetValueOrDefault(name) is string { Length: > 0 } s ? s.Trim() : null;

            if (!MenuIdPattern().IsMatch(local)) { warnings.Add($"{what} '{local}': the id must be 1-64 letters, digits, '-', '_' or '.'; left out."); continue; }
            if (title.Length is 0 or > 100) { warnings.Add($"{what} '{local}': the title must be 1-100 characters; left out."); continue; }
            if (!ids.Add(local)) { warnings.Add($"{what} '{local}': another contribution of the module has this id; left out."); continue; }
            if (!types.Contains(component)) { warnings.Add($"{what} '{local}': its component '{component}' is not a type of the module's own assembly; left out."); continue; }

            var badge = d.Kind == "header-action" ? null : BadgeUrl(what, local, Named("Badge"), warnings);
            var refresh = badge is null ? 0 : d.Named.GetValueOrDefault("BadgeRefresh") as int? ?? 0;
            if (refresh != 0) refresh = Math.Clamp(refresh, 15, 3600);

            List<string>? routes = null, context = null;
            if (d.Kind == "context-panel")
            {
                routes = [];
                foreach (var r in Strings(d.Named, "ForRoutes").Select(r => r.Trim().Trim('/')))
                {
                    if (RoutePattern().IsMatch(r) && !r.Split('/').SkipLast(1).Contains("**")) { if (!routes.Contains(r, StringComparer.OrdinalIgnoreCase)) routes.Add(r); }
                    else warnings.Add($"ContextPanel '{local}': '{r}' is not a page path pattern (segments, '*', a final '**'); it is ignored.");
                }
                context = [];
                foreach (var k in Strings(d.Named, "ForContext").Select(k => k.Trim()))
                {
                    if (ContextKeyPattern().IsMatch(k)) { if (!context.Contains(k, StringComparer.OrdinalIgnoreCase)) context.Add(k); }
                    else warnings.Add($"ContextPanel '{local}': '{k}' is not a context key like 'content.post'; it is ignored.");
                }
                if (routes.Count == 0 && context.Count == 0)
                {
                    warnings.Add($"ContextPanel '{local}': it names no page (ForRoutes) and no context key (ForContext), so it would never show; left out.");
                    continue;
                }
                if (routes.Count == 0) routes = null;
                if (context.Count == 0) context = null;
            }

            items.Add(new ClientModuleContribution($"{moduleId}:{local}", d.Kind, title, component, Named("Icon"),
                d.Named.GetValueOrDefault("Order") as int? ?? 100, Translations(title, texts),
                Permissions(what, local, d.Named, warnings), badge, refresh, routes, context));
        }
        return items;
    }

    private static string[] Strings(IReadOnlyDictionary<string, object?> named, string name) => named.GetValueOrDefault(name) as string[] ?? [];

    /// <summary>Page path patterns: up to 16 segments, each a literal, '*' or '**'.</summary>
    [GeneratedRegex(@"^(\*\*|\*|[A-Za-z0-9._~-]+)(/(\*\*|\*|[A-Za-z0-9._~-]+)){0,15}$")]
    private static partial Regex RoutePattern();

    /// <summary>"{module}.{entity}" (2-4 lower-case parts): "content.post", "shifts.roster.day".</summary>
    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,49}(\.[a-z0-9][a-z0-9-]{0,49}){1,3}$")]
    private static partial Regex ContextKeyPattern();

    // ---------------------------------------------------------------- translations

    /// <summary>The menu title in every language the module translates it into (culture → text).</summary>
    private static Dictionary<string, string>? Translations(string title, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts)
    {
        var titles = texts
            .Where(t => t.Value.TryGetValue(title, out var text) && !string.IsNullOrWhiteSpace(text))
            .ToDictionary(t => t.Key, t => t.Value[title], StringComparer.OrdinalIgnoreCase);
        return titles.Count == 0 ? null : titles;
    }

    /// <summary>
    /// The module's embedded translations (resources "…i18n.{culture}.json", see Share.All's EmbeddedJsonStringLocalizerFactory),
    /// read as data: culture → flat key/text map. A file that is not a JSON object of strings is skipped.
    /// </summary>
    private static Dictionary<string, IReadOnlyDictionary<string, string>> ReadTexts(PEReader pe, MetadataReader md)
    {
        var texts = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (pe.PEHeaders.CorHeader is not { } cor || cor.ResourcesDirectory.Size == 0) return texts;
        var block = pe.GetSectionData(cor.ResourcesDirectory.RelativeVirtualAddress);
        foreach (var handle in md.ManifestResources)
        {
            var resource = md.GetManifestResource(handle);
            if (!resource.Implementation.IsNil) continue; // linked or in another assembly
            if (TranslationResource().Match(md.GetString(resource.Name)) is not { Success: true } match) continue;
            try
            {
                var reader = block.GetReader((int)resource.Offset, block.Length - (int)resource.Offset);
                var bytes = reader.ReadBytes(reader.ReadInt32());
                using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                texts[match.Groups["culture"].Value] = document.RootElement.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.String && !p.Name.StartsWith('$'))
                    .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
            }
            catch (Exception e) when (e is JsonException or BadImageFormatException or ArgumentOutOfRangeException)
            {
                // A broken file only costs the translated menu title; the module itself still installs.
            }
        }
        return texts;
    }

    [GeneratedRegex(@"(?:^|[./\\])i18n[./\\](?<culture>[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*)\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex TranslationResource();

    // ---------------------------------------------------------------- metadata

    private sealed record AssemblyInfo(string Name, string Version, IReadOnlyDictionary<string, object?>? Attribute,
        IReadOnlyList<string> Routes, IReadOnlyList<string> Layouts, IReadOnlyList<string> References,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Texts, IReadOnlyList<MenuDeclaration> Menu)
    {
        public IReadOnlyList<ContributionDeclaration> Contributions { get; init; } = [];
        public IReadOnlyList<ClientModuleLayout> LayoutDetails { get; init; } = [];

        /// <summary>Full names of the types the assembly defines ("Ns.Type", nested "Ns.Outer+Inner").</summary>
        public IReadOnlySet<string> Types { get; init; } = new HashSet<string>();

        /// <summary>The version of ModularBlazor.Abstractions it references (null: none).</summary>
        public Version? PlatformVersion { get; init; }
    }

    private static AssemblyInfo ReadAssembly(PackageFile file)
    {
        try
        {
            using var pe = new PEReader(new MemoryStream(file.Content));
            if (!pe.HasMetadata) throw new ClientModuleException($"{file.Name} is not a .NET assembly.");
            var md = pe.GetMetadataReader();
            if (!md.IsAssembly) throw new ClientModuleException($"{file.Name} is a .NET module, not an assembly.");

            var assembly = md.GetAssemblyDefinition();
            IReadOnlyDictionary<string, object?>? client = null;
            string? informational = null;
            var menu = new List<MenuDeclaration>();
            var contributions = new List<ContributionDeclaration>();
            foreach (var handle in assembly.GetCustomAttributes())
            {
                var attribute = md.GetCustomAttribute(handle);
                var name = AttributeName(md, attribute);
                switch (name)
                {
                    case ClientAttribute: client = Values(attribute); break;
                    case MenuLinkAttribute: menu.Add(new MenuDeclaration("link", Arguments(attribute), Values(attribute))); break;
                    case MenuGroupAttribute: menu.Add(new MenuDeclaration("group", Arguments(attribute), Values(attribute))); break;
                    case "System.Reflection.AssemblyInformationalVersionAttribute": informational = FirstString(attribute); break;
                    default:
                        if (ContributionAttributes.TryGetValue(name, out var kind))
                            contributions.Add(new ContributionDeclaration(kind, Arguments(attribute), Values(attribute)));
                        break;
                }
            }

            var routes = new List<string>();
            var layouts = new List<string>();
            var layoutDetails = new List<ClientModuleLayout>();
            var types = new HashSet<string>(StringComparer.Ordinal);
            foreach (var handle in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(handle);
                types.Add(FullName(md, type));
                string? key = null, role = null;
                Dictionary<string, object?>? info = null;
                foreach (var a in type.GetCustomAttributes())
                {
                    var attribute = md.GetCustomAttribute(a);
                    switch (AttributeName(md, attribute))
                    {
                        case RouteAttribute when FirstString(attribute) is { } template: routes.Add(template); break;
                        case LayoutKeyAttribute when FirstString(attribute) is { } k: layouts.Add(key = k); break;
                        case LayoutInfoAttribute: (role, info) = (FirstString(attribute), Values(attribute)); break;
                    }
                }
                if (key is not null && info is not null)
                    layoutDetails.Add(new ClientModuleLayout(key, role,
                        info.GetValueOrDefault("Sections") as string[] ?? [],
                        info.GetValueOrDefault("Zones") as string[] ?? [],
                        info.GetValueOrDefault("Contributions") as string[] ?? []));
            }

            var references = md.AssemblyReferences.Select(md.GetAssemblyReference).ToList();
            var platform = references.Where(r => md.GetString(r.Name).Equals(AbstractionsAssembly, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Version).FirstOrDefault();
            var version = NormalizeVersion(informational) ?? assembly.Version.ToString(3);
            return new AssemblyInfo(md.GetString(assembly.Name), version, client, routes.Distinct().Order().ToList(), layouts.Distinct().ToList(),
                references.Select(r => md.GetString(r.Name)).ToList(), ReadTexts(pe, md), menu)
            {
                Contributions = contributions,
                LayoutDetails = layoutDetails,
                Types = types,
                PlatformVersion = platform
            };
        }
        catch (BadImageFormatException)
        {
            throw new ClientModuleException($"{file.Name} is not a .NET assembly.");
        }
    }

    private static string Join(string ns, string name) => ns.Length == 0 ? name : $"{ns}.{name}";

    /// <summary>"Ns.Type", or "Ns.Outer+Inner" for a nested type: the name typeof() writes into an attribute.</summary>
    private static string FullName(MetadataReader md, TypeDefinition type)
    {
        var name = md.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        return declaring.IsNil ? Join(md.GetString(type.Namespace), name) : $"{FullName(md, md.GetTypeDefinition(declaring))}+{name}";
    }

    private static string AttributeName(MetadataReader md, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                return member.Parent.Kind == HandleKind.TypeReference ? TypeRefName(md, (TypeReferenceHandle)member.Parent) : "";
            case HandleKind.MethodDefinition:
                var method = md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                var declaring = md.GetTypeDefinition(method.GetDeclaringType());
                return Join(md.GetString(declaring.Namespace), md.GetString(declaring.Name));
            default:
                return "";
        }
    }

    private static string TypeRefName(MetadataReader md, TypeReferenceHandle handle)
    {
        var reference = md.GetTypeReference(handle);
        return Join(md.GetString(reference.Namespace), md.GetString(reference.Name));
    }

    private static string? FirstString(CustomAttribute attribute)
    {
        var value = attribute.DecodeValue(TypeProvider.Instance);
        return value.FixedArguments.Length > 0 ? value.FixedArguments[0].Value as string : null;
    }

    /// <summary>The constructor arguments of an attribute that are strings (others: null).</summary>
    private static List<string?> Arguments(CustomAttribute attribute) =>
        attribute.DecodeValue(TypeProvider.Instance).FixedArguments.Select(a => a.Value as string).ToList();

    /// <summary>Named arguments of an attribute (arrays become string[]).</summary>
    private static Dictionary<string, object?> Values(CustomAttribute attribute)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var named in attribute.DecodeValue(TypeProvider.Instance).NamedArguments)
        {
            if (named.Name is null) continue;
            result[named.Name] = named.Value is ImmutableArray<CustomAttributeTypedArgument<string>> array
                ? array.Select(a => a.Value?.ToString() ?? "").ToArray()
                : named.Value;
        }
        return result;
    }

    private sealed class TypeProvider : ICustomAttributeTypeProvider<string>
    {
        public static readonly TypeProvider Instance = new();
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSystemType() => "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => Join(reader.GetString(reader.GetTypeDefinition(handle).Namespace), reader.GetString(reader.GetTypeDefinition(handle).Name));
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => TypeRefName(reader, handle);
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(string type) => type == "System.Type";
    }

    // ---------------------------------------------------------------- names

    private static ZipArchive OpenZip(byte[] content)
    {
        try { return new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read); }
        catch (InvalidDataException) { throw new ClientModuleException("The file is not a valid .zip / .nupkg archive."); }
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>"1.2.3+abc" → "1.2.3"; null when it is not a usable version.</summary>
    private static string? NormalizeVersion(string? version)
    {
        var value = version?.Split('+', 2)[0].Trim();
        return value is { Length: > 0 } && VersionPattern().IsMatch(value) ? value : null;
    }

    public static void RequireId(string id)
    {
        if (!IdPattern().IsMatch(id))
            throw new ClientModuleException($"'{id}' is not a valid module id (letters, digits, '.', '_', '-'; at most 100).");
    }

    public static bool IsSafeName(string name) => FilePattern().IsMatch(name) && name is not "." and not "..";

    private static string SafeName(string name)
    {
        var file = Path.GetFileName(name);
        return IsSafeName(file) ? file : throw new ClientModuleException($"'{name}' is not an allowed file name (letters, digits, '.', '_', '-').");
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")]
    public static partial Regex IdPattern();

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.-]{0,49}$")]
    public static partial Regex VersionPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,200}$")]
    private static partial Regex FilePattern();
}

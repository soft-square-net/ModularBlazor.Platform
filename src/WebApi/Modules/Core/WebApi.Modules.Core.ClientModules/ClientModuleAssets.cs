using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;

namespace soft.square.net.WebApi.Modules.Core.ClientModules;

/// <summary>One static file of a version (css, js, a font...), by its path under the module's wwwroot.</summary>
public sealed record ClientModuleAsset(string Path, string Sha256, long Size);

/// <summary>
/// The static files of a client module, taken out of what the module already ships, so the browser can load them
/// from real URLs instead of blobs (code splitting, relative <c>url(...)</c> and <c>import</c> work, nothing stays in
/// the WASM heap). Nothing is run: assemblies are read with System.Reflection.Metadata.
/// <list type="bullet">
/// <item>Every assembly's "module-assets/&lt;path&gt;" resources (the Module SDK embeds wwwroot that way).</item>
/// <item>A .nupkg's <c>staticwebassets/</c> files (they win over embedded ones, as on the client).</item>
/// </list>
/// Stored under <c>files/{id}/{version}/assets/</c> and served at
/// <c>files/{id}/{version}/{token}/{path}</c>, the token changing with the content.
/// </summary>
internal static class ClientModuleAssets
{
    public const string ResourcePrefix = "module-assets/";
    public const string StaticWebAssetsPrefix = "staticwebassets/";
    public const string Folder = "assets";

    /// <summary>Limits: a module's assets are code and styles, not media libraries.</summary>
    public const int MaxFiles = 2000;
    public const long MaxBytes = 64L * 1024 * 1024;

    private static readonly FileExtensionContentTypeProvider ContentTypes = CreateContentTypes();

    /// <summary>The assets in a version's stored files (primary first). Later sources win for the same path.</summary>
    public static IReadOnlyList<(string Path, byte[] Content)> Extract(ClientModuleKind kind, IEnumerable<(string Name, byte[] Content)> files)
    {
        var assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, content) in files)
        {
            if (kind == ClientModuleKind.NuPkg && name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)) ReadNuPkg(content, assets);
            else ReadEmbedded(content, assets);
        }
        if (assets.Count > MaxFiles) throw new ClientModuleException($"The module has {assets.Count} static files; at most {MaxFiles} are served.");
        if (assets.Values.Sum(a => (long)a.Length) > MaxBytes)
            throw new ClientModuleException($"The module's static files are larger than {MaxBytes / 1024 / 1024} MB.");
        return assets.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => (a.Key, a.Value)).ToList();
    }

    public static ClientModuleAsset Describe(string path, byte[] content) =>
        new(path, Convert.ToHexStringLower(SHA256.HashData(content)), content.LongLength);

    /// <summary>Identifies a set of assets: equal for the same paths and contents.</summary>
    public static string SetHash(IEnumerable<ClientModuleAsset> assets) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('\n', assets.OrderBy(a => a.Path, StringComparer.Ordinal).Select(a => $"{a.Path}:{a.Sha256}")))))[..16];

    /// <summary>
    /// "css/app.css" from "wwwroot\css\app.css", "/css/app.css" or "_content/My.Module/css/app.css" (as the client
    /// normalizes). Null when the path is not a plain relative path (.., a drive, an empty segment).
    /// </summary>
    public static string? Normalize(string path)
    {
        var p = path.Replace('\\', '/').TrimStart('/');
        if (p.StartsWith("_content/", StringComparison.OrdinalIgnoreCase))
        {
            var slash = p.IndexOf('/', "_content/".Length);
            p = slash < 0 ? "" : p[(slash + 1)..];
        }
        if (p.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase)) p = p["wwwroot/".Length..];
        if (p.Length is 0 or > 400) return null;
        foreach (var segment in p.Split('/'))
            if (segment.Length == 0 || segment is "." or ".." || segment.Contains(':') || segment.Any(char.IsControl)) return null;
        return p;
    }

    /// <summary>The Content-Type to serve a file with (JavaScript as text/javascript: import() checks it).</summary>
    public static string ContentType(string path) =>
        ContentTypes.TryGetContentType(path, out var type) ? type : "application/octet-stream";

    private static FileExtensionContentTypeProvider CreateContentTypes()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".js"] = "text/javascript";
        provider.Mappings[".mjs"] = "text/javascript";
        provider.Mappings[".map"] = "application/json";
        provider.Mappings[".wasm"] = "application/wasm";
        provider.Mappings[".webmanifest"] = "application/manifest+json";
        return provider;
    }

    private static void ReadNuPkg(byte[] content, Dictionary<string, byte[]> assets)
    {
        using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        var libs = zip.Entries
            .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        && !e.Name.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.FullName.Split('/')[1], StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        foreach (var lib in libs ?? Enumerable.Empty<ZipArchiveEntry>()) ReadEmbedded(ReadAll(lib), assets);

        foreach (var entry in zip.Entries.Where(e => e.Length > 0 && e.FullName.StartsWith(StaticWebAssetsPrefix, StringComparison.OrdinalIgnoreCase)))
            if (Normalize(entry.FullName[StaticWebAssetsPrefix.Length..]) is { } path) assets[path] = ReadAll(entry);
    }

    private static void ReadEmbedded(byte[] image, Dictionary<string, byte[]> assets)
    {
        try
        {
            using var pe = new PEReader(new MemoryStream(image));
            if (!pe.HasMetadata) return;
            var cor = pe.PEHeaders.CorHeader;
            if (cor is null || cor.ResourcesDirectory.Size == 0) return;

            var metadata = pe.GetMetadataReader();
            var block = pe.GetSectionData(cor.ResourcesDirectory.RelativeVirtualAddress);
            foreach (var handle in metadata.ManifestResources)
            {
                var resource = metadata.GetManifestResource(handle);
                if (!resource.Implementation.IsNil) continue; // in another file
                var name = metadata.GetString(resource.Name);
                if (!name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (Normalize(name[ResourcePrefix.Length..]) is not { } path) continue;

                var offset = (int)resource.Offset;
                var reader = block.GetReader(offset, block.Length - offset);
                var length = reader.ReadInt32(); // resources are length-prefixed
                assets[path] = reader.ReadBytes(length);
            }
        }
        catch (BadImageFormatException)
        {
            // Not an assembly the inspector would have accepted; nothing to extract.
        }
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

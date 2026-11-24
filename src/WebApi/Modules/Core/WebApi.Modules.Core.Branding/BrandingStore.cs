using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace soft.square.net.WebApi.Modules.Core.Branding;

/// <summary>
/// The brand layers and their image files (singleton), in files every replica shares, like the menus:
/// <c>{Path}/platform.json</c>, <c>{Path}/tenants/{id}.json</c> and <c>{Path}/assets/{scope}/{kind}-{hash}.{ext}</c>.
/// Saves happen under a cross-process lock file and replace the file atomically; reads use the last saved one.
/// An image is never overwritten: its name carries its hash, so it is cached for good.
/// </summary>
internal sealed partial class BrandingStore
{
    public const string PlatformScope = "platform";

    /// <summary>The images a layer has: the logo, the logo on dark backgrounds, the favicon.</summary>
    public static readonly IReadOnlyList<string> AssetKinds = ["logo", "logo-dark", "favicon"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root;
    private readonly Dictionary<string, (DateTime Written, long Length, BrandLayer Value)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _cacheLock = new();

    public BrandingStore(IOptions<BrandingOptions> options, IHostEnvironment environment)
    {
        _root = Path.GetFullPath(options.Value.Path, environment.ContentRootPath);
        Directory.CreateDirectory(Path.Combine(_root, "tenants"));
        Directory.CreateDirectory(Path.Combine(_root, "assets"));
    }

    /// <summary>The brand for a tenant (or for no tenant): the platform's layer, then the tenant's.</summary>
    public EffectiveBrand Effective(string? tenant) =>
        BrandRules.Combine(Read(PlatformScope), tenant is null ? null : Read(tenant));

    /// <summary>A scope's layer: <see cref="PlatformScope"/> or a tenant id. Never saved yet: empty, version 0.</summary>
    public BrandLayer Read(string scope)
    {
        var info = new FileInfo(FileOf(scope));
        if (!info.Exists) return new BrandLayer();
        lock (_cacheLock)
            if (_cache.TryGetValue(scope, out var cached) && cached.Written == info.LastWriteTimeUtc && cached.Length == info.Length)
                return cached.Value;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var value = JsonSerializer.Deserialize<BrandLayer>(File.ReadAllText(info.FullName), Json) ?? new BrandLayer();
                lock (_cacheLock) _cache[scope] = (info.LastWriteTimeUtc, info.Length, value);
                return value;
            }
            catch (IOException) when (attempt < 20) // being replaced by another replica
            {
                Thread.Sleep(25);
            }
        }
    }

    /// <summary>Replaces a scope's layer; 409 when it changed since <see cref="SaveBrandLayer.Version"/> was read.</summary>
    public async Task<BrandLayer> SaveAsync(string scope, SaveBrandLayer save, string? user, CancellationToken ct)
    {
        var layer = BrandRules.Validate(save);
        foreach (var url in new[] { layer.LogoUrl, layer.LogoDarkUrl, layer.FaviconUrl })
            if (url is not null && url.StartsWith("api/", StringComparison.Ordinal) && !url.StartsWith(AssetPrefix(scope), StringComparison.Ordinal))
                throw new BrandingException("An uploaded image can only be used by the layer it was uploaded to.");

        var path = FileOf(scope);
        using var _ = await LockAsync(path, ct);
        var current = File.Exists(path)
            ? JsonSerializer.Deserialize<BrandLayer>(await File.ReadAllTextAsync(path, ct), Json) ?? new BrandLayer()
            : new BrandLayer();
        if (current.Version != save.Version)
            throw new BrandingException($"The brand was changed by {current.UpdatedBy ?? "someone else"} meanwhile; load it again.", 409);

        var saved = layer with { Version = current.Version + 1, UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = user };
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(saved, Json), Encoding.UTF8, ct);
        File.Move(temp, path, overwrite: true);
        lock (_cacheLock) _cache.Remove(scope);
        return saved;
    }

    /// <summary>
    /// Keeps an image for a scope and answers the url to put in its layer. The file's bytes must be the image type its
    /// name says (checked by signature), at most <see cref="BrandRules.MaxImageBytes"/>.
    /// </summary>
    public async Task<BrandAsset> AddAssetAsync(string scope, string kind, string fileName, Stream content, CancellationToken ct)
    {
        if (!AssetKinds.Contains(kind)) throw new BrandingException($"'{kind}' is not an image a brand has (logo, logo-dark, favicon).", 404);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!BrandRules.ImageTypes.TryGetValue(extension, out var contentType))
            throw new BrandingException("The image must be a .png, .jpg, .webp, .svg or .ico file.");
        if (extension == ".jpeg") extension = ".jpg";

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > BrandRules.MaxImageBytes)
                throw new BrandingException($"The image must be at most {BrandRules.MaxImageBytes / 1024} KB.", 413);
            buffer.Write(chunk, 0, read);
        }
        var bytes = buffer.ToArray();
        if (!BrandRules.LooksLike(extension, bytes))
            throw new BrandingException($"The file is not a {extension[1..]} image.");

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
        var name = $"{kind}-{hash}{extension}";
        FileOf(scope); // checks the tenant id
        var folder = Path.Combine(_root, "assets", ScopeFolder(scope));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        if (!File.Exists(path))
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, path, overwrite: true); // the same bytes if another replica got there first
        }
        return new BrandAsset(AssetPrefix(scope) + name, contentType, bytes.Length);
    }

    /// <summary>An image's file and type (folder and name as in its url), or null when there is none of that name.</summary>
    public (string Path, string ContentType)? FindAsset(string folder, string name)
    {
        if (!BrandRules.AssetPath().IsMatch($"api/branding/assets/{folder}/{name}")) return null;
        var path = Path.Combine(_root, "assets", folder, name);
        return File.Exists(path) ? (path, BrandRules.ImageTypes[Path.GetExtension(name)]) : null;
    }

    private static string AssetPrefix(string scope) => $"api/branding/assets/{ScopeFolder(scope)}/";

    /// <summary>"platform", or "tenant-{id}" (so a tenant called "platform" has its own).</summary>
    private static string ScopeFolder(string scope) => scope == PlatformScope ? PlatformScope : "tenant-" + scope.ToLowerInvariant();

    private string FileOf(string scope)
    {
        if (scope == PlatformScope) return Path.Combine(_root, "platform.json");
        if (!TenantId().IsMatch(scope)) throw new BrandingException($"'{scope}' is not a tenant id.");
        return Path.Combine(_root, "tenants", scope.ToLowerInvariant() + ".json");
    }

    private static async Task<IDisposable> LockAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 400) // ~10 s
            {
                await Task.Delay(25, ct);
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$")]
    private static partial Regex TenantId();
}

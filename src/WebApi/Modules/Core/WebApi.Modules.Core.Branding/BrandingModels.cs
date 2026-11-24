using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace soft.square.net.WebApi.Modules.Core.Branding;

/// <summary>
/// One layer of the brand: the platform's or one tenant's. Every field is optional: an empty one comes from the layer
/// below (the platform's, then the built-in brand). The client app's Share.All.BrandInfo has the effective shape.
/// </summary>
public sealed record BrandLayer
{
    /// <summary>Raised on every save; a save must send the version it started from.</summary>
    public int Version { get; init; }

    public string? Name { get; init; }
    public string? ShortName { get; init; }

    /// <summary>"api/branding/assets/..." (uploaded here) or an https:// address.</summary>
    public string? LogoUrl { get; init; }
    public string? LogoDarkUrl { get; init; }
    public string? FaviconUrl { get; init; }

    /// <summary>"#rrggbb".</summary>
    public string? Primary { get; init; }
    public string? Secondary { get; init; }

    /// <summary>Corner radius in px (0-32).</summary>
    public int? Radius { get; init; }

    /// <summary>A CSS font-family list.</summary>
    public string? Font { get; init; }

    /// <summary>"light", "dark" or "system".</summary>
    public string? DefaultMode { get; init; }

    /// <summary>The modes users may pick; null or empty: all.</summary>
    public IReadOnlyList<string>? AllowedModes { get; init; }

    /// <summary>Whether users may change the colours in the theme drawer.</summary>
    public bool? AllowUserColors { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}

/// <summary>A save: the whole layer, and the version it was read at (409 when it changed since).</summary>
public sealed record SaveBrandLayer(
    int Version,
    string? Name = null,
    string? ShortName = null,
    string? LogoUrl = null,
    string? LogoDarkUrl = null,
    string? FaviconUrl = null,
    string? Primary = null,
    string? Secondary = null,
    int? Radius = null,
    string? Font = null,
    string? DefaultMode = null,
    IReadOnlyList<string>? AllowedModes = null,
    bool? AllowUserColors = null);

/// <summary>A layer for the admin page: the one being edited, and below it what it is applied on.</summary>
/// <param name="Scope">"platform" or the tenant id.</param>
/// <param name="Inherited">The platform's layer, when <paramref name="Scope"/> is a tenant.</param>
/// <param name="Effective">What the client gets for this scope once both layers are applied.</param>
public sealed record BrandAdminView(string Scope, BrandLayer Layer, BrandLayer? Inherited, EffectiveBrand Effective);

/// <summary>
/// The brand the client applies: the layers combined over the built-in brand, every field resolved. Same JSON shape
/// as Share.All.BrandInfo, plus <see cref="ETag"/>.
/// </summary>
public sealed record EffectiveBrand(
    string ETag,
    string Name,
    string? ShortName,
    string? LogoUrl,
    string? LogoDarkUrl,
    string? FaviconUrl,
    string? Primary,
    string? Secondary,
    int? Radius,
    string? Font,
    string DefaultMode,
    IReadOnlyList<string> AllowedModes,
    bool AllowUserColors);

/// <summary>An uploaded image: the url to put in a layer's LogoUrl, LogoDarkUrl or FaviconUrl.</summary>
public sealed record BrandAsset(string Url, string ContentType, long Size);

/// <summary>A rule the branding refuses; <see cref="StatusCode"/> is the HTTP answer.</summary>
public sealed class BrandingException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Validation and combination of layers. Pure: tested without a host.</summary>
internal static partial class BrandRules
{
    public const string DefaultName = "Modular Blazor";
    public static readonly IReadOnlyList<string> Modes = ["light", "dark", "system"];

    /// <summary>The image types a logo or favicon may be, by extension.</summary>
    public static readonly IReadOnlyDictionary<string, string> ImageTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon"
    };

    public const long MaxImageBytes = 512 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The layer a save describes, checked and normalized (blank strings become null).</summary>
    public static BrandLayer Validate(SaveBrandLayer save)
    {
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var name = Clean(save.Name);
        if (name is { Length: > 80 }) throw new BrandingException("The name must be at most 80 characters.");
        var shortName = Clean(save.ShortName);
        if (shortName is { Length: > 24 }) throw new BrandingException("The short name must be at most 24 characters.");

        string? Url(string? value, string what)
        {
            var url = Clean(value);
            if (url is null) return null;
            if (url.Length > 500) throw new BrandingException($"The {what} address is too long.");
            if (AssetPath().IsMatch(url)) return url;
            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme == Uri.UriSchemeHttps) return absolute.ToString();
            throw new BrandingException($"The {what} must be an uploaded image (api/branding/assets/...) or an https:// address.");
        }

        string? Color(string? value, string what)
        {
            var color = Clean(value);
            if (color is null) return null;
            if (!HexColor().IsMatch(color)) throw new BrandingException($"The {what} colour must be #rrggbb.");
            return color.ToLowerInvariant();
        }

        if (save.Radius is < 0 or > 32) throw new BrandingException("The corner radius must be between 0 and 32 px.");

        var font = Clean(save.Font);
        if (font is not null && (font.Length > 200 || !FontList().IsMatch(font)))
            throw new BrandingException("The font must be a CSS font-family list: names, quotes, commas, spaces and hyphens.");

        var mode = Clean(save.DefaultMode)?.ToLowerInvariant();
        if (mode is not null && !Modes.Contains(mode)) throw new BrandingException("The default mode must be light, dark or system.");

        IReadOnlyList<string>? allowed = null;
        if (save.AllowedModes is { Count: > 0 })
        {
            var modes = save.AllowedModes.Select(m => m.Trim().ToLowerInvariant()).Distinct().ToList();
            if (modes.Any(m => !Modes.Contains(m))) throw new BrandingException("The allowed modes must be light, dark or system.");
            allowed = modes;
        }
        if (mode is not null && allowed is not null && !allowed.Contains(mode))
            throw new BrandingException("The default mode must be one of the allowed modes.");

        return new BrandLayer
        {
            Name = name,
            ShortName = shortName,
            LogoUrl = Url(save.LogoUrl, "logo"),
            LogoDarkUrl = Url(save.LogoDarkUrl, "dark logo"),
            FaviconUrl = Url(save.FaviconUrl, "favicon"),
            Primary = Color(save.Primary, "primary"),
            Secondary = Color(save.Secondary, "secondary"),
            Radius = save.Radius,
            Font = font,
            DefaultMode = mode,
            AllowedModes = allowed,
            AllowUserColors = save.AllowUserColors
        };
    }

    /// <summary>The platform's layer, then the tenant's on top (a field the tenant set wins), over the built-in brand.</summary>
    public static EffectiveBrand Combine(BrandLayer platform, BrandLayer? tenant)
    {
        T? Pick<T>(Func<BrandLayer, T?> field) where T : class => (tenant is null ? null : field(tenant)) ?? field(platform);
        T? PickValue<T>(Func<BrandLayer, T?> field) where T : struct => (tenant is null ? null : field(tenant)) ?? field(platform);

        var allowed = Pick(l => l.AllowedModes is { Count: > 0 } m ? m : null) ?? Modes;
        var mode = Pick(l => l.DefaultMode) ?? "system";
        if (!allowed.Contains(mode)) mode = allowed[0]; // a tenant narrowed the modes below the platform's default

        var brand = new EffectiveBrand("",
            Pick(l => l.Name) ?? DefaultName,
            Pick(l => l.ShortName),
            Pick(l => l.LogoUrl),
            Pick(l => l.LogoDarkUrl),
            Pick(l => l.FaviconUrl),
            Pick(l => l.Primary),
            Pick(l => l.Secondary),
            PickValue(l => l.Radius),
            Pick(l => l.Font),
            mode,
            allowed,
            PickValue(l => l.AllowUserColors) ?? true);
        var etag = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(brand, Json)))[..32];
        return brand with { ETag = etag };
    }

    /// <summary>Whether the bytes start the way a file of that type does (".jpg", not ".jpeg").</summary>
    public static bool LooksLike(string extension, ReadOnlySpan<byte> bytes) => extension switch
    {
        ".png" => bytes.StartsWith((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        ".jpg" => bytes.StartsWith((byte[])[0xFF, 0xD8, 0xFF]),
        ".webp" => bytes.Length > 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
        ".ico" => bytes.StartsWith((byte[])[0x00, 0x00, 0x01, 0x00]),
        ".svg" => LooksLikeSvg(bytes),
        _ => false
    };

    /// <summary>XML text with an svg root near the start. Served with a sandboxing CSP, so scripts in it never run.</summary>
    private static bool LooksLikeSvg(ReadOnlySpan<byte> bytes)
    {
        var head = Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, 1024)]).TrimStart((char)0xFEFF).TrimStart();
        return head.StartsWith('<') && head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    [GeneratedRegex("""^[A-Za-z0-9 ,'"\-]+$""")]
    private static partial Regex FontList();

    /// <summary>api/branding/assets/{platform|tenant-id}/{kind}-{hash}.{ext}: what this module hands out for uploads.</summary>
    [GeneratedRegex(@"^api/branding/assets/(platform|tenant-[a-z0-9][a-z0-9_-]{0,99})/(logo|logo-dark|favicon)-[0-9a-f]{16}\.(png|jpg|webp|svg|ico)$")]
    public static partial Regex AssetPath();
}

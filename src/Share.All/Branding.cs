namespace soft.square.net.Share.All;

/// <summary>The colour modes a layout offers (light, dark, or whatever the device prefers).</summary>
public static class ThemeModes
{
    public const string Light = "light";
    public const string Dark = "dark";

    /// <summary>Follows the device (prefers-color-scheme).</summary>
    public const string System = "system";

    public static readonly IReadOnlyList<string> All = [Light, Dark, System];
}

/// <summary>
/// A tenant's brand: what layouts show instead of a fixed name and logo, and the theme tokens the tenant starts from.
/// The branding core module serves it per tenant (anonymously: the sign-in page is branded too). Null fields fall back
/// to the layout's and MudTail's defaults.
/// </summary>
/// <param name="Name">Shown in the top bar, the title and the footer.</param>
/// <param name="ShortName">For narrow places (the collapsed menu, a phone's top bar). Null: <see cref="Name"/>.</param>
/// <param name="LogoUrl">The logo on a light background; <see cref="LogoDarkUrl"/> on a dark one (null: the same).</param>
/// <param name="Primary">"#rrggbb": MudTail's --mud-palette-primary.</param>
/// <param name="Secondary">"#rrggbb": MudTail's --mud-palette-secondary.</param>
/// <param name="Radius">Corner radius in px.</param>
/// <param name="Font">A CSS font-family list for the brand's text.</param>
/// <param name="DefaultMode">One of <see cref="ThemeModes"/>: what a user who chose nothing gets.</param>
/// <param name="AllowedModes">The modes users may pick (empty: all). A tenant may, for example, allow only light.</param>
/// <param name="AllowUserColors">Whether users may change the colours in the theme drawer (mode and density always remain theirs).</param>
public sealed record BrandInfo(
    string Name,
    string? ShortName = null,
    string? LogoUrl = null,
    string? LogoDarkUrl = null,
    string? FaviconUrl = null,
    string? Primary = null,
    string? Secondary = null,
    int? Radius = null,
    string? Font = null,
    string DefaultMode = ThemeModes.System,
    IReadOnlyList<string>? AllowedModes = null,
    bool AllowUserColors = true)
{
    /// <summary>The platform's own brand, used until (or without) the branding core module.</summary>
    public static BrandInfo Default { get; } = new("Modular Blazor");

    public string DisplayShortName => string.IsNullOrWhiteSpace(ShortName) ? Name : ShortName;

    public bool Allows(string mode) =>
        AllowedModes is not { Count: > 0 } || AllowedModes.Contains(mode, StringComparer.OrdinalIgnoreCase);
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Contracts;
using soft.square.net.WebApi.Contracts.Security;

namespace soft.square.net.WebApi.Modules.Core.Branding;

/// <summary>
/// Core module "branding" (ships with every WebApi release): what the client's layouts show instead of a fixed name and
/// logo, and the theme tokens a tenant starts from.
/// <list type="bullet">
/// <item>Two layers, like the menus: the platform's (every tenant; <c>platform.modules.manage</c>) and each tenant's on
/// top of it (<see cref="BrandingPermissions.Manage"/> in that tenant). A field the tenant leaves empty comes from the
/// platform's layer, then from the built-in brand.</item>
/// <item><c>GET /api/branding</c> answers anyone (the sign-in page is branded too) with the brand of the request's
/// tenant; the client caches it and applies it before the first render.</item>
/// <item>Logos and the favicon are small image files kept by this module (<c>/api/branding/assets/...</c>, served
/// with a long cache: their names carry their hash).</item>
/// </list>
/// </summary>
public sealed class BrandingModule : IRuntimeModule
{
    public ModuleManifest Manifest { get; } = new("branding", "Branding", "1.0.0", "branding")
    {
        Description = "Core module: the brand the client shows (name, logos, favicon, colours, font, colour modes), per platform and per tenant.",
        Permissions = [new PermissionDefinition(BrandingPermissions.Manage, "Change the tenant's brand: name, logos, colours, font and colour modes.")],
        DefaultSettings = new Dictionary<string, string?>
        {
            ["Branding:Path"] = "App_Data/branding"
        }
    };

    public void ConfigureServices(IServiceCollection services, IConfiguration settings)
    {
        services.Configure<BrandingOptions>(settings.GetSection("Branding"));
        services.AddSingleton<BrandingStore>();
    }
}

public static class BrandingPermissions
{
    /// <summary>Change the tenant's brand. Platform admins change any tenant's, and the platform's, with platform.modules.manage.</summary>
    public const string Manage = "branding.manage";
}

public sealed class BrandingOptions
{
    /// <summary>The folder of the layers and image files, relative to the content root. Shared by every replica.</summary>
    public string Path { get; set; } = "App_Data/branding";
}

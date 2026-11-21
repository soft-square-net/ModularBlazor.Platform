using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Contracts;
using soft.square.net.WebApi.Contracts.Security;

namespace soft.square.net.WebApi.Modules.Core.Menus;

/// <summary>
/// Core module "menus" (ships with every WebApi release): what administrators change in the Blazor client's menus.
/// <list type="bullet">
/// <item>The modules bring their own menu entries (MenuGroup / MenuLink, in the client-modules manifest). This module
/// never reads them: it keeps changes by entry id (hide, rename, move to another menu or group, reorder) and the links
/// and groups administrators add (pages of the app or external addresses), and the client applies them.</item>
/// <item>Two layers: the platform's (every tenant; <c>platform.modules.manage</c>) and each tenant's on top of it
/// (<see cref="MenusPermissions.Manage"/> in that tenant, or a platform admin sending the tenant).</item>
/// <item><c>GET /api/menus</c> answers anyone with the combined changes for their tenant.</item>
/// </list>
/// </summary>
public sealed class MenusModule : IRuntimeModule
{
    public ModuleManifest Manifest { get; } = new("menus", "Menus", "1.0.0", "menus")
    {
        Description = "Core module: administrators' changes to the client's menus (hide, rename, move, reorder; added and external links), per platform and per tenant.",
        Permissions = [new PermissionDefinition(MenusPermissions.Manage, "Change the menus of the tenant: hide, rename and move entries, add links.")],
        DefaultSettings = new Dictionary<string, string?>
        {
            ["Menus:Path"] = "App_Data/menus"
        }
    };

    public void ConfigureServices(IServiceCollection services, IConfiguration settings)
    {
        services.Configure<MenusOptions>(settings.GetSection("Menus"));
        services.AddSingleton<MenuStore>();
    }
}

public static class MenusPermissions
{
    /// <summary>Change the tenant's menus. Platform admins change any tenant's, and the platform's, with platform.modules.manage.</summary>
    public const string Manage = "menus.manage";
}

public sealed class MenusOptions
{
    /// <summary>The folder of the layers, relative to the content root. Shared by every replica.</summary>
    public string Path { get; set; } = "App_Data/menus";
}

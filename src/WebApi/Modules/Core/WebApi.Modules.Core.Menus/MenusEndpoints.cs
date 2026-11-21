using System.Security.Claims;
using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;

namespace soft.square.net.WebApi.Modules.Core.Menus;

/// <summary>
/// /api/menus: the combined changes for the caller (anonymous or signed in), and the admin API for one layer: the
/// tenant's when the request has a tenant, the platform's when it has none. Endpoint names are the OpenAPI operation
/// ids: "Menus_Save" becomes MenusClient.SaveAsync in WebApi.Modules.Core.Menus.Client.
/// </summary>
public sealed class MenusEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var root = app.MapGroup("")
            .TenantOptional()
            .AddEndpointFilter(async (context, next) =>
            {
                try { return await next(context); }
                catch (MenuException e) { return Results.Problem(e.Message, statusCode: e.StatusCode); }
            });

        root.MapGet("", async (HttpContext http, MenuStore store, ITenantContext tenant, IAccessScopes access, CancellationToken ct) =>
            {
                var signedIn = http.User.Identity?.IsAuthenticated == true;
                var menus = MenuRules.Combine(store.Read(MenuStore.PlatformScope), tenant.TenantId is { } id ? store.Read(id) : null,
                    signedIn, signedIn && await CanManageAsync(access, tenant, ct));
                var etag = $"\"{menus.ETag}\"";
                http.Response.Headers.ETag = etag;
                http.Response.Headers.CacheControl = "private, no-cache";
                http.Response.Headers.Vary = "Authorization, X-Tenant";
                var known = http.Request.Headers.IfNoneMatch.SelectMany(v => (v ?? "").Split(',')).Select(t => t.Trim());
                return known.Any(t => t == etag || t == "W/" + etag)
                    ? Results.StatusCode(StatusCodes.Status304NotModified)
                    : Results.Ok(menus);
            })
            .AllowAnonymous()
            .Produces<EffectiveMenus>()
            .Produces(StatusCodes.Status304NotModified)
            .WithName("Menus_Get")
            .WithSummary("The administrators' changes to the menus for the caller's tenant (the platform's, then the tenant's) and " +
                         "the links they added that the caller may see. Sends an ETag; If-None-Match answers 304 when nothing changed.");

        var admin = root.MapGroup("/admin").RequirePermission(MenusPermissions.Manage, PlatformPermissions.ModulesManage);

        admin.MapGet("", async (MenuStore store, ITenantContext tenant, IAccessScopes access, CancellationToken ct) =>
            {
                var scope = await ScopeAsync(access, tenant, ct);
                return new MenuAdminView(scope, store.Read(scope), scope == MenuStore.PlatformScope ? null : store.Read(MenuStore.PlatformScope));
            })
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("Menus_GetLayer")
            .WithSummary("The layer this request changes, with what it is applied on: the tenant's (send the tenant) on top of " +
                         "the platform's, or the platform's (no tenant; platform.modules.manage).");

        admin.MapPut("", async (SaveMenuCustomization save, HttpContext http, MenuStore store, ITenantContext tenant, IAccessScopes access, CancellationToken ct) =>
            {
                var scope = await ScopeAsync(access, tenant, ct);
                var saved = await store.SaveAsync(scope, save, UserName(http.User), ct);
                return new MenuAdminView(scope, saved, scope == MenuStore.PlatformScope ? null : store.Read(MenuStore.PlatformScope));
            })
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("Menus_Save")
            .WithSummary("Replaces the layer (send the version it was read at). Ids of added items are local to the layer: the " +
                         "menu knows them as @platform:{id} or @tenant:{id}.");
    }

    /// <summary>The tenant's layer when the request has one; otherwise the platform's, for platform admins only.</summary>
    private static async Task<string> ScopeAsync(IAccessScopes access, ITenantContext tenant, CancellationToken ct)
    {
        if (tenant.TenantId is { } id) return id;
        if (await access.HasPermissionAsync(PlatformPermissions.ModulesManage, ct)) return MenuStore.PlatformScope;
        throw new MenuException("Changing the platform's menus needs platform.modules.manage; send a tenant to change its menus.", 403);
    }

    private static async Task<bool> CanManageAsync(IAccessScopes access, ITenantContext tenant, CancellationToken ct)
    {
        try
        {
            return await access.HasPermissionAsync(PlatformPermissions.ModulesManage, ct)
                   || (tenant.TenantId is not null && await access.HasPermissionAsync(MenusPermissions.Manage, ct));
        }
        catch (InvalidOperationException)
        {
            return false; // the tenant's grants cannot be read (no database yet): no admin page link, the menus still work
        }
    }

    private static string? UserName(ClaimsPrincipal user) =>
        user.FindFirst(PlatformClaims.Email)?.Value ?? user.FindFirst(PlatformClaims.Name)?.Value ?? user.FindFirst(PlatformClaims.Subject)?.Value;
}

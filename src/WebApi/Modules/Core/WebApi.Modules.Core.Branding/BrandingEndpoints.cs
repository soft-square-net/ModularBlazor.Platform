using System.Security.Claims;
using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;

namespace soft.square.net.WebApi.Modules.Core.Branding;

/// <summary>
/// /api/branding: the brand of the request's tenant (anyone), its images (anyone, cached for good), and the admin API
/// for one layer: the tenant's when the request has a tenant, the platform's when it has none. Endpoint names are the
/// OpenAPI operation ids: "Branding_Save" becomes BrandingClient.SaveAsync in WebApi.Modules.Core.Branding.Client.
/// </summary>
public sealed class BrandingEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var root = app.MapGroup("")
            .TenantOptional()
            .AddEndpointFilter(async (context, next) =>
            {
                try { return await next(context); }
                catch (BrandingException e) { return Results.Problem(e.Message, statusCode: e.StatusCode); }
            });

        root.MapGet("", (HttpContext http, BrandingStore store, ITenantContext tenant) =>
            {
                var brand = store.Effective(tenant.TenantId);
                var etag = $"\"{brand.ETag}\"";
                http.Response.Headers.ETag = etag;
                http.Response.Headers.CacheControl = "no-cache";
                http.Response.Headers.Vary = "X-Tenant";
                var known = http.Request.Headers.IfNoneMatch.SelectMany(v => (v ?? "").Split(',')).Select(t => t.Trim());
                return known.Any(t => t == etag || t == "W/" + etag)
                    ? Results.StatusCode(StatusCodes.Status304NotModified)
                    : Results.Ok(brand);
            })
            .AllowAnonymous()
            .Produces<EffectiveBrand>()
            .Produces(StatusCodes.Status304NotModified)
            .WithName("Branding_Get")
            .WithSummary("The brand of the caller's tenant (the platform's layer, then the tenant's, over the built-in brand). " +
                         "Anyone may read it. Sends an ETag; If-None-Match answers 304 when nothing changed.");

        root.MapGet("/assets/{folder}/{name}", (string folder, string name, HttpContext http, BrandingStore store) =>
            {
                if (store.FindAsset(folder, name) is not { } asset) return Results.NotFound();
                // The name carries the file's hash: cache it for good. Never run anything it contains (an svg may hold script).
                http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(asset.Path, asset.ContentType);
            })
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("Branding_GetAsset")
            .WithSummary("A brand image (logo, dark logo, favicon) by the url its upload answered.");

        var admin = root.MapGroup("/admin").RequirePermission(BrandingPermissions.Manage, PlatformPermissions.ModulesManage);

        admin.MapGet("", async (BrandingStore store, ITenantContext tenant, IAccessScopes access, CancellationToken ct) =>
                View(store, await ScopeAsync(access, tenant, ct)))
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("Branding_GetLayer")
            .WithSummary("The layer this request changes, with what it is applied on: the tenant's (send the tenant) on top of " +
                         "the platform's, or the platform's (no tenant; platform.modules.manage).");

        admin.MapPut("", async (SaveBrandLayer save, HttpContext http, BrandingStore store, ITenantContext tenant, IAccessScopes access, CancellationToken ct) =>
            {
                var scope = await ScopeAsync(access, tenant, ct);
                await store.SaveAsync(scope, save, UserName(http.User), ct);
                return View(store, scope);
            })
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("Branding_Save")
            .WithSummary("Replaces the layer (send the version it was read at). Empty fields come from the layer below.");

        admin.MapPost("/assets/{kind}", async (string kind, IFormFile image, BrandingStore store, ITenantContext tenant, IAccessScopes access, CancellationToken ct) =>
            {
                var scope = await ScopeAsync(access, tenant, ct);
                if (image.Length > BrandRules.MaxImageBytes)
                    throw new BrandingException($"The image must be at most {BrandRules.MaxImageBytes / 1024} KB.", 413);
                await using var content = image.OpenReadStream();
                return await store.AddAssetAsync(scope, kind, image.FileName, content, ct);
            })
            .DisableAntiforgery()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .WithName("Branding_UploadAsset")
            .WithSummary("Keeps an image (kind: logo, logo-dark or favicon; .png, .jpg, .webp, .svg or .ico, at most 512 KB) " +
                         "for this layer and answers its url; save the layer with that url to use it.");
    }

    private static BrandAdminView View(BrandingStore store, string scope) =>
        scope == BrandingStore.PlatformScope
            ? new BrandAdminView(scope, store.Read(scope), null, store.Effective(null))
            : new BrandAdminView(scope, store.Read(scope), store.Read(BrandingStore.PlatformScope), store.Effective(scope));

    /// <summary>The tenant's layer when the request has one; otherwise the platform's, for platform admins only.</summary>
    private static async Task<string> ScopeAsync(IAccessScopes access, ITenantContext tenant, CancellationToken ct)
    {
        if (tenant.TenantId is { } id) return id;
        if (await access.HasPermissionAsync(PlatformPermissions.ModulesManage, ct)) return BrandingStore.PlatformScope;
        throw new BrandingException("Changing the platform's brand needs platform.modules.manage; send a tenant to change its brand.", 403);
    }

    private static string? UserName(ClaimsPrincipal user) =>
        user.FindFirst(PlatformClaims.Email)?.Value ?? user.FindFirst(PlatformClaims.Name)?.Value ?? user.FindFirst(PlatformClaims.Subject)?.Value;
}

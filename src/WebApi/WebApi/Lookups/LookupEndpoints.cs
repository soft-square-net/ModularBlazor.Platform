using Carter;
using Microsoft.AspNetCore.Authorization;
using soft.square.net.WebApi.Contracts.Lookups;
using soft.square.net.WebApi.Contracts.Security;

namespace soft.square.net.WebApi.Lookups;

/// <summary>
/// The endpoints of a module's lookups, mapped by the host into the module's own route group, so they live under its
/// prefix (the gateway routes them to the pool that runs the module) and come and go with its routes:
/// <list type="bullet">
/// <item><c>GET /api/{prefix}/lookups/{name}?q=&amp;fields=&amp;parent=&amp;skip=&amp;take=</c>: search, a page of items.</item>
/// <item><c>GET /api/{prefix}/lookups/{name}/keys?keys=3,17</c> and <c>POST .../keys</c> (with filters): resolve keys.</item>
/// <item><c>POST /api/{prefix}/lookups/{name}/query</c>: search with filter chips and an optional total.</item>
/// </list>
/// Each request runs in the module's request scope (its tenant DbContext). The lookup's permission is checked per
/// request, since one endpoint serves every lookup of the module.
/// </summary>
internal static class LookupEndpoints
{
    public static void Map(IEndpointRouteBuilder group, LookupSet set)
    {
        var lookups = group.MapGroup("/lookups").RequireAuthorization().ExcludeFromDescription();

        lookups.MapGet("/{name}", (string name, string? q, string? fields, string? parent, int? skip, int? take, HttpContext http, CancellationToken ct) =>
            RunAsync(http, set, name, d => d.SearchAsync(http.RequestServices, new LookupQuery
            {
                Q = q,
                Fields = Split(fields),
                Parent = string.IsNullOrEmpty(parent) ? null : parent,
                Skip = skip ?? 0,
                Take = take ?? 20
            }, ct)));

        lookups.MapGet("/{name}/keys", (string name, string? keys, HttpContext http, CancellationToken ct) =>
            RunAsync(http, set, name, d => d.ResolveAsync(http.RequestServices, Split(keys) ?? [], null, ct)));

        lookups.MapPost("/{name}/keys", (string name, LookupKeysRequest request, HttpContext http, CancellationToken ct) =>
            RunAsync(http, set, name, d => d.ResolveAsync(http.RequestServices, request.Keys ?? [], request.Filters, ct)));

        lookups.MapPost("/{name}/query", (string name, LookupQuery query, HttpContext http, CancellationToken ct) =>
            RunAsync(http, set, name, d => d.SearchAsync(http.RequestServices, query, ct)));
    }

    private static async Task<IResult> RunAsync<TResult>(HttpContext http, LookupSet set, string name, Func<ILookupDefinition, Task<TResult>> run)
    {
        if (!set.Lookups.TryGetValue(name, out var definition))
            return Results.Problem($"Lookup '{set.RoutePrefix}.{name}' does not exist.", statusCode: StatusCodes.Status404NotFound);
        if (!await LookupAccess.AllowedAsync(http.RequestServices, http.User, definition.RequiredPermission))
            return Results.Problem($"You need the permission '{definition.RequiredPermission}' to use this lookup.", statusCode: StatusCodes.Status403Forbidden);
        try
        {
            return Results.Ok(await run(definition));
        }
        catch (ArgumentException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid lookup request");
        }
    }

    private static string[]? Split(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Keys to resolve, with optional filters they must also pass (an author's preset filters).</summary>
public sealed record LookupKeysRequest(IReadOnlyList<string>? Keys, IReadOnlyList<LookupFilter>? Filters = null);

internal static class LookupAccess
{
    /// <summary>An authenticated user, holding the lookup's permission when it has one.</summary>
    public static async Task<bool> AllowedAsync(IServiceProvider services, System.Security.Claims.ClaimsPrincipal user, string? permission)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        if (permission is null) return true;
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement([permission])).Build();
        return (await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy)).Succeeded;
    }
}

/// <summary><c>GET /api/lookups</c>: every lookup of every active module that the caller may use (metadata only).</summary>
public sealed class LookupCatalogEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lookups", (ILookupClient lookups, CancellationToken ct) => lookups.CatalogAsync(ct))
            .RequireAuthorization()
            .WithTags("Lookups")
            .WithName("Lookups_Catalog");
    }
}

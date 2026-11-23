using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using soft.square.net.WebApi.Identity.Server.Auth;
using soft.square.net.WebApi.Identity.Server.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace soft.square.net.WebApi.Identity.Server.Admin;

/// <summary>
/// The service admin API (<c>/api/service/...</c>): accounts, memberships and the OpenIddict data, for one trusted
/// caller, the WebApi's identity-admin module. It is called with a client-credentials token of the
/// <c>Identity:AdminClient</c> client (scope <c>identity.admin</c>, audience <c>modular-identity</c>), so it can not
/// be reached with a user's token, nor used against the WebApi. Who may do what is decided by the WebApi module
/// (grants, the request's tenant) before it calls; this API trusts it for that and keeps only the data's rules. The
/// module names the acting user in <c>X-Acting-User</c> (recorded as the inviter).
/// </summary>
public static class ServiceAdminEndpoints
{
    public const string Policy = "identity-service";
    public const string ActingUserHeader = "X-Acting-User";

    public static void AddServiceAdmin(this IServiceCollection services)
    {
        services.AddScoped<Sessions>();
        services.AddAuthorizationBuilder().AddPolicy(Policy, p => p
            .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
            {
                var client = context.Resource is HttpContext http
                    ? http.RequestServices.GetRequiredService<IOptions<IdentityServerOptions>>().Value.AdminClient.ClientId
                    : null;
                var caller = context.User;
                return client is not null
                    && caller.HasScope(IdentityClaims.AdminScope)
                    && caller.HasAudience(IdentityClaims.IdentityAudience)
                    && string.Equals(caller.GetClaim(Claims.Subject), client, StringComparison.Ordinal);
            }));
    }

    public static void MapServiceAdmin(this IEndpointRouteBuilder app)
    {
        var service = app.MapGroup("/api/service")
            .RequireAuthorization(Policy)
            .WithTags("Service admin")
            .AddEndpointFilter(async (context, next) =>
            {
                try { return await next(context); }
                catch (MembershipException e) { return e.ToResult(); }
            });

        // ------------------------------------------------------------ members of one tenant
        var members = service.MapGroup("/tenants/{tenant}/members");

        members.MapGet("/", (string tenant, string? search, Memberships m, CancellationToken ct) => m.ListAsync(tenant, search, ct));

        members.MapGet("/{userId}", async (string tenant, string userId, Memberships m, CancellationToken ct) =>
            await m.GetAsync(tenant, userId, ct) is { } member ? Results.Ok(member) : Results.NotFound());

        members.MapPost("/", async (string tenant, InviteMember input, HttpContext http, Memberships m, CancellationToken ct) =>
        {
            var invited = await m.InviteAsync(tenant, input, ActingUser(http), ensureTenant: true, ct);
            return Results.Created($"/api/service/tenants/{tenant}/members/{invited.UserId}", invited);
        });

        members.MapPut("/{userId}", (string tenant, string userId, UpdateMember input, Memberships m, CancellationToken ct) =>
            m.UpdateAsync(tenant, userId, input, ct));

        members.MapPost("/{userId}/suspend", (string tenant, string userId, Memberships m, CancellationToken ct) =>
            m.SetStatusAsync(tenant, userId, MembershipStatus.Suspended, ct));

        members.MapPost("/{userId}/activate", (string tenant, string userId, Memberships m, CancellationToken ct) =>
            m.SetStatusAsync(tenant, userId, MembershipStatus.Active, ct));

        members.MapDelete("/{userId}", (string tenant, string userId, Memberships m, CancellationToken ct) =>
            m.RemoveAsync(tenant, userId, ct));

        // ------------------------------------------------------------ sessions (tenant-bound sign-ins)
        service.MapGet("/tenants/{tenant}/sessions", (string tenant, string? userId, bool? includeEnded, Sessions s, CancellationToken ct) =>
            s.ListAsync(tenant, userId, includeEnded ?? false, ct));

        service.MapPost("/tenants/{tenant}/sessions/{sessionId}/revoke", (string tenant, string sessionId, Sessions s, CancellationToken ct) =>
            s.RevokeAsync(tenant, sessionId, ct));

        members.MapPost("/{userId}/sessions/revoke", async (string tenant, string userId, Memberships m, Sessions s, CancellationToken ct) =>
            await m.GetAsync(tenant, userId, ct) is null ? Results.NotFound() : Results.Ok(await s.RevokeAllAsync(tenant, userId, ct)));

        MapPlatform(service.MapGroup("/platform"));
    }

    /// <summary>What only platform admins manage (the module checks the caller): tenants' policy, accounts, clients, scopes.</summary>
    private static void MapPlatform(RouteGroupBuilder platform)
    {
        platform.MapGet("/tenants", (PlatformAdmin p, CancellationToken ct) => p.ListTenantsAsync(ct));
        platform.MapPut("/tenants/{tenant}", (string tenant, TenantPolicyInput input, PlatformAdmin p, CancellationToken ct) => p.SetTenantAsync(tenant, input, ct));

        platform.MapGet("/accounts", (string? search, int? take, PlatformAdmin p, CancellationToken ct) => p.ListAccountsAsync(search, take ?? 100, ct));
        platform.MapGet("/accounts/{userId}", (string userId, PlatformAdmin p, CancellationToken ct) => p.GetAccountAsync(userId, ct));
        platform.MapPost("/accounts/{userId}/lock", (string userId, PlatformAdmin p, CancellationToken ct) => p.LockAsync(userId, ct));
        platform.MapPost("/accounts/{userId}/unlock", (string userId, PlatformAdmin p, CancellationToken ct) => p.UnlockAsync(userId, ct));
        platform.MapPost("/accounts/{userId}/sign-out", (string userId, PlatformAdmin p, CancellationToken ct) => p.SignOutEverywhereAsync(userId, ct));
        platform.MapPut("/accounts/{userId}/roles", (string userId, AccountRolesInput input, PlatformAdmin p, CancellationToken ct) => p.SetRolesAsync(userId, input, ct));

        platform.MapGet("/applications", (PlatformAdmin p, CancellationToken ct) => p.ListApplicationsAsync(ct));
        platform.MapGet("/applications/{clientId}", (string clientId, PlatformAdmin p, CancellationToken ct) => p.GetApplicationAsync(clientId, ct));
        platform.MapPost("/applications", async (ApplicationInput input, PlatformAdmin p, CancellationToken ct) =>
        {
            var (application, secret) = await p.CreateApplicationAsync(input, ct);
            return Results.Created($"/api/service/platform/applications/{application.ClientId}", new { application, secret });
        });
        platform.MapPut("/applications/{clientId}", (string clientId, ApplicationInput input, PlatformAdmin p, CancellationToken ct) => p.UpdateApplicationAsync(clientId, input, ct));
        platform.MapDelete("/applications/{clientId}", (string clientId, PlatformAdmin p, CancellationToken ct) => p.DeleteApplicationAsync(clientId, ct));
        platform.MapPost("/applications/{clientId}/secret", (string clientId, PlatformAdmin p, CancellationToken ct) => p.NewSecretAsync(clientId, ct));
        platform.MapPost("/applications/{clientId}/rotation/complete", (string clientId, PlatformAdmin p, CancellationToken ct) => p.CompleteRotationAsync(clientId, ct));
        platform.MapPost("/applications/{clientId}/rotation/cancel", (string clientId, PlatformAdmin p, CancellationToken ct) => p.CancelRotationAsync(clientId, ct));
        platform.MapPost("/applications/{clientId}/tokens/revoke", (string clientId, PlatformAdmin p, CancellationToken ct) => p.RevokeApplicationTokensAsync(clientId, ct));

        platform.MapGet("/scopes", (PlatformAdmin p, CancellationToken ct) => p.ListScopesAsync(ct));
    }

    private static string? ActingUser(HttpContext http) =>
        http.Request.Headers[ActingUserHeader].ToString() is { Length: > 0 and <= 200 } user ? user : null;
}

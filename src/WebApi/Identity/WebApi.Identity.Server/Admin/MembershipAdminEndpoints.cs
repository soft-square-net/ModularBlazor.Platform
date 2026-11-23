using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using soft.square.net.WebApi.Identity.Server.Auth;
using soft.square.net.WebApi.Identity.Server.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace soft.square.net.WebApi.Identity.Server.Admin;

public sealed record MemberView(string UserId, string? Email, string? DisplayName, string Status, string Role,
    string? InvitedBy, DateTimeOffset InvitedAt, DateTimeOffset? JoinedAt);
public sealed record TenantSettingsInput(string? DisplayName, bool RequireMfa);

/// <summary>
/// Minimal membership admin API, called with a bearer token from this service (audience modular-webapi).
/// Allowed for the platform role, or for an active tenant-admin member whose token is for the same tenant.
/// A tenant admin sees only that tenant's memberships, never the user's other tenants. The rules of the data are in
/// <see cref="Memberships"/>, shared with the service API.
/// </summary>
public static class MembershipAdminEndpoints
{
    public const string Policy = "identity-admin";

    public static void MapMembershipAdmin(this IEndpointRouteBuilder app)
    {
        var tenants = app.MapGroup("/api/admin/tenants/{tenant}")
            .RequireAuthorization(Policy)
            .WithTags("Membership admin");

        tenants.MapGet("/members", ListAsync);
        tenants.MapPost("/members", InviteAsync);
        tenants.MapPost("/members/{userId}/suspend", (string tenant, string userId, ClaimsPrincipal caller, IdentityDb db, Memberships members,
            CancellationToken ct) => SetStatusAsync(tenant, userId, MembershipStatus.Suspended, caller, db, members, ct));
        tenants.MapPost("/members/{userId}/activate", (string tenant, string userId, ClaimsPrincipal caller, IdentityDb db, Memberships members,
            CancellationToken ct) => SetStatusAsync(tenant, userId, MembershipStatus.Active, caller, db, members, ct));
        tenants.MapDelete("/members/{userId}", RemoveAsync);
        // Tenant security policy: platform role only.
        tenants.MapPut("/", PutTenantAsync);
    }

    public static void AddMembershipAdmin(this IServiceCollection services)
    {
        services.AddScoped<Memberships>();
        services.AddAuthorizationBuilder().AddPolicy(Policy, p => p
            .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser());
    }

    private static IResult Denied => Results.Problem("Requires the platform-admin role or an active tenant-admin membership in this tenant (token for the same tenant).", statusCode: StatusCodes.Status403Forbidden);

    private static bool IsPlatformAdmin(ClaimsPrincipal caller) => caller.HasClaim(Claims.Role, IdentityClaims.PlatformAdmin);

    private static async Task<bool> CanAdministerAsync(ClaimsPrincipal caller, string tenant, IdentityDb db, CancellationToken ct)
    {
        if (IsPlatformAdmin(caller)) return true;
        var sub = caller.GetClaim(Claims.Subject);
        if (sub is null || !string.Equals(caller.GetClaim(IdentityClaims.Tenant), tenant, StringComparison.Ordinal)) return false;
        return await db.TenantMemberships.AnyAsync(m => m.UserId == sub && m.TenantId == tenant &&
            m.Status == MembershipStatus.Active && m.Role == MembershipRoles.Admin, ct);
    }

    private static async Task<IResult> ListAsync(string tenant, ClaimsPrincipal caller, IdentityDb db, Memberships members, CancellationToken ct)
    {
        if (!await CanAdministerAsync(caller, tenant, db, ct)) return Denied;
        return Results.Ok((await members.ListAsync(tenant, null, ct)).Select(m => new MemberView(m.UserId, m.Email, m.DisplayName,
            m.Status, m.Role, m.InvitedBy, m.InvitedAt, m.JoinedAt)));
    }

    /// <summary>
    /// Invites an account into the tenant (status Invited until the user accepts on /Account/Invitations). An unknown
    /// email gets a new account without a password. TODO: send the invitation email and a set-password link.
    /// </summary>
    private static async Task<IResult> InviteAsync(string tenant, InviteMember input, ClaimsPrincipal caller, IdentityDb db,
        Memberships members, CancellationToken ct)
    {
        if (!await CanAdministerAsync(caller, tenant, db, ct)) return Denied;
        try
        {
            var invited = await members.InviteAsync(tenant, input, caller.GetClaim(Claims.Subject), ensureTenant: false, ct);
            return Results.Created($"/api/admin/tenants/{tenant}/members/{invited.UserId}", invited);
        }
        catch (MembershipException e) { return e.ToResult(); }
    }

    private static async Task<IResult> SetStatusAsync(string tenant, string userId, MembershipStatus status, ClaimsPrincipal caller,
        IdentityDb db, Memberships members, CancellationToken ct)
    {
        if (!await CanAdministerAsync(caller, tenant, db, ct)) return Denied;
        try { return Results.Ok(await members.SetStatusAsync(tenant, userId, status, ct)); }
        catch (MembershipException e) { return e.ToResult(); }
    }

    private static async Task<IResult> RemoveAsync(string tenant, string userId, ClaimsPrincipal caller, IdentityDb db,
        Memberships members, CancellationToken ct)
    {
        if (!await CanAdministerAsync(caller, tenant, db, ct)) return Denied;
        try { return Results.Ok(await members.RemoveAsync(tenant, userId, ct)); }
        catch (MembershipException e) { return e.ToResult(); }
    }

    private static async Task<IResult> PutTenantAsync(string tenant, TenantSettingsInput input, ClaimsPrincipal caller, IdentityDb db, CancellationToken ct)
    {
        if (!IsPlatformAdmin(caller)) return Denied;
        var settings = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenant, ct);
        if (settings is null) db.Tenants.Add(settings = new TenantSettings { TenantId = tenant });
        settings.DisplayName = input.DisplayName ?? (settings.DisplayName.Length > 0 ? settings.DisplayName : tenant);
        settings.RequireMfa = input.RequireMfa;
        await db.SaveChangesAsync(ct);
        return Results.Ok(settings);
    }
}

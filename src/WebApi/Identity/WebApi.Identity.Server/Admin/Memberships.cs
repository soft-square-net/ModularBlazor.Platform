using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using soft.square.net.WebApi.Identity.Server.Auth;
using soft.square.net.WebApi.Identity.Server.Data;

namespace soft.square.net.WebApi.Identity.Server.Admin;

public sealed record InviteMember(string Email, string? Role, string? DisplayName, string? JobTitle = null);

/// <summary>Changes to a membership; null leaves a value as it is, "" clears a profile field.</summary>
public sealed record UpdateMember(string? Role, string? DisplayName, string? JobTitle);

/// <summary>A member of one tenant, with what an admin needs to know about the account behind it.</summary>
/// <param name="AccountName">The global account's display name (every tenant sees the same).</param>
/// <param name="DisplayName">The tenant profile's display name (this tenant only).</param>
/// <param name="HasPassword">False for an invited account that has never set one.</param>
public sealed record MemberDetails(
    string UserId,
    string? Email,
    bool EmailConfirmed,
    string? AccountName,
    string? DisplayName,
    string? JobTitle,
    string Status,
    string Role,
    string? InvitedBy,
    DateTimeOffset InvitedAt,
    DateTimeOffset? JoinedAt,
    bool LockedOut,
    bool TwoFactorEnabled,
    bool HasPassword);

public sealed record MemberInvited(string UserId, string Status, bool NewAccount);
public sealed record MemberStatusChanged(string UserId, string Tenant, string Status, int RevokedAuthorizations);
public sealed record MemberRemoved(string UserId, string Tenant, bool Removed, int RevokedAuthorizations);

/// <summary>A membership rule refused the change; <see cref="ToResult"/> is the HTTP answer.</summary>
public sealed class MembershipException(int statusCode, string error, string message, string? userId = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Error { get; } = error;
    public string? UserId { get; } = userId;

    public IResult ToResult() => StatusCode switch
    {
        StatusCodes.Status404NotFound => Results.Problem(Message, statusCode: StatusCode),
        StatusCodes.Status400BadRequest => Results.ValidationProblem(new Dictionary<string, string[]> { [Error] = [Message] }),
        _ => Results.Json(new { error = Error, message = Message, userId = UserId }, statusCode: StatusCode),
    };

    public static MembershipException NotFound(string message) => new(StatusCodes.Status404NotFound, "not_found", message);
}

/// <summary>
/// Tenant memberships: listing, inviting, editing, suspending, re-activating and removing members. Shared by the
/// membership admin API (tenant admins with their own token) and the service API (the WebApi's identity-admin module),
/// which check who may call; this class only keeps the rules of the data. Suspending or removing a member revokes that
/// tenant's authorizations of the user, so their refresh tokens stop working at once.
/// </summary>
public sealed class Memberships(IdentityDb db, UserManager<ApplicationUser> users, TenantAuthorizations authorizations)
{
    public static bool IsRole(string role) => role is MembershipRoles.Member or MembershipRoles.Admin;

    public async Task<IReadOnlyList<MemberDetails>> ListAsync(string tenant, string? search, CancellationToken ct)
    {
        var query = Query(tenant);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.User.Email ?? "", text)
                || EF.Functions.ILike(x.User.DisplayName ?? "", text)
                || EF.Functions.ILike(x.Profile!.DisplayName ?? "", text));
        }
        var rows = await query.OrderBy(x => x.User.Email).ToListAsync(ct);
        return rows.Select(ToDetails).ToList();
    }

    public async Task<MemberDetails?> GetAsync(string tenant, string userId, CancellationToken ct) =>
        await Query(tenant).Where(x => x.Membership.UserId == userId).FirstOrDefaultAsync(ct) is { } row ? ToDetails(row) : null;

    /// <summary>
    /// Invites an account into the tenant (status Invited until the user accepts on /Account/Invitations). An unknown
    /// email gets a new account without a password. <paramref name="ensureTenant"/>: a tenant the identity service has
    /// not heard of yet gets its row (the caller vouches for it: the WebApi resolved it); otherwise it is a 404.
    /// </summary>
    public async Task<MemberInvited> InviteAsync(string tenant, InviteMember input, string? invitedBy, bool ensureTenant, CancellationToken ct)
    {
        var role = string.IsNullOrWhiteSpace(input.Role) ? MembershipRoles.Member : input.Role.Trim();
        if (!IsRole(role))
            throw new MembershipException(StatusCodes.Status400BadRequest, "role", $"Use '{MembershipRoles.Member}' or '{MembershipRoles.Admin}'.");
        if (string.IsNullOrWhiteSpace(input.Email) || !input.Email.Contains('@'))
            throw new MembershipException(StatusCodes.Status400BadRequest, "email", "An email address is required.");

        if (!await db.Tenants.AnyAsync(t => t.TenantId == tenant, ct))
        {
            if (!ensureTenant) throw MembershipException.NotFound($"Tenant '{tenant}' is not known to the identity service.");
            db.Tenants.Add(new TenantSettings { TenantId = tenant, DisplayName = tenant });
        }

        var email = input.Email.Trim();
        var user = await users.FindByEmailAsync(email);
        var newAccount = user is null;
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, DisplayName = Clean(input.DisplayName) };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
                throw new MembershipException(StatusCodes.Status400BadRequest, created.Errors.First().Code, string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        if (await db.TenantMemberships.AnyAsync(m => m.UserId == user.Id && m.TenantId == tenant, ct))
            throw new MembershipException(StatusCodes.Status409Conflict, "already_member", $"{email} is a member of '{tenant}' already.", user.Id);

        db.TenantMemberships.Add(new TenantMembership
        {
            UserId = user.Id, TenantId = tenant, Status = MembershipStatus.Invited, Role = role, InvitedBy = invitedBy,
        });
        if (Clean(input.DisplayName) is not null || Clean(input.JobTitle) is not null)
            db.TenantProfiles.Add(new TenantProfile { UserId = user.Id, TenantId = tenant, DisplayName = Clean(input.DisplayName), JobTitle = Clean(input.JobTitle) });
        await db.SaveChangesAsync(ct);
        return new MemberInvited(user.Id, nameof(MembershipStatus.Invited), newAccount);
    }

    /// <summary>Changes the member's role in the tenant and their tenant profile.</summary>
    public async Task<MemberDetails> UpdateAsync(string tenant, string userId, UpdateMember input, CancellationToken ct)
    {
        var membership = await FindAsync(tenant, userId, ct);
        if (input.Role is { } role)
        {
            if (!IsRole(role)) throw new MembershipException(StatusCodes.Status400BadRequest, "role", $"Use '{MembershipRoles.Member}' or '{MembershipRoles.Admin}'.");
            membership.Role = role;
        }
        if (input.DisplayName is not null || input.JobTitle is not null)
        {
            var profile = await db.TenantProfiles.FirstOrDefaultAsync(p => p.UserId == userId && p.TenantId == tenant, ct);
            if (profile is null) db.TenantProfiles.Add(profile = new TenantProfile { UserId = userId, TenantId = tenant });
            if (input.DisplayName is not null) profile.DisplayName = Clean(input.DisplayName);
            if (input.JobTitle is not null) profile.JobTitle = Clean(input.JobTitle);
        }
        await db.SaveChangesAsync(ct);
        return (await GetAsync(tenant, userId, ct))!;
    }

    /// <summary>Suspends (and revokes the tenant's authorizations) or re-activates a member. An invitation cannot be activated.</summary>
    public async Task<MemberStatusChanged> SetStatusAsync(string tenant, string userId, MembershipStatus status, CancellationToken ct)
    {
        var membership = await FindAsync(tenant, userId, ct);
        if (status == MembershipStatus.Active && membership.Status == MembershipStatus.Invited)
            throw new MembershipException(StatusCodes.Status409Conflict, "invitation_pending", "The user has to accept the invitation.", userId);

        membership.Status = status;
        await db.SaveChangesAsync(ct);
        // A suspended member's refresh tokens die now; a refresh would be refused anyway, this also ends sessions.
        var revoked = status == MembershipStatus.Suspended ? await authorizations.RevokeAsync(userId, tenant, ct) : 0;
        return new MemberStatusChanged(userId, tenant, status.ToString(), revoked);
    }

    /// <summary>Removes the membership and the tenant profile, and revokes that tenant's authorizations of the user.</summary>
    public async Task<MemberRemoved> RemoveAsync(string tenant, string userId, CancellationToken ct)
    {
        var membership = await FindAsync(tenant, userId, ct);
        db.TenantMemberships.Remove(membership);
        await db.TenantProfiles.Where(p => p.UserId == userId && p.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        var revoked = await authorizations.RevokeAsync(userId, tenant, ct);
        return new MemberRemoved(userId, tenant, true, revoked);
    }

    private async Task<TenantMembership> FindAsync(string tenant, string userId, CancellationToken ct) =>
        await db.TenantMemberships.FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenant, ct)
            ?? throw MembershipException.NotFound($"'{userId}' is not a member of '{tenant}'.");

    // Member init (not a constructor), so EF Core can still filter and sort on it.
    private sealed class Row
    {
        public required TenantMembership Membership { get; init; }
        public required ApplicationUser User { get; init; }
        public TenantProfile? Profile { get; init; }
    }

    private IQueryable<Row> Query(string tenant) =>
        from m in db.TenantMemberships.AsNoTracking()
        where m.TenantId == tenant
        join u in db.Users.AsNoTracking() on m.UserId equals u.Id
        join p in db.TenantProfiles.AsNoTracking().Where(p => p.TenantId == tenant) on m.UserId equals p.UserId into profiles
        from p in profiles.DefaultIfEmpty()
        select new Row { Membership = m, User = u, Profile = p };

    private static MemberDetails ToDetails(Row x) => new(
        x.Membership.UserId, x.User.Email, x.User.EmailConfirmed, x.User.DisplayName, x.Profile?.DisplayName, x.Profile?.JobTitle,
        x.Membership.Status.ToString(), x.Membership.Role, x.Membership.InvitedBy, x.Membership.InvitedAt, x.Membership.JoinedAt,
        x.User.LockoutEnd is { } end && end > DateTimeOffset.UtcNow, x.User.TwoFactorEnabled, x.User.PasswordHash is not null);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

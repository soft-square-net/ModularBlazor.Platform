using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using soft.square.net.WebApi.Identity.Server.Auth;
using soft.square.net.WebApi.Identity.Server.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace soft.square.net.WebApi.Identity.Server.Admin;

// ---------------------------------------------------------------- contracts

public sealed record TenantPolicy(string TenantId, string DisplayName, bool RequireMfa, int Active, int Invited, int Suspended);
public sealed record TenantPolicyInput(string? DisplayName, bool? RequireMfa);

public sealed record AccountMembership(string Tenant, string Status, string Role);
public sealed record AccountDetails(
    string UserId, string? Email, bool EmailConfirmed, string? DisplayName,
    bool LockedOut, DateTimeOffset? LockoutEnd, int AccessFailedCount,
    bool TwoFactorEnabled, bool HasPassword,
    IReadOnlyList<string> PlatformRoles, IReadOnlyList<AccountMembership> Memberships);
public sealed record AccountRolesInput(bool PlatformAdmin);
public sealed record AccountChanged(string UserId, string Action, int RevokedSessions);

/// <summary>A client of this service. Built-in ones come from configuration (<c>config</c>); <c>ui</c> ones are managed here.</summary>
public sealed record ApplicationDetails(
    string Id, string ClientId, string? DisplayName, string ClientType, string ManagedBy,
    IReadOnlyList<string> GrantTypes, IReadOnlyList<string> Scopes,
    IReadOnlyList<string> RedirectUris, IReadOnlyList<string> PostLogoutRedirectUris,
    bool RequirePkce, bool HasSecret, SecretRotation? Rotation, DateTimeOffset? SecretRotatedAt,
    int LiveTokens, DateTimeOffset? LastTokenAt);
/// <summary>A rotation in progress: the next secret is accepted next to the current one.</summary>
public sealed record SecretRotation(DateTimeOffset? StartedAt, DateTimeOffset? FirstUsedAt);
public sealed record ApplicationInput(
    string? ClientId, string? DisplayName, string? ClientType, IReadOnlyList<string>? GrantTypes, IReadOnlyList<string>? Scopes,
    IReadOnlyList<string>? RedirectUris, IReadOnlyList<string>? PostLogoutRedirectUris);
/// <summary>A secret, shown this once: it is only stored hashed.</summary>
public sealed record ApplicationSecret(string ClientId, string Secret, string Mode);
public sealed record ApplicationTokensRevoked(string ClientId, int RevokedAuthorizations, int RevokedTokens);
/// <summary>A scope a client may ask for; <c>BuiltIn</c>: a standard OpenID Connect scope (not one of the identity service's own).</summary>
public sealed record ScopeDetails(string Name, string? DisplayName, string? Description, IReadOnlyList<string> Resources, bool BuiltIn);

/// <summary>
/// What only a platform admin manages in the identity service (through the WebApi's identity-admin module, which
/// checks the caller is one): tenants' sign-in policy, global accounts, the clients and the scopes.
/// </summary>
public sealed partial class PlatformAdmin(
    IdentityDb db, UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles,
    IOpenIddictApplicationManager applicationManager, IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens,
    TenantAuthorizations tenantAuthorizations, SessionEnds sessionEnds)
{
    private RotatingApplicationManager Applications => (RotatingApplicationManager)applicationManager;

    /// <summary>Standard scopes the server knows without a scope entry.</summary>
    private static readonly string[] StandardScopes = [Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess];
    private static readonly string[] UiGrantTypes = [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken, GrantTypes.ClientCredentials];

    // ============================================================ tenants

    public async Task<IReadOnlyList<TenantPolicy>> ListTenantsAsync(CancellationToken ct)
    {
        var counts = await db.TenantMemberships.GroupBy(m => new { m.TenantId, m.Status })
            .Select(g => new { g.Key.TenantId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var settings = await db.Tenants.AsNoTracking().ToListAsync(ct);
        var ids = settings.Select(t => t.TenantId).Union(counts.Select(c => c.TenantId)).Distinct().Order(StringComparer.Ordinal);
        int Count(string tenant, MembershipStatus status) => counts.Where(c => c.TenantId == tenant && c.Status == status).Sum(c => c.Count);
        return ids.Select(id =>
        {
            var s = settings.FirstOrDefault(t => t.TenantId == id);
            return new TenantPolicy(id, s?.DisplayName ?? id, s?.RequireMfa ?? false,
                Count(id, MembershipStatus.Active), Count(id, MembershipStatus.Invited), Count(id, MembershipStatus.Suspended));
        }).ToList();
    }

    public async Task<TenantPolicy> SetTenantAsync(string tenant, TenantPolicyInput input, CancellationToken ct)
    {
        var settings = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenant, ct);
        if (settings is null) db.Tenants.Add(settings = new TenantSettings { TenantId = tenant, DisplayName = tenant });
        if (!string.IsNullOrWhiteSpace(input.DisplayName)) settings.DisplayName = input.DisplayName.Trim();
        if (input.RequireMfa is { } mfa) settings.RequireMfa = mfa;
        await db.SaveChangesAsync(ct);
        return (await ListTenantsAsync(ct)).First(t => t.TenantId == tenant);
    }

    // ============================================================ accounts

    public async Task<IReadOnlyList<AccountDetails>> ListAccountsAsync(string? search, int take, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = $"%{search.Trim()}%";
            query = query.Where(u => EF.Functions.ILike(u.Email ?? "", text) || EF.Functions.ILike(u.DisplayName ?? "", text));
        }
        var people = await query.OrderBy(u => u.Email).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
        return await DescribeAsync(people, ct);
    }

    public async Task<AccountDetails> GetAccountAsync(string userId, CancellationToken ct) =>
        (await DescribeAsync([await FindAsync(userId)], ct))[0];

    /// <summary>Locks the account everywhere: no sign-in, every session ends (all tenants and the platform).</summary>
    public async Task<AccountChanged> LockAsync(string userId, CancellationToken ct)
    {
        var user = await FindAsync(userId);
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        return new AccountChanged(userId, "locked", await EndEverywhereAsync(user, ct));
    }

    public async Task<AccountChanged> UnlockAsync(string userId, CancellationToken ct)
    {
        var user = await FindAsync(userId);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return new AccountChanged(userId, "unlocked", 0);
    }

    /// <summary>Ends every session of the account, in every tenant and on the platform; each browser signs in again.</summary>
    public async Task<AccountChanged> SignOutEverywhereAsync(string userId, CancellationToken ct) =>
        new(userId, "signed-out", await EndEverywhereAsync(await FindAsync(userId), ct));

    /// <summary>Grants or takes the platform-admin role; sessions are renewed so tokens carry the change.</summary>
    public async Task<AccountChanged> SetRolesAsync(string userId, AccountRolesInput input, CancellationToken ct)
    {
        var user = await FindAsync(userId);
        if (!await roles.RoleExistsAsync(IdentityClaims.PlatformAdmin)) await roles.CreateAsync(new IdentityRole(IdentityClaims.PlatformAdmin));
        var has = await users.IsInRoleAsync(user, IdentityClaims.PlatformAdmin);
        if (input.PlatformAdmin == has) return new AccountChanged(userId, "unchanged", 0);
        var result = input.PlatformAdmin
            ? await users.AddToRoleAsync(user, IdentityClaims.PlatformAdmin)
            : await users.RemoveFromRoleAsync(user, IdentityClaims.PlatformAdmin);
        if (!result.Succeeded) throw new MembershipException(StatusCodes.Status400BadRequest, "roles", string.Join(" ", result.Errors.Select(e => e.Description)));
        // Tokens carry the role from when they were issued: revoke so the next ones are issued with the change.
        return new AccountChanged(userId, input.PlatformAdmin ? "platform-admin-granted" : "platform-admin-removed",
            await tenantAuthorizations.RevokeAllAsync(userId, ct));
    }

    private async Task<int> EndEverywhereAsync(ApplicationUser user, CancellationToken ct)
    {
        var revoked = await tenantAuthorizations.RevokeAllAsync(user.Id, ct);
        var tenants = await db.TenantMemberships.Where(m => m.UserId == user.Id).Select(m => m.TenantId).ToListAsync(ct);
        foreach (var tenant in tenants.Append(SessionEnds.Platform)) await sessionEnds.EndAllAsync(user.Id, tenant);
        await users.UpdateSecurityStampAsync(user); // the login cookie stops validating at its next check
        return revoked;
    }

    private async Task<IReadOnlyList<AccountDetails>> DescribeAsync(IReadOnlyList<ApplicationUser> people, CancellationToken ct)
    {
        var ids = people.Select(p => p.Id).ToList();
        var memberships = await db.TenantMemberships.AsNoTracking().Where(m => ids.Contains(m.UserId))
            .OrderBy(m => m.TenantId).ToListAsync(ct);
        var platformRoles = await db.UserRoles.Where(r => ids.Contains(r.UserId))
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name }).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        return people.Select(u => new AccountDetails(
            u.Id, u.Email, u.EmailConfirmed, u.DisplayName,
            u.LockoutEnd is { } end && end > now, u.LockoutEnd, u.AccessFailedCount,
            u.TwoFactorEnabled, u.PasswordHash is not null,
            platformRoles.Where(r => r.UserId == u.Id && r.Name is not null).Select(r => r.Name!).ToList(),
            memberships.Where(m => m.UserId == u.Id).Select(m => new AccountMembership(m.TenantId, m.Status.ToString(), m.Role)).ToList()))
            .ToList();
    }

    private async Task<ApplicationUser> FindAsync(string userId) =>
        await users.FindByIdAsync(userId) ?? throw MembershipException.NotFound($"No account '{userId}'.");

    // ============================================================ applications

    public async Task<IReadOnlyList<ApplicationDetails>> ListApplicationsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var usage = await db.Set<OpenIddictEntityFrameworkCoreToken>()
            .Where(t => t.Application != null)
            .GroupBy(t => t.Application!.Id)
            .Select(g => new
            {
                Id = g.Key,
                Live = g.Count(t => t.Status == Statuses.Valid && (t.ExpirationDate == null || t.ExpirationDate > now)
                    && (t.Type == TokenTypeIdentifiers.AccessToken || t.Type == TokenTypeIdentifiers.RefreshToken)),
                Last = g.Max(t => t.CreationDate),
            })
            .ToListAsync(ct);

        var list = new List<ApplicationDetails>();
        await foreach (var app in Applications.ListAsync(null, null, ct))
        {
            var id = (await Applications.GetIdAsync(app, ct))!;
            var use = usage.FirstOrDefault(u => u.Id == id);
            list.Add(await DescribeAsync(app, use?.Live ?? 0, use?.Last, ct));
        }
        return list.OrderBy(a => a.ManagedBy == RotatingApplicationManager.ManagedByConfig ? 0 : 1).ThenBy(a => a.ClientId, StringComparer.Ordinal).ToList();
    }

    public async Task<ApplicationDetails> GetApplicationAsync(string clientId, CancellationToken ct) =>
        (await ListApplicationsAsync(ct)).FirstOrDefault(a => a.ClientId == clientId) ?? throw MembershipException.NotFound($"No client '{clientId}'.");

    /// <summary>Creates a client managed in the admin UI; a confidential one gets a secret, returned this once.</summary>
    public async Task<(ApplicationDetails Application, ApplicationSecret? Secret)> CreateApplicationAsync(ApplicationInput input, CancellationToken ct)
    {
        var clientId = input.ClientId?.Trim() ?? "";
        if (!ClientIdPattern().IsMatch(clientId))
            throw Invalid("clientId", "The client id is 3 to 64 characters: lower-case letters, digits, '.', '_' and '-'.");
        if (await Applications.FindByClientIdAsync(clientId, ct) is not null)
            throw new MembershipException(StatusCodes.Status409Conflict, "client_exists", $"A client '{clientId}' exists already.");
        var type = input.ClientType is ClientTypes.Public or ClientTypes.Confidential ? input.ClientType
            : throw Invalid("clientType", "The client type is public or confidential.");

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = type,
            ConsentType = ConsentTypes.Implicit,
            Properties = { [RotatingApplicationManager.ManagedBy] = JsonSerializer.SerializeToElement(RotatingApplicationManager.ManagedByUi) },
        };
        await FillAsync(descriptor, input, type, ct);
        string? secret = null;
        if (type == ClientTypes.Confidential) descriptor.ClientSecret = secret = RotatingApplicationManager.NewSecret();
        await Applications.CreateAsync(descriptor, ct);

        var created = await GetApplicationAsync(clientId, ct);
        return (created, secret is null ? null : new ApplicationSecret(clientId, secret, "created"));
    }

    public async Task<ApplicationDetails> UpdateApplicationAsync(string clientId, ApplicationInput input, CancellationToken ct)
    {
        var app = await UiManagedAsync(clientId, ct);
        var descriptor = new OpenIddictApplicationDescriptor();
        await Applications.PopulateAsync(descriptor, app, ct); // keeps the type, the secret hash and the properties
        descriptor.Permissions.Clear();
        descriptor.Requirements.Clear();
        descriptor.RedirectUris.Clear();
        descriptor.PostLogoutRedirectUris.Clear();
        await FillAsync(descriptor, input, descriptor.ClientType!, ct);
        await Applications.UpdateAsync(app, descriptor, ct);
        return await GetApplicationAsync(clientId, ct);
    }

    public async Task<ApplicationTokensRevoked> DeleteApplicationAsync(string clientId, CancellationToken ct)
    {
        var app = await UiManagedAsync(clientId, ct);
        var revoked = await RevokeApplicationTokensAsync(clientId, ct);
        // The manager's entity may come from OpenIddict's cache, loaded before the row's last update, and the delete
        // checks its concurrency stamp: delete the row as it is now. Through the manager, so its cache forgets it.
        var id = app.Id!;
        db.ChangeTracker.Clear();
        var fresh = await db.Set<OpenIddictEntityFrameworkCoreApplication>().SingleAsync(a => a.Id == id, ct);
        await Applications.DeleteAsync(fresh, ct);
        return revoked;
    }

    /// <summary>
    /// A new secret. For a client managed in the UI it replaces the old one now. For a built-in (configuration) client
    /// it starts a rotation: the old and the new secret both work until the rotation is completed.
    /// </summary>
    public async Task<ApplicationSecret> NewSecretAsync(string clientId, CancellationToken ct)
    {
        var app = await FindApplicationAsync(clientId, ct);
        if (await Applications.GetClientTypeAsync(app, ct) != ClientTypes.Confidential)
            throw new MembershipException(StatusCodes.Status409Conflict, "public_client", "A public client has no secret.");
        return await ManagedByAsync(app, ct) == RotatingApplicationManager.ManagedByUi
            ? new ApplicationSecret(clientId, await Applications.ReplaceSecretAsync(app, ct), "replaced")
            : new ApplicationSecret(clientId, await Applications.StartRotationAsync(app, ct), "rotation-started");
    }

    /// <summary>Completes a rotation once the new secret was used: from now on only it works.</summary>
    public async Task<ApplicationDetails> CompleteRotationAsync(string clientId, CancellationToken ct)
    {
        var app = await FindApplicationAsync(clientId, ct);
        var properties = await Applications.GetStoredPropertiesAsync(app, ct);
        if (!properties.ContainsKey(RotatingApplicationManager.NextSecret))
            throw new MembershipException(StatusCodes.Status409Conflict, "no_rotation", "No rotation is in progress.");
        if (!properties.ContainsKey(RotatingApplicationManager.NextSecretUsed))
            throw new MembershipException(StatusCodes.Status409Conflict, "not_used_yet",
                "The new secret has not been used yet: put it in the secret store, restart the services that use it, and try again.");
        await Applications.CompleteRotationAsync(app, ct);
        return await GetApplicationAsync(clientId, ct);
    }

    public async Task<ApplicationDetails> CancelRotationAsync(string clientId, CancellationToken ct)
    {
        await Applications.CancelRotationAsync(await FindApplicationAsync(clientId, ct), ct);
        return await GetApplicationAsync(clientId, ct);
    }

    /// <summary>Revokes every authorization and token of the client (its users sign in again; a service gets a new token).</summary>
    public async Task<ApplicationTokensRevoked> RevokeApplicationTokensAsync(string clientId, CancellationToken ct)
    {
        var app = await FindApplicationAsync(clientId, ct);
        var id = (await Applications.GetIdAsync(app, ct))!;
        int revokedTokens = 0, revokedAuthorizations = 0;
        await foreach (var token in tokens.FindByApplicationIdAsync(id, ct))
            if (await tokens.GetStatusAsync(token, ct) == Statuses.Valid && await tokens.TryRevokeAsync(token, ct)) revokedTokens++;
        await foreach (var authorization in authorizations.FindByApplicationIdAsync(id, ct))
            if (await authorizations.GetStatusAsync(authorization, ct) == Statuses.Valid && await authorizations.TryRevokeAsync(authorization, ct)) revokedAuthorizations++;
        return new ApplicationTokensRevoked(clientId, revokedAuthorizations, revokedTokens);
    }

    private async Task FillAsync(OpenIddictApplicationDescriptor descriptor, ApplicationInput input, string type, CancellationToken ct)
    {
        descriptor.DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? descriptor.ClientId : input.DisplayName.Trim();

        var grants = (input.GrantTypes ?? []).Select(g => g.Trim()).Where(g => g.Length > 0).Distinct().ToList();
        if (grants.Count == 0) throw Invalid("grantTypes", "Pick at least one grant type.");
        if (grants.FirstOrDefault(g => !UiGrantTypes.Contains(g)) is { } unknown) throw Invalid("grantTypes", $"'{unknown}' is not a grant type a client here may use.");
        if (grants.Contains(GrantTypes.ClientCredentials) && type != ClientTypes.Confidential)
            throw Invalid("grantTypes", "Only a confidential client may use client credentials.");
        if (grants.Contains(GrantTypes.RefreshToken) && !grants.Contains(GrantTypes.AuthorizationCode))
            throw Invalid("grantTypes", "Refresh tokens come with the authorization code grant.");

        var known = StandardScopes.Concat(await db.Set<OpenIddictEntityFrameworkCoreScope>().Select(s => s.Name!).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var scopes = (input.Scopes ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
        if (scopes.FirstOrDefault(s => !known.Contains(s)) is { } unknownScope) throw Invalid("scopes", $"There is no scope '{unknownScope}'.");
        if (scopes.Contains(IdentityClaims.AdminScope)) throw Invalid("scopes", "The identity.admin scope belongs to the identity-admin module's client only.");
        if (scopes.Contains(Scopes.OfflineAccess) && !grants.Contains(GrantTypes.RefreshToken))
            throw Invalid("scopes", "offline_access needs the refresh token grant.");

        var redirects = Uris(input.RedirectUris, "redirectUris");
        var logouts = Uris(input.PostLogoutRedirectUris, "postLogoutRedirectUris");
        var code = grants.Contains(GrantTypes.AuthorizationCode);
        if (code && redirects.Count == 0) throw Invalid("redirectUris", "The authorization code grant needs at least one redirect URI.");

        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        if (code)
        {
            descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
            descriptor.Permissions.Add(Permissions.Endpoints.Revocation);
            descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange); // PKCE, always
        }
        foreach (var grant in grants) descriptor.Permissions.Add(Permissions.Prefixes.GrantType + grant);
        foreach (var scope in scopes.Where(s => s != Scopes.OpenId)) descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        foreach (var uri in redirects) descriptor.RedirectUris.Add(uri);
        foreach (var uri in logouts) descriptor.PostLogoutRedirectUris.Add(uri);
    }

    private static List<Uri> Uris(IReadOnlyList<string>? values, string field)
    {
        var list = new List<Uri>();
        foreach (var value in (values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct())
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.Fragment))
                throw Invalid(field, $"'{value}' is not an absolute http(s) URI without a fragment.");
            list.Add(uri);
        }
        return list;
    }

    private async Task<ApplicationDetails> DescribeAsync(OpenIddictEntityFrameworkCoreApplication app, int live, DateTime? last, CancellationToken ct)
    {
        var permissions = await Applications.GetPermissionsAsync(app, ct);
        var properties = await Applications.GetStoredPropertiesAsync(app, ct);
        var requirements = await Applications.GetRequirementsAsync(app, ct);
        var clientType = await Applications.GetClientTypeAsync(app, ct) ?? "";
        var rotation = properties.ContainsKey(RotatingApplicationManager.NextSecret)
            ? new SecretRotation(RotatingApplicationManager.Time(properties, RotatingApplicationManager.NextSecretCreated),
                RotatingApplicationManager.Time(properties, RotatingApplicationManager.NextSecretUsed))
            : null;
        return new ApplicationDetails(
            (await Applications.GetIdAsync(app, ct))!,
            (await Applications.GetClientIdAsync(app, ct))!,
            await Applications.GetDisplayNameAsync(app, ct),
            clientType,
            ManagedBy(properties),
            permissions.Where(p => p.StartsWith(Permissions.Prefixes.GrantType, StringComparison.Ordinal))
                .Select(p => p[Permissions.Prefixes.GrantType.Length..]).Order(StringComparer.Ordinal).ToList(),
            // openid needs no permission (OpenIddict always allows it): a client that signs people in may ask for it.
            permissions.Where(p => p.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal))
                .Select(p => p[Permissions.Prefixes.Scope.Length..])
                .Concat(permissions.Contains(Permissions.GrantTypes.AuthorizationCode) ? [Scopes.OpenId] : [])
                .Distinct().Order(StringComparer.Ordinal).ToList(),
            (await Applications.GetRedirectUrisAsync(app, ct)).Order(StringComparer.Ordinal).ToList(),
            (await Applications.GetPostLogoutRedirectUrisAsync(app, ct)).Order(StringComparer.Ordinal).ToList(),
            requirements.Contains(Requirements.Features.ProofKeyForCodeExchange),
            clientType == ClientTypes.Confidential && await Applications.GetSecretHashAsync(app, ct) is { Length: > 0 },
            rotation,
            RotatingApplicationManager.Time(properties, RotatingApplicationManager.SecretRotated),
            live,
            last is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null);
    }

    private static string ManagedBy(IReadOnlyDictionary<string, JsonElement> properties) =>
        RotatingApplicationManager.Text(properties, RotatingApplicationManager.ManagedBy) ?? RotatingApplicationManager.ManagedByConfig;

    private async Task<string> ManagedByAsync(OpenIddictEntityFrameworkCoreApplication app, CancellationToken ct) =>
        ManagedBy(await Applications.GetStoredPropertiesAsync(app, ct));

    private async Task<OpenIddictEntityFrameworkCoreApplication> FindApplicationAsync(string clientId, CancellationToken ct) =>
        await Applications.FindByClientIdAsync(clientId, ct) ?? throw MembershipException.NotFound($"No client '{clientId}'.");

    private async Task<OpenIddictEntityFrameworkCoreApplication> UiManagedAsync(string clientId, CancellationToken ct)
    {
        var app = await FindApplicationAsync(clientId, ct);
        if (await ManagedByAsync(app, ct) != RotatingApplicationManager.ManagedByUi)
            throw new MembershipException(StatusCodes.Status409Conflict, "built_in",
                $"'{clientId}' is a built-in client: it is defined in the identity service's configuration, not here.");
        return app;
    }

    // ============================================================ scopes

    public async Task<IReadOnlyList<ScopeDetails>> ListScopesAsync(CancellationToken ct)
    {
        var stored = await db.Set<OpenIddictEntityFrameworkCoreScope>().AsNoTracking()
            .Select(s => new { s.Name, s.DisplayName, s.Description, s.Resources }).ToListAsync(ct);
        var standard = StandardScopes.Select(s => new ScopeDetails(s, s switch
        {
            Scopes.OpenId => "OpenID Connect sign-in",
            Scopes.Profile => "Name and profile",
            Scopes.Email => "Email address",
            _ => "Refresh tokens",
        }, "Standard (OpenID Connect).", [], true));
        return standard.Concat(stored.Select(s => new ScopeDetails(s.Name!, s.DisplayName, s.Description,
                string.IsNullOrEmpty(s.Resources) ? [] : JsonSerializer.Deserialize<string[]>(s.Resources) ?? [], false)))
            .ToList();
    }

    private static MembershipException Invalid(string field, string message) => new(StatusCodes.Status400BadRequest, field, message);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,63}$")]
    private static partial Regex ClientIdPattern();
}

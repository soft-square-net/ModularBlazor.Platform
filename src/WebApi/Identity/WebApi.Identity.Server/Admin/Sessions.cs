using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using soft.square.net.WebApi.Identity.Server.Auth;
using soft.square.net.WebApi.Identity.Server.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace soft.square.net.WebApi.Identity.Server.Admin;

/// <summary>
/// One sign-in of a member in a tenant: the tenant-bound ad-hoc authorization the sign-in created and the tokens
/// issued under it (the refresh token chain). It is <paramref name="Active"/> while the authorization is valid and a
/// refresh token of it is valid and unexpired; it ends when that token expires, or when it is revoked here, by
/// suspending or removing the member, or by signing out.
/// </summary>
/// <param name="Id">The authorization's id.</param>
/// <param name="ExpiresAt">When the newest valid refresh token expires; null when none is left.</param>
/// <param name="LastUsedAt">When the newest token of the session was issued (sign-in or the last refresh).</param>
public sealed record SessionDetails(
    string Id,
    string UserId,
    string? Email,
    string? DisplayName,
    string? ClientId,
    string? Application,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt,
    string Status,
    bool Active,
    int ValidTokens);

public sealed record SessionsRevoked(string Tenant, string? UserId, int RevokedSessions);

/// <summary>
/// Sessions of a tenant's members (see <see cref="SessionDetails"/>), and revoking them. Only authorizations tagged
/// with the tenant are seen or touched: the same person's sessions in other tenants stay invisible to that tenant.
/// Revoking ends the refresh tokens at once; an access token already issued stays valid until it expires (minutes),
/// and the WebApi's per-request membership check closes that gap for removed or suspended members.
/// </summary>
public sealed class Sessions(IdentityDb db, IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens,
    TenantAuthorizations tenantAuthorizations, SessionEnds ends)
{
    private const string TenantProperty = "tenant";

    public async Task<IReadOnlyList<SessionDetails>> ListAsync(string tenant, string? userId, bool includeEnded, CancellationToken ct)
    {
        // The tenant's members (or the one asked for); their authorizations, filtered by the tenant tag below (it is in
        // the JSON Properties column, so not in SQL).
        var members = db.TenantMemberships.Where(m => m.TenantId == tenant);
        if (!string.IsNullOrWhiteSpace(userId)) members = members.Where(m => m.UserId == userId);
        var people = await members
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Email, u.DisplayName })
            .GroupJoin(db.TenantProfiles.Where(p => p.TenantId == tenant), u => u.Id, p => p.UserId, (u, p) => new { u, p })
            .SelectMany(x => x.p.DefaultIfEmpty(), (x, p) => new { x.u.Id, x.u.Email, Name = p != null && p.DisplayName != null ? p.DisplayName : x.u.DisplayName })
            .ToDictionaryAsync(x => x.Id, ct);
        if (people.Count == 0) return [];

        var ids = people.Keys.ToList();
        var rows = await db.Set<OpenIddictEntityFrameworkCoreAuthorization>()
            .Where(a => a.Subject != null && ids.Contains(a.Subject) && a.Type == AuthorizationTypes.AdHoc)
            .Where(a => a.Properties != null && a.Properties.Contains(tenant))
            .Select(a => new
            {
                a.Id, a.Subject, a.Properties, a.Scopes, a.Status, a.CreationDate,
                ClientId = a.Application != null ? a.Application.ClientId : null,
                Application = a.Application != null ? a.Application.DisplayName : null,
                Tokens = a.Tokens.Select(t => new { t.Type, t.Status, t.CreationDate, t.ExpirationDate }).ToList(),
            })
            .AsNoTracking()
            .ToListAsync(ct);

        var now = TimeProvider.System.GetUtcNow();
        var sessions = new List<SessionDetails>();
        foreach (var a in rows)
        {
            if (!IsTenant(a.Properties, tenant)) continue;
            // Live tokens: access tokens (minutes) and refresh tokens (days, when the client asked for offline_access).
            var live = a.Tokens
                .Where(t => t.Type is TokenTypeIdentifiers.AccessToken or TokenTypeIdentifiers.RefreshToken && t.Status == Statuses.Valid
                    && (t.ExpirationDate is null || t.ExpirationDate > now.UtcDateTime))
                .ToList();
            var active = a.Status == Statuses.Valid && live.Count > 0;
            if (!active && !includeEnded) continue;

            var person = people[a.Subject!];
            sessions.Add(new SessionDetails(
                a.Id!, a.Subject!, person.Email, person.Name, a.ClientId, a.Application,
                ParseScopes(a.Scopes),
                Utc(a.CreationDate),
                Utc(a.Tokens.Max(t => t.CreationDate)),
                Utc(live.Max(t => t.ExpirationDate)),
                a.Status ?? "", active, live.Count));
        }
        return sessions.OrderByDescending(s => s.Active).ThenByDescending(s => s.LastUsedAt ?? s.CreatedAt).ToList();
    }

    /// <summary>Revokes one session of the tenant: its authorization and every token issued under it.</summary>
    public async Task<SessionsRevoked> RevokeAsync(string tenant, string sessionId, CancellationToken ct)
    {
        var authorization = await authorizations.FindByIdAsync(sessionId, ct);
        if (authorization is null || !IsTenant(await authorizations.GetPropertiesAsync(authorization, ct), tenant))
            throw MembershipException.NotFound($"No session '{sessionId}' in tenant '{tenant}'.");
        var userId = await authorizations.GetSubjectAsync(authorization, ct);
        var properties = await authorizations.GetPropertiesAsync(authorization, ct);
        await tokens.RevokeByAuthorizationIdAsync(sessionId, ct);
        var revoked = await authorizations.TryRevokeAsync(authorization, ct) ? 1 : 0;

        // The browser is likely still signed in to this service: make it sign in again for the tenant (SessionEnds).
        if (userId is not null)
        {
            if (properties.TryGetValue(TenantAuthorizations.LoginProperty, out var login) && login.ValueKind == JsonValueKind.Number)
                await ends.EndLoginAsync(userId, tenant, login.GetInt64());
            else
                await ends.EndAllAsync(userId, tenant); // an older authorization: its login is unknown
        }
        return new SessionsRevoked(tenant, userId, revoked);
    }

    /// <summary>Ends every sign-in of the user in the tenant; each browser signs in again before it gets a token here.</summary>
    public async Task<SessionsRevoked> RevokeAllAsync(string tenant, string userId, CancellationToken ct)
    {
        var revoked = await tenantAuthorizations.RevokeAsync(userId, tenant, ct);
        await ends.EndAllAsync(userId, tenant);
        return new SessionsRevoked(tenant, userId, revoked);
    }

    private static bool IsTenant(string? properties, string tenant)
    {
        if (string.IsNullOrEmpty(properties)) return false;
        try
        {
            using var json = JsonDocument.Parse(properties);
            return json.RootElement.TryGetProperty(TenantProperty, out var value) && value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), tenant, StringComparison.Ordinal);
        }
        catch (JsonException) { return false; }
    }

    private static bool IsTenant(IReadOnlyDictionary<string, JsonElement> properties, string tenant) =>
        properties.TryGetValue(TenantProperty, out var value) && value.ValueKind == JsonValueKind.String
        && string.Equals(value.GetString(), tenant, StringComparison.Ordinal);

    private static IReadOnlyList<string> ParseScopes(string? scopes)
    {
        if (string.IsNullOrEmpty(scopes)) return [];
        try { return JsonSerializer.Deserialize<string[]>(scopes) ?? []; }
        catch (JsonException) { return []; }
    }

    private static DateTimeOffset? Utc(DateTime? value) =>
        value is { } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;
}

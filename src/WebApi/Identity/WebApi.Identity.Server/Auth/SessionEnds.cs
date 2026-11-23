using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using soft.square.net.WebApi.Identity.Server.Data;

namespace soft.square.net.WebApi.Identity.Server.Auth;

/// <summary>
/// When this browser signed in to the identity service: a <c>login_at</c> claim (UTC ticks) in the login cookie, added
/// when the cookie is issued and kept when the security stamp check refreshes the principal. Unlike the cookie's
/// IssuedUtc it does not move on sliding renewal, so it identifies one login of one browser.
/// </summary>
public static class LoginTime
{
    public const string Claim = "login_at";

    public static void AddLoginTime(this IServiceCollection services)
    {
        services.ConfigureApplicationCookie(o =>
        {
            var signingIn = o.Events.OnSigningIn;
            o.Events.OnSigningIn = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity && !identity.HasClaim(c => c.Type == Claim))
                    identity.AddClaim(new Claim(Claim, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)));
                return signingIn(context);
            };
        });
        services.Configure<SecurityStampValidatorOptions>(o =>
        {
            var refreshing = o.OnRefreshingPrincipal;
            o.OnRefreshingPrincipal = context =>
            {
                if (context.CurrentPrincipal?.FindFirst(Claim) is { } login && context.NewPrincipal?.Identity is ClaimsIdentity identity
                    && !identity.HasClaim(c => c.Type == Claim))
                    identity.AddClaim(new Claim(Claim, login.Value));
                return refreshing?.Invoke(context) ?? Task.CompletedTask;
            };
        });
    }

    /// <summary>The login's time (UTC ticks), or null for a cookie issued before this claim existed.</summary>
    public static long? Read(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirst(Claim)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ? ticks : null;
}

/// <summary>
/// Ended sign-ins, per user and tenant. Revoking an authorization stops its refresh tokens, but a browser that is still
/// signed in to this service would silently get a new authorization (prompt=none) the next time its client renews. So
/// ending a sign-in also records it here, and <c>/connect/authorize</c> asks that browser to sign in again before it
/// issues anything for the tenant:
/// <list type="bullet">
/// <item>ending one sign-in records its login (<see cref="LoginTime"/>, kept on the authorization);</item>
/// <item>ending all of a member's sign-ins in the tenant records a time: logins before it must sign in again.</item>
/// </list>
/// Kept in the user's authentication tokens (AspNetUserTokens, provider <c>tenant-sessions</c>, one row per tenant), so
/// nothing reaches the login cookie or a token, and other tenants are not affected.
/// </summary>
public sealed class SessionEnds(UserManager<ApplicationUser> users)
{
    private const string Provider = "tenant-sessions";

    /// <summary>The key for platform tokens (no tenant).</summary>
    public const string Platform = "*";
    private static readonly TimeSpan KeepEnded = TimeSpan.FromDays(30);

    private sealed record State(long? NotBefore, List<long> Ended);

    /// <summary>Every login of the user from before now must sign in again for the tenant.</summary>
    public async Task EndAllAsync(string userId, string tenant)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return;
        var now = DateTimeOffset.UtcNow.UtcTicks;
        await SaveAsync(user, tenant, new State(now, []));
    }

    /// <summary>One login (one browser) must sign in again for the tenant.</summary>
    public async Task EndLoginAsync(string userId, string tenant, long loginAt)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return;
        var state = await LoadAsync(user, tenant);
        var oldest = DateTimeOffset.UtcNow.UtcTicks - KeepEnded.Ticks;
        var ended = state.Ended.Where(t => t >= oldest && t != loginAt).Append(loginAt).ToList();
        await SaveAsync(user, tenant, state with { Ended = ended });
    }

    /// <summary>Whether a browser whose login was at <paramref name="loginAt"/> must sign in again for the tenant.</summary>
    public async Task<bool> MustSignInAgainAsync(ApplicationUser user, string tenant, long? loginAt)
    {
        var state = await LoadAsync(user, tenant);
        if (state.NotBefore is null && state.Ended.Count == 0) return false;
        if (loginAt is not { } at) return true; // an older cookie: it can not tell which login it is
        return at < state.NotBefore || state.Ended.Contains(at);
    }

    private async Task<State> LoadAsync(ApplicationUser user, string tenant)
    {
        var json = await users.GetAuthenticationTokenAsync(user, Provider, tenant);
        if (string.IsNullOrEmpty(json)) return new State(null, []);
        try { return JsonSerializer.Deserialize<State>(json) ?? new State(null, []); }
        catch (JsonException) { return new State(null, []); }
    }

    private Task SaveAsync(ApplicationUser user, string tenant, State state) =>
        users.SetAuthenticationTokenAsync(user, Provider, tenant, JsonSerializer.Serialize(state));
}

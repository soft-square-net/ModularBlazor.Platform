using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using soft.square.net.WebApi.Identity.Server.Auth;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace soft.square.net.WebApi.Identity.Server.Data;

public sealed class IdentityServerOptions
{
    /// <summary>The issuer (the service's public URL). Null: OpenIddict uses the request's base URL.</summary>
    public Uri? Issuer { get; set; }
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);
    public BlazorClientOptions BlazorClient { get; set; } = new();
    public ServiceClientOptions ElsaClient { get; set; } = new();
    /// <summary>The WebApi's identity-admin module: the only caller of the service admin API (scope identity.admin).</summary>
    public ServiceClientOptions AdminClient { get; set; } = new() { ClientId = "webapi-identity-admin" };
    public SeedOptions Seed { get; set; } = new();
}

public sealed class BlazorClientOptions
{
    public string ClientId { get; set; } = "blazor-client";
    public List<string> RedirectUris { get; set; } = [];
    public List<string> PostLogoutRedirectUris { get; set; } = [];
}

public sealed class ServiceClientOptions
{
    public string ClientId { get; set; } = "elsa-workflows";
    /// <summary>Development value in appsettings.Development.json; production from a secret store.</summary>
    public string? ClientSecret { get; set; }
}

public sealed class SeedOptions
{
    public List<TenantSeed> Tenants { get; set; } = [];
    public List<UserSeed> Users { get; set; } = [];
}

public sealed class TenantSeed
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool RequireMfa { get; set; }
}

public sealed class UserSeed
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? DisplayName { get; set; }
    public List<string> PlatformRoles { get; set; } = [];
    public List<MembershipSeed> Memberships { get; set; } = [];
}

public sealed class MembershipSeed
{
    public string Tenant { get; set; } = "";
    public string Role { get; set; } = MembershipRoles.Member;
    public string? DisplayName { get; set; }
}

/// <summary>Migrates the database, registers the clients and scopes, and seeds the development data.</summary>
public static class IdentitySeeder
{
    public static async Task InitializeAsync(IServiceProvider root, CancellationToken ct = default)
    {
        await using var scope = root.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityServerOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        // EF Core 9+ takes a database lock around migrations, so concurrent replicas are safe.
        await services.GetRequiredService<IdentityDb>().Database.MigrateAsync(ct);

        await EnsureScopesAsync(services.GetRequiredService<IOpenIddictScopeManager>(), ct);
        await EnsureClientsAsync(services.GetRequiredService<IOpenIddictApplicationManager>(), options, logger, ct);
        await SeedDataAsync(services, options.Seed, logger, ct);
    }

    private static async Task EnsureScopesAsync(IOpenIddictScopeManager scopes, CancellationToken ct)
    {
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = IdentityClaims.ApiScope,
            DisplayName = "Modular WebApi",
            Resources = { IdentityClaims.Audience },
        };
        if (await scopes.FindByNameAsync(descriptor.Name, ct) is { } existing) await scopes.UpdateAsync(existing, descriptor, ct);
        else await scopes.CreateAsync(descriptor, ct);

        // The service admin API: a token for this service itself (audience modular-identity), not for the WebApi.
        var admin = new OpenIddictScopeDescriptor
        {
            Name = IdentityClaims.AdminScope,
            DisplayName = "Identity service administration",
            Resources = { IdentityClaims.IdentityAudience },
        };
        if (await scopes.FindByNameAsync(admin.Name, ct) is { } existingAdmin) await scopes.UpdateAsync(existingAdmin, admin, ct);
        else await scopes.CreateAsync(admin, ct);
    }

    private static async Task EnsureClientsAsync(IOpenIddictApplicationManager applications, IdentityServerOptions options, ILogger logger, CancellationToken ct)
    {
        // Public SPA client: Authorization Code + PKCE, refresh tokens, no secret.
        var blazor = new OpenIddictApplicationDescriptor
        {
            ClientId = options.BlazorClient.ClientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Modular Blazor",
            Permissions =
            {
                Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.EndSession,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Profile, Permissions.Scopes.Email, Permissions.Prefixes.Scope + IdentityClaims.ApiScope,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        foreach (var uri in options.BlazorClient.RedirectUris) blazor.RedirectUris.Add(new Uri(uri));
        foreach (var uri in options.BlazorClient.PostLogoutRedirectUris) blazor.PostLogoutRedirectUris.Add(new Uri(uri));
        await UpsertAsync(applications, blazor, ct, logger);

        // The WebApi's identity-admin module: client credentials, the service admin scope only. Not registered without
        // a secret (Development: appsettings.Development.json; elsewhere a secret store).
        if (string.IsNullOrEmpty(options.AdminClient.ClientSecret))
            logger.LogWarning("Identity:AdminClient:ClientSecret is not set; the {Client} client is not registered.", options.AdminClient.ClientId);
        else
            await UpsertAsync(applications, new OpenIddictApplicationDescriptor
            {
                ClientId = options.AdminClient.ClientId,
                ClientSecret = options.AdminClient.ClientSecret,
                ClientType = ClientTypes.Confidential,
                DisplayName = "WebApi identity admin",
                Permissions =
                {
                    Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + IdentityClaims.AdminScope,
                },
            }, ct, logger);

        // Confidential service client: client credentials only.
        if (string.IsNullOrEmpty(options.ElsaClient.ClientSecret))
        {
            logger.LogWarning("Identity:ElsaClient:ClientSecret is not set; the {Client} client is not registered.", options.ElsaClient.ClientId);
            return;
        }
        var elsa = new OpenIddictApplicationDescriptor
        {
            ClientId = options.ElsaClient.ClientId,
            ClientSecret = options.ElsaClient.ClientSecret,
            ClientType = ClientTypes.Confidential,
            DisplayName = "Elsa Workflows",
            Permissions =
            {
                Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials,
                Permissions.Prefixes.Scope + IdentityClaims.ApiScope,
            },
        };
        await UpsertAsync(applications, elsa, ct, logger);

    /*───────────────────────Ahmed_Galal───────────────────────────*/
    var scalar = new OpenIddictApplicationDescriptor
    {
      ClientId = "scalar-client",
      ClientType = ClientTypes.Public,
      ConsentType = ConsentTypes.Implicit,
      DisplayName = "Scalar API Documentation",

      Permissions =
    {
        Permissions.Endpoints.Authorization,
        Permissions.Endpoints.Token,

        Permissions.GrantTypes.AuthorizationCode,
        Permissions.GrantTypes.RefreshToken,

        Permissions.ResponseTypes.Code,

        Permissions.Scopes.Profile,
        Permissions.Scopes.Email,
        Permissions.Prefixes.Scope + IdentityClaims.ApiScope,
        Permissions.Prefixes.Scope + Scopes.OfflineAccess
    },

      Requirements =
    {
        Requirements.Features.ProofKeyForCodeExchange
    }
    };

    // Register the actual Scalar redirect URI here.
    scalar.RedirectUris.Add(
        new Uri("http://localhost:5080/scalar/"));
    scalar.RedirectUris.Add(new Uri("http://localhost:5080/pools/core/scalar/"));
    scalar.RedirectUris.Add(new Uri("http://localhost:5080/pools/sales/scalar/"));
    scalar.RedirectUris.Add(new Uri("http://localhost:5080/pools/worker/scalar/"));

    await UpsertAsync(applications, scalar, ct);
    /*───────────────────────Ahmed_Galal───────────────────────────*/
  }

  /// <summary>
  /// Creates or updates a client from configuration (marked managed_by=config: the admin UI shows it, never edits it).
  /// The configured secret wins, except around a rotation done in the admin UI (RotatingApplicationManager):
  /// <list type="bullet">
  /// <item>it is the pending next secret: the secret store was updated, so the rotation completes here;</item>
  /// <item>the secret was rotated in the UI and configuration still has an older one: the rotated secret stays (a warning
  /// says to update the secret store), so a restart never locks the client out.</item>
  /// </list>
  /// </summary>
  private static async Task UpsertAsync(IOpenIddictApplicationManager applications, OpenIddictApplicationDescriptor descriptor, CancellationToken ct,
      ILogger? logger = null)
    {
        descriptor.Properties[RotatingApplicationManager.ManagedBy] = JsonSerializer.SerializeToElement(RotatingApplicationManager.ManagedByConfig);
        var manager = (RotatingApplicationManager)applications;
        if (await manager.FindByClientIdAsync(descriptor.ClientId!, ct) is not { } existing)
        {
            await manager.CreateAsync(descriptor, ct);
            return;
        }

        var stored = await manager.GetStoredPropertiesAsync(existing, ct);
        foreach (var key in new[] { RotatingApplicationManager.NextSecret, RotatingApplicationManager.NextSecretCreated,
                     RotatingApplicationManager.NextSecretUsed, RotatingApplicationManager.SecretRotated })
            if (stored.TryGetValue(key, out var value)) descriptor.Properties[key] = value;

        string? promote = null;
        if (descriptor.ClientSecret is { Length: > 0 } configured)
        {
            var current = await manager.GetSecretHashAsync(existing, ct);
            if (await manager.IsCurrentSecretAsync(existing, configured, ct))
                descriptor.ClientSecret = current; // unchanged: keep the hash as it is
            else if (await manager.IsNextSecretAsync(existing, configured, ct))
            {
                promote = RotatingApplicationManager.Text(stored, RotatingApplicationManager.NextSecret);
                descriptor.ClientSecret = current;
                foreach (var key in new[] { RotatingApplicationManager.NextSecret, RotatingApplicationManager.NextSecretCreated, RotatingApplicationManager.NextSecretUsed })
                    descriptor.Properties.Remove(key);
                descriptor.Properties[RotatingApplicationManager.SecretRotated] = JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow.ToString("O"));
                logger?.LogInformation("The configured secret of {Client} is its rotated one: the rotation is complete.", descriptor.ClientId);
            }
            else if (stored.ContainsKey(RotatingApplicationManager.SecretRotated))
            {
                descriptor.ClientSecret = current;
                logger?.LogWarning("The configured secret of {Client} is older than the one rotated in the admin UI; the rotated one stays. Update the secret store.", descriptor.ClientId);
            }
            else
                foreach (var key in new[] { RotatingApplicationManager.NextSecret, RotatingApplicationManager.NextSecretCreated, RotatingApplicationManager.NextSecretUsed })
                    descriptor.Properties.Remove(key); // a new secret from the deployment: it replaces the current one
        }

        await manager.UpdateAsync(existing, descriptor, ct);
        if (promote is not null) await manager.SetSecretHashAsync(existing, promote, ct);
    }

    private static async Task SeedDataAsync(IServiceProvider services, SeedOptions seed, ILogger logger, CancellationToken ct)
    {
        var db = services.GetRequiredService<IdentityDb>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in IdentityClaims.PlatformRoles)
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));

        foreach (var tenant in seed.Tenants)
            if (!await db.Tenants.AnyAsync(t => t.TenantId == tenant.Id, ct))
                db.Tenants.Add(new TenantSettings { TenantId = tenant.Id, DisplayName = tenant.DisplayName, RequireMfa = tenant.RequireMfa });
        await db.SaveChangesAsync(ct);

        foreach (var entry in seed.Users)
        {
            var user = await users.FindByEmailAsync(entry.Email);
            if (user is null)
            {
                user = new ApplicationUser { UserName = entry.Email, Email = entry.Email, EmailConfirmed = true, DisplayName = entry.DisplayName };
                var created = await users.CreateAsync(user, entry.Password);
                if (!created.Succeeded)
                {
                    logger.LogError("Seeding {Email} failed: {Errors}", entry.Email, string.Join("; ", created.Errors.Select(e => e.Description)));
                    continue;
                }
                logger.LogInformation("Seeded account {Email}.", entry.Email);
            }

            foreach (var role in entry.PlatformRoles)
                if (!await users.IsInRoleAsync(user, role)) await users.AddToRoleAsync(user, role);

            foreach (var m in entry.Memberships)
            {
                if (await db.TenantMemberships.AnyAsync(x => x.UserId == user.Id && x.TenantId == m.Tenant, ct)) continue;
                db.TenantMemberships.Add(new TenantMembership
                {
                    UserId = user.Id, TenantId = m.Tenant, Role = m.Role, Status = MembershipStatus.Active, JoinedAt = DateTimeOffset.UtcNow,
                });
                if (m.DisplayName is not null)
                    db.TenantProfiles.Add(new TenantProfile { UserId = user.Id, TenantId = m.Tenant, DisplayName = m.DisplayName });
            }
            await db.SaveChangesAsync(ct);
        }
    }



}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modularity;
using soft.square.net.WebApi.Security;
using soft.square.net.WebApi.Tenancy;

namespace soft.square.net.WebApi.Tests.Tenancy;

/// <summary>
/// Tenant resolution against tenants this deployment does not know (foreign tenants), on a real host: tenancy from
/// configuration (acme active, closed suspended), security with test JWTs, and three endpoints:
/// <c>/optional</c> (a runtime-module endpoint marked TenantOptional), <c>/required</c> (a runtime-module endpoint that
/// needs a tenant) and <c>/host</c> (a plain host endpoint).
/// </summary>
public sealed class ForeignTenantTests : IAsyncLifetime
{
    private const string Issuer = "tenancy-tests";
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("tenancy-tests-signing-key-0123456789-abcdef");

    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public sealed record Seen(string? Tenant, string? Foreign, bool IsForeign, string? ResolvedBy, string[] Permissions, bool HasGrant);

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenancy:Tenants:0:Id"] = "acme",
            ["Tenancy:Tenants:0:Hosts:0"] = "acme.test",
            ["Tenancy:Tenants:1:Id"] = "closed",
            ["Tenancy:Tenants:1:Status"] = "Suspended",
            ["Authentication:Schemes:Bearer:ValidIssuer"] = Issuer,
            ["Authentication:Schemes:Bearer:ValidAudiences:0"] = Issuer,
            ["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = Issuer,
            ["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = Convert.ToBase64String(Key),
        });
        builder.AddPlatformTenancy("broker");
        builder.AddPlatformSecurity("broker");

        _app = builder.Build();
        _app.UseRouting();
        _app.UsePlatformSecurity();
        var module = new RuntimeModuleMetadata("fake", "1.0.0");
        _app.MapGet("/optional", Describe).WithMetadata(module).TenantOptional().AllowAnonymous();
        _app.MapGet("/required", Describe).WithMetadata(module).AllowAnonymous();
        _app.MapGet("/host", Describe).AllowAnonymous();
        await _app.StartAsync();
        await _app.Services.GetRequiredService<TenantDirectory>().Ready;

        // A grant stored for "newsdesk" (a tenant this deployment does not know): it must never count.
        await _app.Services.GetRequiredService<IGrantRepository>().AddAsync(new AccessGrant(Guid.NewGuid(), "newsdesk", "u1", "org.read",
            AccessScope.Tenant, null, null, GrantSources.Standing, DateTimeOffset.UtcNow, null), TestContext.Current.CancellationToken);
        await _app.Services.GetRequiredService<IGrantRepository>().AddAsync(new AccessGrant(Guid.NewGuid(), "acme", "u1", "org.read",
            AccessScope.Tenant, null, null, GrantSources.Standing, DateTimeOffset.UtcNow, null), TestContext.Current.CancellationToken);

        var address = _app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        _http = new HttpClient { BaseAddress = new Uri(address) };
    }

    private static async Task<Seen> Describe(ITenantContext tenant, IAccessScopes access, CancellationToken ct) =>
        new(tenant.TenantId, tenant.ForeignTenantId, tenant.IsForeign, tenant.ResolvedBy,
            [.. await access.PermissionsAsync(ct)], await access.HasPermissionAsync("org.read", ct));

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static string Token(string userId, string? tenant, params string[] roles)
    {
        var claims = new Dictionary<string, object> { [PlatformClaims.Subject] = userId };
        if (tenant is not null) claims[PlatformClaims.Tenant] = tenant;
        if (roles.Length > 0) claims[PlatformClaims.Role] = roles;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Issuer,
            Claims = claims,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Key), SecurityAlgorithms.HmacSha256)
        });
    }

    private Task<HttpResponseMessage> GetAsync(string path, string? token = null, string? header = null, string? host = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (header is not null) request.Headers.Add(TenancyNames.Header, header);
        if (host is not null) request.Headers.Add("X-Forwarded-Host", host);
        return _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<Seen> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<Seen>(TestContext.Current.CancellationToken))!;
    }

    // --- Endpoints that work without a tenant ---

    [Fact]
    public async Task An_unknown_token_tenant_is_downgraded_to_a_foreign_caller_with_no_rights()
    {
        var seen = await ReadAsync(await GetAsync("/optional", Token("u1", "newsdesk", PlatformRoles.Admin)));

        seen.Tenant.ShouldBeNull();
        seen.Foreign.ShouldBe("newsdesk");
        seen.IsForeign.ShouldBeTrue();
        seen.ResolvedBy.ShouldBe("claim");
        seen.Permissions.ShouldBeEmpty();   // no platform-role permissions
        seen.HasGrant.ShouldBeFalse();      // and not the grant stored under "newsdesk"
    }

    [Fact]
    public async Task An_unknown_header_tenant_is_downgraded_and_marked_as_from_the_header()
    {
        var seen = await ReadAsync(await GetAsync("/optional", header: "newsdesk"));

        seen.Tenant.ShouldBeNull();
        seen.Foreign.ShouldBe("newsdesk");
        seen.ResolvedBy.ShouldBe("header");
    }

    [Fact]
    public async Task An_unknown_header_with_a_token_for_the_same_tenant_counts_as_the_token()
    {
        var seen = await ReadAsync(await GetAsync("/optional", Token("u1", "newsdesk"), header: "NewsDesk"));

        seen.Foreign.ShouldBe("NewsDesk");
        seen.ResolvedBy.ShouldBe("claim");
        seen.HasGrant.ShouldBeFalse();
    }

    [Fact]
    public async Task An_unknown_header_with_a_token_for_another_tenant_is_refused()
    {
        (await GetAsync("/optional", Token("u1", "acme"), header: "newsdesk")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/optional", Token("u1", "elsewhere"), header: "newsdesk")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_unknown_header_on_a_tenant_host_is_refused()
    {
        (await GetAsync("/optional", header: "newsdesk", host: "acme.test")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_known_tenant_is_never_foreign()
    {
        var seen = await ReadAsync(await GetAsync("/optional", Token("u1", "acme")));

        seen.Tenant.ShouldBe("acme");
        seen.Foreign.ShouldBeNull();
        seen.IsForeign.ShouldBeFalse();
        seen.HasGrant.ShouldBeTrue();
    }

    [Fact]
    public async Task A_suspended_tenant_is_still_refused()
    {
        (await GetAsync("/optional", Token("u1", "closed"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/optional", header: "closed")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task No_tenant_at_all_is_a_plain_platform_caller()
    {
        var seen = await ReadAsync(await GetAsync("/optional", Token("u1", null, PlatformRoles.Admin)));

        seen.Tenant.ShouldBeNull();
        seen.Foreign.ShouldBeNull();
        seen.IsForeign.ShouldBeFalse();
        seen.Permissions.ShouldContain(PlatformPermissions.TenantsManage); // platform roles still apply without a tenant
    }

    // --- Endpoints that need a tenant: unchanged ---

    [Fact]
    public async Task An_endpoint_that_needs_a_tenant_still_refuses_unknown_tenants()
    {
        (await GetAsync("/required", Token("u1", "newsdesk"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/required", header: "newsdesk")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync("/required", Token("u1", "closed"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/required")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadAsync(await GetAsync("/required", Token("u1", "acme")))).Tenant.ShouldBe("acme");
    }

    [Fact]
    public async Task A_host_endpoint_still_refuses_unknown_tenants()
    {
        (await GetAsync("/host", Token("u1", "newsdesk"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("/host", header: "newsdesk")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync("/host", header: "closed")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(await GetAsync("/host"))).Foreign.ShouldBeNull();
    }

    [Fact]
    public void A_foreign_scope_cannot_be_given_a_tenant_later()
    {
        var context = new TenantContext();
        context.UseForeign("newsdesk", "claim");
        Should.Throw<InvalidOperationException>(() => context.Use(new TenantInfo("acme", "Acme", TenantStatus.Active, [], "", ""), "scope"));
    }
}

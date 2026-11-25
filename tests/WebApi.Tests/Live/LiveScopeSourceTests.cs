using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Live;
using soft.square.net.WebApi.Tenancy;

namespace soft.square.net.WebApi.Tests.Live;

/// <summary>
/// The host side of <see cref="ILiveScopeResolver"/>: resolved per call from the module's own services, in a scope of
/// the subscriber's tenant; forgotten when the module is released; failing closed.
/// </summary>
public sealed class LiveScopeSourceTests
{
    private static readonly TenantInfo Acme = new("acme", "Acme", TenantStatus.Active, [], "", "");

    private static IServiceProvider Module(Func<string, string, string, IReadOnlyList<string>> resolve)
    {
        var services = new ServiceCollection();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantScopeInitializer>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ILiveScopeResolver>(sp => new Resolver(sp.GetRequiredService<ITenantContext>(), resolve));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Resolves_from_the_loaded_module_in_the_tenants_scope_and_forgets_it_on_release()
    {
        var source = new ModuleLiveScopeSource(NullLogger<ModuleLiveScopeSource>.Instance);
        var module = Module((tenant, kind, key) => [$"{tenant}/{kind}/{key}"]);

        (await source.ScopesAsync("org", Acme, "unit", "desk", default)).ShouldBeEmpty(); // not loaded here

        source.Loaded("org", module);
        (await source.ScopesAsync("org", Acme, "unit", "desk", default)).ShouldBe(["acme/unit/desk"]);

        await source.ReleasingAsync("org", module);
        (await source.ScopesAsync("org", Acme, "unit", "desk", default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Releasing_an_old_copy_keeps_the_new_one()
    {
        var source = new ModuleLiveScopeSource(NullLogger<ModuleLiveScopeSource>.Instance);
        var old = Module((_, _, _) => ["old"]);
        var current = Module((_, _, _) => ["new"]);
        source.Loaded("org", old);
        source.Loaded("org", current); // a swap loads the new copy before releasing the old one
        await source.ReleasingAsync("org", old);
        (await source.ScopesAsync("org", Acme, "unit", "desk", default)).ShouldBe(["new"]);
    }

    [Fact]
    public async Task A_failing_resolver_or_none_gives_no_scopes()
    {
        var source = new ModuleLiveScopeSource(NullLogger<ModuleLiveScopeSource>.Instance);
        source.Loaded("broken", Module((_, _, _) => throw new InvalidOperationException("boom")));
        (await source.ScopesAsync("broken", Acme, "post", "1", default)).ShouldBeEmpty();

        var plain = new ServiceCollection();
        plain.AddScoped<TenantContext>();
        plain.AddScoped<ITenantScopeInitializer>(sp => sp.GetRequiredService<TenantContext>());
        source.Loaded("plain", plain.BuildServiceProvider());
        (await source.ScopesAsync("plain", Acme, "post", "1", default)).ShouldBeEmpty();
    }

    private sealed class Resolver(ITenantContext tenant, Func<string, string, string, IReadOnlyList<string>> resolve) : ILiveScopeResolver
    {
        public Task<IReadOnlyList<string>> ScopesAsync(string kind, string key, CancellationToken ct) =>
            Task.FromResult(resolve(tenant.Required.Id, kind, key));
    }
}

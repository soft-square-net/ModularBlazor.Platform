using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Messaging;
using soft.square.net.WebApi.Modularity;
using soft.square.net.WebApi.Tenancy.Seeds;

namespace soft.square.net.WebApi.Tenancy;

public static class TenancyHostExtensions
{
    /// <summary>Connection string of the tenant store (registry of tenants).</summary>
    public const string StoreConnectionName = "TenantStore";

    /// <summary>
    /// Host-level tenancy, on every replica of every pool: the tenant directory (tenant store, cached, refreshed over
    /// the broker <paramref name="brokerConnectionName"/>), <see cref="ITenantContext"/> for every request and tenant
    /// scope, tenant database connections, and per-tenant initialization of runtime modules. Register before the
    /// runtime modules so the directory loads before they activate.
    /// </summary>
    public static IHostApplicationBuilder AddPlatformTenancy(this IHostApplicationBuilder builder, string brokerConnectionName)
    {
        var services = builder.Services;
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantScopeInitializer>(sp => sp.GetRequiredService<TenantContext>());

        services.AddSingleton<TenantDirectory>();
        services.AddSingleton<ITenantDirectory>(sp => sp.GetRequiredService<TenantDirectory>());
        services.AddHostedService<TenantDirectoryService>();
        services.AddSingleton<TenantDatabases>();

        services.AddSingleton<ModuleTenantInitializer>();
        services.AddSingleton<IModuleLifecycleObserver>(sp => sp.GetRequiredService<ModuleTenantInitializer>());

        var databases = new TenantDatabases(builder.Configuration);
        if (databases.IsConfigured(StoreConnectionName))
        {
            // On the store's own engine (DatabaseServers:TenantStore:Provider; PostgreSql when not set).
            var store = databases.Store;
            services.AddDbContext<TenantStoreDbContext>(options => TenantStoreDbContext.Configure(options, store));
            services.AddScoped<TenantRegistry>();
            services.AddScoped<TenantSubscriptions>();
            services.TryAddSingleton(TimeProvider.System);
            AddTenantSeeds(builder);
        }

        services.TryAddSingleton<ITenantChangeBus, NullTenantChangeBus>();
        if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(brokerConnectionName)))
        {
            // The RabbitMQ client itself is registered by AddRuntimeModuleBroadcast (same connection name).
            services.Replace(ServiceDescriptor.Singleton<ITenantChangeBus, RabbitMqTenantChangeBus>());
            services.AddHostedService<TenantChangeSubscriber>();
        }
        return builder;
    }

    /// <summary>
    /// Tenant seeds (needs the tenant store): <see cref="TenantSeedReconciler"/> pulls the seeds of
    /// <c>Platform:DeploymentId</c> from the loaded modules that serve them (<see cref="ITenantSeedSource"/>) and
    /// reconciles them into the store, one replica at a time (Redis <c>Tenancy:Seeds:LockConnectionName</c>, default
    /// <c>redis</c>, or the tenant store's database lock). Placement: <see cref="ITenantPlacementPolicy"/>, by default
    /// <see cref="AcceptAsIsPlacementPolicy"/> (register another one before this call to replace it). A cycle also runs
    /// on every replica when <c>tenancy.seed-changed.v1</c> arrives (a host integration subscription).
    /// </summary>
    private static void AddTenantSeeds(IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddOptions<TenantSeedOptions>().BindConfiguration(TenantSeedOptions.Section);
        services.AddSingleton<TenantSeedSources>();
        services.AddSingleton<IModuleLifecycleObserver>(sp => sp.GetRequiredService<TenantSeedSources>());
        services.TryAddSingleton<ITenantPlacementPolicy, AcceptAsIsPlacementPolicy>();
        services.AddSingleton<ITenantReadiness>(sp => sp.GetRequiredService<ModuleTenantInitializer>());
        services.AddScoped<ITenantDatabaseCreator, TenantDatabaseCreator>();

        var lockConnection = builder.Configuration[$"{TenantSeedOptions.Section}:{nameof(TenantSeedOptions.LockConnectionName)}"] ?? "redis";
        if (builder.Configuration.GetConnectionString(lockConnection) is { Length: > 0 } redis)
            services.AddSingleton<ILeaderLock>(sp => new RedisLeaderLock(redis, sp.GetRequiredService<ILogger<RedisLeaderLock>>()));
        else
            services.AddSingleton<ILeaderLock, DatabaseLeaderLock>();

        services.AddSingleton<TenantSeedReconciler>();
        services.AddHostedService(sp => sp.GetRequiredService<TenantSeedReconciler>());
        services.AddHostIntegrationHandler<SeedChangedTrigger>(TenantSeedNames.SeedChangedEvent);
    }

    /// <summary>Resolves the tenant of every request (after routing, so it can read the endpoint's metadata).</summary>
    public static IApplicationBuilder UseTenancy(this IApplicationBuilder app) => app.UseMiddleware<TenantResolutionMiddleware>();
}

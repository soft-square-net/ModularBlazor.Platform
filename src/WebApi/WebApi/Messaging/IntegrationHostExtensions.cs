using RabbitMQ.Client;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Modularity;

namespace soft.square.net.WebApi.Messaging;

public static class IntegrationHostExtensions
{
    /// <summary>
    /// Integration events between runtime modules (see the contracts in <c>WebApi.Contracts/Messaging</c>):
    /// <list type="bullet">
    /// <item>with the broker <paramref name="brokerConnectionName"/>: <see cref="IBrokerPublisher"/> publishes to the topic
    /// exchange <c>integration</c>; each subscriber module has a durable queue per pool (<c>integration.{module}.{pool}</c>),
    /// consumed by one consumer per replica while the module is loaded, with retries and a dead-letter exchange;</item>
    /// <item>without it: an in-process bus delivers to the subscriber modules loaded on this process.</item>
    /// </list>
    /// Modules publish through their own outbox (<c>AddIntegrationEvents&lt;TDbContext&gt;()</c>). Register after the
    /// tenancy and the runtime modules (and after <c>AddRuntimeModuleBroadcast</c>, which adds the RabbitMQ client).
    /// </summary>
    public static IHostApplicationBuilder AddPlatformIntegrationEvents(this IHostApplicationBuilder builder, string brokerConnectionName)
    {
        var services = builder.Services;
        services.Configure<IntegrationOptions>(builder.Configuration.GetSection("Integration"));
        services.AddSingleton<ModuleSubscriptions>();
        services.AddSingleton<IntegrationDelivery>();
        services.AddSingleton<IntegrationConsumers>();
        services.AddSingleton<IModuleLifecycleObserver>(sp => sp.GetRequiredService<IntegrationConsumers>());

        if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(brokerConnectionName)))
        {
            if (!services.Any(d => d.ServiceType == typeof(IConnection))) builder.AddRabbitMQClient(brokerConnectionName);
            services.AddSingleton<IntegrationTopology>();
            services.AddSingleton<IModuleCatalogObserver>(sp => sp.GetRequiredService<IntegrationTopology>());
            services.AddSingleton<RabbitMqBrokerPublisher>();
            services.AddSingleton<IBrokerPublisher>(sp => sp.GetRequiredService<RabbitMqBrokerPublisher>());
            services.AddSingleton<IIntegrationTransport, RabbitMqIntegrationTransport>();
            services.AddSingleton<IDeadLetters, RabbitMqDeadLetters>();
        }
        else
        {
            services.AddSingleton<LocalIntegrationBus>();
            services.AddSingleton<IBrokerPublisher>(sp => sp.GetRequiredService<LocalIntegrationBus>());
            services.AddSingleton<IIntegrationTransport>(sp => sp.GetRequiredService<LocalIntegrationBus>());
            services.AddSingleton<LocalDeadLetters>();
            services.AddSingleton<IDeadLetters>(sp => sp.GetRequiredService<LocalDeadLetters>());
        }
        return builder;
    }
}

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection.Extensions;
using soft.square.net.WebApi.Contracts.Live;

namespace soft.square.net.WebApi.Live;

public static class LiveHostExtensions
{
    /// <summary>
    /// The live hub (<see cref="LiveHubMethods.Path"/>) and <see cref="ILiveNotifier"/> for modules. With a broker
    /// (<c>ConnectionStrings:{brokerConnectionName}</c>; its client is registered by <c>AddRuntimeModuleBroadcast</c>), every
    /// replica receives every message over the fanout "live"; without one, messages reach this replica's connections.
    /// Call after <c>AddPlatformSecurity</c>.
    /// </summary>
    public static IHostApplicationBuilder AddPlatformLive(this IHostApplicationBuilder builder, string brokerConnectionName)
    {
        var services = builder.Services;
        services.AddSignalR();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<LiveReplica>();
        services.AddSingleton<LiveConnections>();
        services.AddSingleton<LiveDispatcher>();
        services.AddSingleton<LiveTopicAuthorizer>();
        services.TryAddSingleton<ILiveTopicCatalog, ModuleLiveTopicCatalog>();
        services.AddSingleton<LiveRevalidator>();
        services.AddHostedService(sp => sp.GetRequiredService<LiveRevalidator>());
        services.AddScoped<ILiveNotifier, LiveNotifier>();

        if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(brokerConnectionName)))
        {
            services.AddSingleton<ILiveBus, RabbitMqLiveBus>();
            services.AddHostedService<LiveSubscriber>();
        }
        else
        {
            services.AddSingleton<ILiveBus, LocalLiveBus>();
        }

        // Browsers cannot set headers on a WebSocket: the hub's token comes in the query string (access_token).
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var previous = options.Events.OnMessageReceived;
            options.Events.OnMessageReceived = async context =>
            {
                await previous(context);
                if (context.Token is null
                    && context.HttpContext.Request.Path.StartsWithSegments(LiveHubMethods.Path)
                    && context.Request.Query["access_token"].ToString() is { Length: > 0 } token)
                    context.Token = token;
            };
        });
        return builder;
    }

    /// <summary>
    /// Maps the hub. Clients connect with WebSockets and skip negotiation, so a connection is one request to one replica
    /// and the gateway needs no sticky sessions. The connection closes when its token expires; the client reconnects
    /// with a fresh one and subscribes again.
    /// </summary>
    public static IEndpointRouteBuilder MapPlatformLive(this IEndpointRouteBuilder app)
    {
        app.MapHub<LiveHub>(LiveHubMethods.Path, options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.CloseOnAuthenticationExpiration = true;
        });
        return app;
    }
}

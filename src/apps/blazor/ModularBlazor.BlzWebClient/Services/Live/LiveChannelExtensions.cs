using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.ModularBlazor.Abstractions.Live;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services.Live;

public static class LiveChannelExtensions
{
    /// <summary><see cref="ILiveChannel"/> for modules, on the gateway's live hub.</summary>
    public static IServiceCollection AddLiveChannel(this IServiceCollection services, string gatewayBaseUrl)
    {
        var gateway = new Uri(gatewayBaseUrl.EndsWith('/') ? gatewayBaseUrl : gatewayBaseUrl + "/");
        // Scoped like the token provider it uses (WebAssembly has one scope for the whole app).
        services.AddScoped(sp => new LiveChannel(
            new HubLiveTransport(gateway, AccessToken(sp.GetRequiredService<IAccessTokenProvider>())),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LiveChannel>>()));
        services.AddScoped<ILiveChannel>(sp => sp.GetRequiredService<LiveChannel>());
        return services;
    }

    private static Func<Task<string?>> AccessToken(IAccessTokenProvider tokens) => async () =>
        (await tokens.RequestAccessToken()).TryGetToken(out var token) ? token.Value : null;

    /// <summary>Connects while the user is signed in: now if they are, and on every sign-in and sign-out after that.</summary>
    public static async Task StartLiveChannelAsync(this IServiceProvider services)
    {
        var channel = services.GetRequiredService<LiveChannel>();
        var authentication = services.GetRequiredService<AuthenticationStateProvider>();

        async Task FollowAsync(Task<AuthenticationState> state)
        {
            try
            {
                if ((await state).User.Identity?.IsAuthenticated == true) await channel.StartAsync();
                else await channel.StopAsync();
            }
            catch (Exception) { /* the channel logs its own failures; signing in must never fail on it */ }
        }

        authentication.AuthenticationStateChanged += state => _ = FollowAsync(state);
        await FollowAsync(authentication.GetAuthenticationStateAsync());
    }
}

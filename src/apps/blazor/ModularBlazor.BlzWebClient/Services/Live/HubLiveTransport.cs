using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using soft.square.net.ModularBlazor.Abstractions.Live;

namespace soft.square.net.ModularBlazor.BlzWebClient.Services.Live;

/// <summary>
/// The live hub through the gateway (<c>{WebApi:BaseUrl}hubs/live</c>): WebSockets only, negotiation skipped (one request
/// to one replica, no sticky sessions), the access token in the query string because browsers cannot set headers on a
/// WebSocket. The tenant is the token's. Reconnecting is <see cref="LiveChannel"/>'s job, not SignalR's.
/// </summary>
public sealed class HubLiveTransport : ILiveTransport
{
    private readonly HubConnection _hub;

    /// <param name="accessToken">The user's current access token (null: none, the hub refuses the connection).</param>
    public HubLiveTransport(Uri gateway, Func<Task<string?>> accessToken)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(gateway, "hubs/live"), options =>
            {
                options.SkipNegotiation = true;
                options.Transports = HttpTransportType.WebSockets;
                options.AccessTokenProvider = accessToken;
            })
            .Build();
        _hub.Closed += error => Closed?.Invoke(error) ?? Task.CompletedTask;
        _hub.On<LiveMessage>("Message", message => Received?.Invoke(message) ?? Task.CompletedTask);
        _hub.On<string>("Revoked", topic => Revoked?.Invoke(topic) ?? Task.CompletedTask);
    }

    public Func<Exception?, Task>? Closed { get; set; }
    public Func<LiveMessage, Task>? Received { get; set; }
    public Func<string, Task>? Revoked { get; set; }

    public Task StartAsync(CancellationToken ct) =>
        _hub.State == HubConnectionState.Disconnected ? _hub.StartAsync(ct) : Task.CompletedTask;

    public Task StopAsync(CancellationToken ct) => _hub.StopAsync(ct);

    public Task<LiveJoinResult> SubscribeAsync(string topic, CancellationToken ct) =>
        _hub.InvokeAsync<LiveJoinResult>("Subscribe", topic, ct);

    public Task UnsubscribeAsync(string topic, CancellationToken ct) => _hub.InvokeAsync("Unsubscribe", topic, ct);

    public ValueTask DisposeAsync() => _hub.DisposeAsync();
}

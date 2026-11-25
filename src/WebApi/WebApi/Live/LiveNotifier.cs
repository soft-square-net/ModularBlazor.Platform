using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Security;

namespace soft.square.net.WebApi.Live;

/// <summary>The scoped <see cref="ILiveNotifier"/>: the current scope's tenant, every replica through <see cref="ILiveBus"/>.</summary>
internal sealed class LiveNotifier(ITenantContext tenant, ILiveBus bus, LiveReplica replica, TimeProvider clock) : ILiveNotifier
{
    public Task PublishAsync(string topic, string type, JsonElement? payload = null, long? sequence = null, CancellationToken ct = default)
    {
        if (!LiveTopicAuthorizer.TryParse(topic, out _, out _)) throw new ArgumentException($"'{topic}' is not a live topic.", nameof(topic));
        return SendAsync([topic], type, payload, sequence, ct);
    }

    public Task PublishToUnitAsync(string unitPath, string type, JsonElement? payload = null, long? sequence = null, CancellationToken ct = default)
    {
        var topics = LiveTopics.UnitAndAncestors(unitPath);
        if (topics.Count == 0) throw new ArgumentException("The unit path names no unit.", nameof(unitPath));
        return SendAsync(topics, type, payload, sequence, ct);
    }

    private Task SendAsync(IReadOnlyList<string> topics, string type, JsonElement? payload, long? sequence, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        // Clone: the element may belong to a JsonDocument the caller disposes before the broker publish serializes it.
        var message = new LiveMessage(tenant.Required.Id, topics, type, payload?.Clone(), sequence, clock.GetUtcNow(), replica.Id);
        return bus.PublishAsync(message, ct);
    }
}

/// <summary>
/// Checks the subscriptions of users again when their grants change on this replica (every replica hears about every
/// grant change), and drops the topics they may no longer receive: the client gets <see cref="LiveHubMethods.Revoked"/>.
/// For example, the end of a shift (check-out) ends the live feed of the desk's units.
/// </summary>
internal sealed class LiveRevalidator(GrantCache grants, LiveConnections connections, LiveTopicAuthorizer authorizer,
    IHubContext<LiveHub> hub, ILogger<LiveRevalidator> log) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _running = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        grants.Invalidated += OnInvalidated;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        grants.Invalidated -= OnInvalidated;
        await _stopping.CancelAsync();
        Task[] running;
        lock (_running) running = [.. _running];
        try { await Task.WhenAll(running).WaitAsync(cancellationToken); }
        catch (Exception e) when (e is OperationCanceledException or TimeoutException) { }
    }

    /// <summary>The checks of the latest grant change (tests wait on it).</summary>
    internal Task Pending
    {
        get { lock (_running) return Task.WhenAll([.. _running]); }
    }

    private void OnInvalidated(string tenantId, IReadOnlyCollection<string>? userIds)
    {
        var affected = connections.Of(tenantId, userIds);
        if (affected.Count == 0) return;
        var task = Task.Run(() => RecheckAsync(affected, _stopping.Token));
        lock (_running)
        {
            _running.RemoveAll(t => t.IsCompleted);
            _running.Add(task);
        }
    }

    internal async Task RecheckAsync(IReadOnlyList<LiveConnection> affected, CancellationToken ct)
    {
        foreach (var connection in affected)
        foreach (var topic in connection.Topics.Keys.ToArray())
        {
            try
            {
                var result = await authorizer.AuthorizeAsync(connection.Tenant, connection.User, topic, ct);
                if (result.Ok || !connection.Topics.TryRemove(topic, out _)) continue;
                await hub.Groups.RemoveFromGroupAsync(connection.Id, LiveGroups.Of(connection.Tenant.Id, topic), ct);
                await hub.Clients.Client(connection.Id).SendAsync(LiveHubMethods.Revoked, topic, ct);
                log.LogInformation("Live topic {Topic} of {User} revoked after a grant change", topic, connection.UserId);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Could not check live topic {Topic} of {User} again", topic, connection.UserId);
            }
        }
    }
}

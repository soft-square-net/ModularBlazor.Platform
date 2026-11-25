using StackExchange.Redis;
using soft.square.net.WebApi.Data;

namespace soft.square.net.WebApi.Tenancy.Seeds;

/// <summary>
/// A cross-replica lock taken without waiting: the replica that gets it leads this cycle, the others skip it.
/// Redis when the host has one, otherwise a session lock on the tenant store's database.
/// </summary>
internal interface ILeaderLock
{
    /// <summary>The lock, held until disposed; null when another replica holds it.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan lease, CancellationToken ct);
}

/// <summary>A session-level lock on the tenant store's server (released on dispose, or when the connection drops).</summary>
internal sealed class DatabaseLeaderLock(TenantDatabases databases) : ILeaderLock
{
    public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan lease, CancellationToken ct)
    {
        var store = databases.Store;
        return store.Adapter.TryLockAsync(store.ConnectionString, key, ct);
    }
}

/// <summary>
/// <c>SET key token NX PX lease</c>: the lease bounds how long a crashed leader blocks the others. Released with a
/// compare-and-delete, so a leader whose lease ran out never deletes the next leader's lock.
/// </summary>
internal sealed class RedisLeaderLock(string connectionString, ILogger<RedisLeaderLock> log) : ILeaderLock, IAsyncDisposable
{
    private const string ReleaseScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private readonly SemaphoreSlim _connect = new(1, 1);
    private ConnectionMultiplexer? _redis;

    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan lease, CancellationToken ct)
    {
        var db = (await ConnectAsync(ct)).GetDatabase();
        var token = Guid.NewGuid().ToString("n");
        if (!await db.StringSetAsync(key, token, lease, When.NotExists)) return null;
        return new Held(db, key, token, log);
    }

    private async Task<ConnectionMultiplexer> ConnectAsync(CancellationToken ct)
    {
        if (_redis is { } existing) return existing;
        await _connect.WaitAsync(ct);
        try
        {
            if (_redis is null)
            {
                var options = ConfigurationOptions.Parse(connectionString);
                options.AbortOnConnectFail = false;
                _redis = await ConnectionMultiplexer.ConnectAsync(options);
            }
            return _redis;
        }
        finally { _connect.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_redis is not null) await _redis.DisposeAsync();
    }

    private sealed class Held(IDatabase db, string key, string token, ILogger log) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await db.ScriptEvaluateAsync(ReleaseScript, [key], [token]); }
            catch (RedisException e) { log.LogWarning(e, "Could not release the lock {Key}; it expires with its lease", key); }
        }
    }
}

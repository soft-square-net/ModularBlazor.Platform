using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace soft.square.net.WebApi.Modules.Core.ClientModules;

/// <summary>
/// Development only: watches the <see cref="ClientModulesOptions.Local"/> build outputs and installs a client module
/// again as soon as it is rebuilt, then tells the open browsers (<see cref="LocalClientModuleChanges"/>, served as
/// Server-Sent Events on <c>GET /api/client-modules/dev/events</c>), which reload it in place.
/// <para>
/// A Debug build made with the Module SDK writes <see cref="StampFile"/> into its output folder last, so the watcher
/// reacts to that file and never reads half a build; without it (an older SDK) the files must be quiet for a second.
/// </para>
/// </summary>
internal sealed class LocalClientModuleWatcher(ClientModuleCatalog catalog, LocalClientModuleChanges changes,
    ILogger<LocalClientModuleWatcher> log) : BackgroundService
{
    /// <summary>Written last by every Debug build of a module (Module SDK, target WriteModuleBuildStamp). Empty.</summary>
    public const string StampFile = ".module-built";

    private static readonly TimeSpan QuietWithStamp = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan QuietWithoutStamp = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(1);
    private const int MaxAttempts = 5;

    // One entry: every local module is installed by one SeedLocalAsync (it skips the unchanged ones, orders dependencies).
    private readonly ConcurrentDictionary<string, (DateTime Due, int Attempts)> _pending = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var folders = catalog.LocalPaths()
            .Select(p => Directory.Exists(p) ? p : Path.GetDirectoryName(p))
            .Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (folders.Count == 0) return;

        var watchers = new List<FileSystemWatcher>();
        try
        {
            foreach (var folder in folders!)
            {
                var watcher = new FileSystemWatcher(folder!)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024
                };
                watcher.Changed += (_, e) => OnChange(folder!, e.Name);
                watcher.Created += (_, e) => OnChange(folder!, e.Name);
                watcher.Renamed += (_, e) => OnChange(folder!, e.Name);
                watcher.Error += (_, _) => Schedule(QuietWithoutStamp);
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            log.LogInformation("Watching {Count} local client module folder(s): a rebuild reloads the module in open browsers", watchers.Count);

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!_pending.TryGetValue("", out var pending) || pending.Due > DateTime.UtcNow) continue;
                if (!_pending.TryRemove(new("", pending))) continue;
                await SeedAsync(pending.Attempts, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            foreach (var watcher in watchers) watcher.Dispose();
        }
    }

    private void OnChange(string folder, string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (Path.GetFileName(name).Equals(StampFile, StringComparison.OrdinalIgnoreCase)) Schedule(QuietWithStamp);
        else if (!File.Exists(Path.Combine(folder, StampFile))) Schedule(QuietWithoutStamp);
    }

    private void Schedule(TimeSpan quiet, int attempts = 0) => _pending[""] = (DateTime.UtcNow + quiet, attempts);

    private async Task SeedAsync(int attempts, CancellationToken ct)
    {
        try
        {
            var installed = await catalog.SeedLocalAsync(ct);
            if (installed.Count == 0) return;
            changes.Publish(installed);
            log.LogInformation("Local client module(s) {Modules} rebuilt: open browsers reload them", string.Join(", ", installed));
        }
        catch (IOException e) when (attempts + 1 < MaxAttempts)
        {
            log.LogDebug(e, "A local client module is still being written; retrying");
            Schedule(RetryAfter, attempts + 1);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Reinstalling the local client modules failed");
        }
    }
}

/// <summary>
/// Development only: the local client modules installed again since the WebApi started, as a stream the browsers wait on
/// (<c>GET /api/client-modules/dev/events</c>). One process only: other replicas' browsers are not told.
/// </summary>
internal sealed class LocalClientModuleChanges
{
    private readonly Lock _lock = new();
    private TaskCompletionSource<string[]> _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Publish(IReadOnlyList<string> ids)
    {
        TaskCompletionSource<string[]> current;
        lock (_lock)
        {
            current = _next;
            _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        current.SetResult(ids.ToArray());
    }

    /// <summary>The ids of the next change.</summary>
    public Task<string[]> NextAsync(CancellationToken ct)
    {
        lock (_lock) return _next.Task.WaitAsync(ct);
    }
}

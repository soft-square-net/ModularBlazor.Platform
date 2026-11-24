using System.Collections.Concurrent;
using System.Diagnostics;

namespace soft.square.net.WebApi.Modularity;

/// <summary>
/// Development only: watches the <see cref="ModuleRuntimeOptions.Local"/> build output folders and swaps a module for its
/// new build as soon as it is rebuilt (<c>dotnet watch build</c>, or a build in the IDE), without restarting the WebApi.
/// <para>
/// A build of a module made with the Module SDK writes <see cref="StampFile"/> into its output folder last (Debug only),
/// so the watcher reacts to that file and never sees half a build. A folder without the stamp (an older SDK) is installed
/// once its files have been quiet for <see cref="QuietWithoutStamp"/>. A build that fails writes no stamp: nothing happens.
/// </para>
/// </summary>
internal sealed class LocalModuleWatcher(ModuleRuntime runtime, IHostEnvironment environment, ILogger<LocalModuleWatcher> log)
    : BackgroundService
{
    /// <summary>Written last by every Debug build of a module (Module SDK, target WriteModuleBuildStamp). Empty.</summary>
    public const string StampFile = ".module-built";

    private static readonly TimeSpan QuietWithStamp = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan QuietWithoutStamp = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(1);
    private const int MaxAttempts = 5;

    // Folder → when to install it (pushed back by every new event) and how many tries have failed.
    private readonly ConcurrentDictionary<string, (DateTime Due, int Attempts)> _pending = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!environment.IsDevelopment()) return;
        var folders = runtime.LocalFolders();
        if (folders.Count == 0) return;

        var watchers = new List<FileSystemWatcher>();
        try
        {
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    log.LogWarning("Local module folder {Folder} does not exist: not watched (build the module, then restart)", folder);
                    continue;
                }
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024
                };
                watcher.Changed += (_, e) => OnChange(folder, e.Name);
                watcher.Created += (_, e) => OnChange(folder, e.Name);
                watcher.Renamed += (_, e) => OnChange(folder, e.Name);
                watcher.Error += (_, e) => { log.LogWarning(e.GetException(), "Watching {Folder} overflowed; reinstalling it", folder); Schedule(folder, QuietWithoutStamp); };
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            if (watchers.Count == 0) return;
            log.LogInformation("Watching {Count} local module folder(s): a rebuild swaps the module in place", watchers.Count);

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = DateTime.UtcNow;
                foreach (var (folder, (due, attempts)) in _pending)
                {
                    if (due > now || !_pending.TryRemove(new(folder, (due, attempts)))) continue;
                    await ReinstallAsync(folder, attempts, stoppingToken);
                }
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
        var file = Path.GetFileName(name);
        if (file.Equals(StampFile, StringComparison.OrdinalIgnoreCase)) Schedule(folder, QuietWithStamp);
        // Without a stamp (older SDK), any file counts; with one, the build announces itself.
        else if (!File.Exists(Path.Combine(folder, StampFile))) Schedule(folder, QuietWithoutStamp);
    }

    private void Schedule(string folder, TimeSpan quiet, int attempts = 0) =>
        _pending[folder] = (DateTime.UtcNow + quiet, attempts);

    private async Task ReinstallAsync(string folder, int attempts, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            if (await runtime.ReinstallLocalAsync(folder, ct) is not { } reinstalled) return; // unchanged or invalid (logged)
            var (id, result) = reinstalled;
            if (result.Errors.TryGetValue(id, out var error))
                log.LogError(error, "Local module {Id} was rebuilt but could not be swapped in; the running copy stays", id);
            else
                log.LogInformation("Local module {Id} rebuilt: reloaded in {Elapsed} ms", id, watch.ElapsedMilliseconds);
        }
        catch (IOException e) when (attempts + 1 < MaxAttempts)
        {
            // The build (or an antivirus) still holds a file: try again shortly.
            log.LogDebug(e, "Local module folder {Folder} is busy; retrying", folder);
            Schedule(folder, RetryAfter, attempts + 1);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Reloading the local module in {Folder} failed", folder);
        }
    }
}

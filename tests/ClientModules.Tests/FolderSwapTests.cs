using soft.square.net.WebApi.Modules.Core.ClientModules;

namespace soft.square.net.ClientModules.Tests;

/// <summary>
/// Swapping a version folder rides out a file held for a moment (the Windows indexer or an antivirus scanner) instead
/// of aborting the install, and still fails when the error does not go away.
/// </summary>
public sealed class FolderSwapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "folder-swap-" + Guid.NewGuid().ToString("N")[..8]);

    public FolderSwapTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Retry_rides_out_transient_access_errors()
    {
        var calls = 0;
        FolderSwap.Retry(() =>
        {
            calls++;
            if (calls == 1) throw new UnauthorizedAccessException("Access to the path is denied.");
            if (calls == 2) throw new IOException("The process cannot access the file because it is being used by another process.");
        }, attempts: 5, delay: TimeSpan.Zero);

        calls.ShouldBe(3);
    }

    [Fact]
    public void Retry_gives_up_after_the_last_attempt()
    {
        var calls = 0;
        Should.Throw<UnauthorizedAccessException>(() =>
            FolderSwap.Retry(() => { calls++; throw new UnauthorizedAccessException(); }, attempts: 3, delay: TimeSpan.Zero));

        calls.ShouldBe(3);
    }

    [Fact]
    public void Retry_does_not_swallow_other_errors()
    {
        var calls = 0;
        Should.Throw<InvalidOperationException>(() =>
            FolderSwap.Retry(() => { calls++; throw new InvalidOperationException(); }, attempts: 3, delay: TimeSpan.Zero));

        calls.ShouldBe(1);
    }

    [Fact]
    public void Replace_swaps_the_staged_folder_in()
    {
        var target = Path.Combine(_root, "1.0.0");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "old.js"), "old");
        var staging = target + ".new-1";
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "new.js"), "new");

        FolderSwap.Replace(staging, target);

        Directory.Exists(staging).ShouldBeFalse();
        File.Exists(Path.Combine(target, "old.js")).ShouldBeFalse();
        File.ReadAllText(Path.Combine(target, "new.js")).ShouldBe("new");
    }

    [Fact]
    public async Task Replace_waits_for_a_file_held_open_by_a_scanner()
    {
        var target = Path.Combine(_root, "1.0.0");
        var staging = target + ".new-1";
        Directory.CreateDirectory(staging);
        var file = Path.Combine(staging, "module.js");
        File.WriteAllText(file, "content");

        // Like the indexer: holds the just-written file without sharing it, then lets go. On Windows the move fails
        // with "Access denied" while the handle is open; elsewhere it succeeds at once.
        var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(600, TestContext.Current.CancellationToken);
            await held.DisposeAsync();
        }, TestContext.Current.CancellationToken);

        try
        {
            FolderSwap.Replace(staging, target);
        }
        finally
        {
            await release;
        }

        File.ReadAllText(Path.Combine(target, "module.js")).ShouldBe("content");
    }
}

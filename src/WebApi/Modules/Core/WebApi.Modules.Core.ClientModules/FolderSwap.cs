namespace soft.square.net.WebApi.Modules.Core.ClientModules;

/// <summary>
/// Replaces a folder with a staged one. On Windows a file that was just written can be held open for a moment by the
/// search indexer or an antivirus scanner, and moving or deleting its folder then fails with "Access denied" (or a
/// sharing violation). Those failures are transient, so each step is retried for a few seconds before giving up.
/// </summary>
internal static class FolderSwap
{
    /// <summary>How many times a step is tried before its error is thrown.</summary>
    internal const int Attempts = 20;

    /// <summary>The pause between attempts: 20 × 250 ms rides out a scanner for about 5 s.</summary>
    internal static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(250);

    /// <summary>Deletes <paramref name="target"/> if it exists, then moves <paramref name="staging"/> to it.</summary>
    public static void Replace(string staging, string target)
    {
        Retry(() => { if (Directory.Exists(target)) Directory.Delete(target, recursive: true); });
        Retry(() => Directory.Move(staging, target));
    }

    /// <summary>Deletes a folder (and everything in it) if it exists, retrying while a file in it is held.</summary>
    public static void Delete(string folder) =>
        Retry(() => { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); });

    /// <summary>Runs <paramref name="step"/>, retrying on <see cref="IOException"/> and <see cref="UnauthorizedAccessException"/>.</summary>
    internal static void Retry(Action step, int attempts = Attempts, TimeSpan? delay = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                step();
                return;
            }
            catch (Exception e) when (attempt < attempts && e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(delay ?? Delay);
            }
        }
    }
}

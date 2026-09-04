using System.Collections.Concurrent;

namespace PowerX.Core.Diagnostics;

public sealed record FolderEntry(string Path, string Name, bool IsDirectory, long SizeBytes, long FileCount)
{
    /// <summary>Placeholder shown while a folder is still being measured.</summary>
    public bool Pending => IsDirectory && SizeBytes < 0;
}

/// <summary>
/// Answers "where did my disk space go" — sizes the immediate children of a folder (each
/// sub-folder measured recursively) so you can drill down to the big ones. Read-only. Skips
/// reparse points (junctions / symlinks) so nothing is followed twice or forever, and ignores
/// paths it is not allowed to read.
///
/// Results are reported one child at a time as each finishes, so a scan of a whole drive shows
/// the folders it has already measured instead of a frozen screen.
/// </summary>
public static class FolderSizer
{
    private static readonly EnumerationOptions Recurse = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static readonly EnumerationOptions TopLevel = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>Sensible starting points: the current user's profile first (that is where most
    /// personal clutter lives), then every fixed / removable drive.</summary>
    public static IReadOnlyList<string> Roots()
    {
        var roots = new List<string>();
        try
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Directory.Exists(profile)) roots.Add(profile);
        }
        catch { }
        foreach (var d in DriveInfo.GetDrives())
        {
            try { if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable) roots.Add(d.RootDirectory.FullName); }
            catch { }
        }
        return roots;
    }

    /// <summary>The immediate child folders of <paramref name="path"/>, so the caller can draw the
    /// rows before any sizing has happened.</summary>
    public static IReadOnlyList<string> ChildDirectories(string path)
    {
        try { return Directory.GetDirectories(path, "*", TopLevel); }
        catch (Exception) { return []; }
    }

    /// <summary>
    /// Size the children of <paramref name="path"/>. Loose files are reported immediately; each
    /// sub-folder is measured recursively (in parallel) and reported through <paramref name="onEntry"/>
    /// the moment it finishes. <paramref name="onProgress"/> gets (folders done, folders total).
    /// </summary>
    public static async Task ScanAsync(
        string path,
        IProgress<FolderEntry>? onEntry,
        IProgress<(int Done, int Total)>? onProgress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(path)) return;

        // Loose files first — instant, and often a big page/hiberfile is the answer.
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", TopLevel))
            {
                ct.ThrowIfCancellationRequested();
                long len;
                try { len = file.Length; } catch { continue; }
                onEntry?.Report(new FolderEntry(file.FullName, file.Name, false, len, 1));
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested) { }

        string[] dirs = ChildDirectories(path).ToArray();
        int total = dirs.Length, done = 0;
        onProgress?.Report((0, total));

        await Parallel.ForEachAsync(dirs,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Min(6, Environment.ProcessorCount) },
            (dir, token) =>
            {
                var (size, count) = Measure(dir, token);
                onEntry?.Report(new FolderEntry(dir, System.IO.Path.GetFileName(dir), true, size, count));
                onProgress?.Report((Interlocked.Increment(ref done), total));
                return ValueTask.CompletedTask;
            });
    }

    /// <summary>One-shot variant for the CLI: runs the whole scan and returns the list, largest first.</summary>
    public static async Task<IReadOnlyList<FolderEntry>> ScanAsync(string path, CancellationToken ct = default)
    {
        var bag = new ConcurrentBag<FolderEntry>();
        await ScanAsync(path, new Progress<FolderEntry>(bag.Add), null, ct);
        // Progress<T> marshals asynchronously; give the last callbacks a beat to land.
        await Task.Delay(30, ct);
        return bag.OrderByDescending(e => e.SizeBytes).ToList();
    }

    private static (long Size, long Files) Measure(string dir, CancellationToken ct)
    {
        long total = 0, files = 0;
        int tick = 0;
        try
        {
            foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*", Recurse))
            {
                if ((++tick & 0x1FFF) == 0) ct.ThrowIfCancellationRequested();
                try { total += file.Length; files++; }
                catch { /* vanished or denied mid-walk */ }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* denied at the top of this subtree */ }
        return (total, files);
    }
}

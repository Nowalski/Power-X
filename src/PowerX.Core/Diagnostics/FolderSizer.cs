using System.Collections.Concurrent;

namespace PowerX.Core.Diagnostics;

public sealed record FolderEntry(string Path, string Name, bool IsDirectory, long SizeBytes, long FileCount);

/// <summary>
/// Answers "where did my disk space go" — sizes the immediate children of a folder (each
/// sub-folder measured recursively) so you can drill down to the big ones. Read-only. Skips
/// reparse points (junctions / symlinks) so nothing is followed twice or forever, and ignores
/// paths it is not allowed to read.
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

    /// <summary>Sensible starting points: every fixed/removable drive plus the current user's profile.</summary>
    public static IReadOnlyList<string> Roots()
    {
        var roots = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try { if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable) roots.Add(d.RootDirectory.FullName); }
            catch { }
        }
        try
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Directory.Exists(profile)) roots.Add(profile);
        }
        catch { }
        return roots;
    }

    /// <summary>
    /// Size the immediate children of <paramref name="path"/>. Directories are measured recursively
    /// (in parallel); loose files in the folder are reported individually. Newest-first by size.
    /// </summary>
    public static async Task<IReadOnlyList<FolderEntry>> ScanAsync(
        string path, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(path)) return [];

        var result = new ConcurrentBag<FolderEntry>();

        string[] dirs;
        try { dirs = Directory.GetDirectories(path, "*", TopLevel); }
        catch (Exception) { dirs = []; }

        await Parallel.ForEachAsync(dirs,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount) },
            (dir, token) =>
            {
                progress?.Report(System.IO.Path.GetFileName(dir));
                var (size, count) = Measure(dir, token);
                result.Add(new FolderEntry(dir, System.IO.Path.GetFileName(dir), true, size, count));
                return ValueTask.CompletedTask;
            });

        try
        {
            // DirectoryInfo.EnumerateFiles hands back FileInfo objects already populated from the
            // directory scan, so .Length costs no extra syscall (unlike Directory.EnumerateFiles + new FileInfo).
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", TopLevel))
            {
                ct.ThrowIfCancellationRequested();
                long len;
                try { len = file.Length; } catch { continue; }
                result.Add(new FolderEntry(file.FullName, file.Name, false, len, 1));
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested) { }

        return result.OrderByDescending(e => e.SizeBytes).ToList();
    }

    private static (long Size, long Files) Measure(string dir, CancellationToken ct)
    {
        long total = 0, files = 0;
        int tick = 0;
        try
        {
            foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*", Recurse))
            {
                if ((++tick & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
                try { total += file.Length; files++; }
                catch { /* vanished or denied mid-walk */ }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* denied at the top of this subtree */ }
        return (total, files);
    }
}

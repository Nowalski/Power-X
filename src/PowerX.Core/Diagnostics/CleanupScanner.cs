using System.Runtime.InteropServices;

namespace PowerX.Core.Diagnostics;

public sealed record CleanupTarget
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public bool RecommendedDefault { get; init; }

    /// <summary>Special-cased: emptied via the shell API rather than file deletion.</summary>
    public bool IsRecycleBin { get; init; }

    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
}

/// <summary>
/// Transparent disk cleanup: enumerates well-known cache/temp locations, sizes them, and
/// deletes only what the user picked. No Prefetch deletion, no aggressive "optimizations".
/// </summary>
public static partial class CleanupScanner
{
    public static IReadOnlyList<CleanupTarget> BuildTargets()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);

        return
        [
            new CleanupTarget
            {
                Id = "recycle-bin", Name = "Recycle Bin", RecommendedDefault = false, IsRecycleBin = true,
                Description = "Everything currently in the Recycle Bin, for all drives.",
                Paths = [],
            },
            new CleanupTarget
            {
                Id = "user-temp", Name = "Temporary files (your account)", RecommendedDefault = true,
                Description = "The per-user %TEMP% folder. Anything an app still needs is skipped (locked).",
                Paths = [Path.GetTempPath()],
            },
            new CleanupTarget
            {
                Id = "windows-temp", Name = "Temporary files (Windows)", RecommendedDefault = true,
                Description = "C:\\Windows\\Temp, shared scratch space.",
                Paths = [Path.Combine(win, "Temp")],
            },
            new CleanupTarget
            {
                Id = "wu-cache", Name = "Windows Update download cache", RecommendedDefault = false,
                Description = "Already-installed update packages under SoftwareDistribution\\Download. Safe to clear; Windows re-downloads if needed.",
                Paths = [Path.Combine(win, "SoftwareDistribution", "Download")],
            },
            new CleanupTarget
            {
                Id = "delivery-opt", Name = "Delivery Optimization cache", RecommendedDefault = false,
                Description = "Peer-to-peer update cache under Windows\\SoftwareDistribution\\DeliveryOptimization.",
                Paths = [Path.Combine(win, "SoftwareDistribution", "DeliveryOptimization")],
            },
            new CleanupTarget
            {
                Id = "thumb-cache", Name = "Thumbnail cache", RecommendedDefault = false,
                Description = "Explorer's thumbnail database. Rebuilds automatically (first browse of a folder is slower).",
                Paths = [Path.Combine(local, "Microsoft", "Windows", "Explorer")],
            },
            new CleanupTarget
            {
                Id = "crash-dumps", Name = "Crash dumps & error reports", RecommendedDefault = true,
                Description = "WER queued/archived reports and local minidumps.",
                Paths =
                [
                    Path.Combine(local, "CrashDumps"),
                    Path.Combine(local, "Microsoft", "Windows", "WER"),
                    Path.Combine(win, "Minidump"),
                ],
            },
            new CleanupTarget
            {
                Id = "dx-shader", Name = "DirectX / GPU shader cache", RecommendedDefault = false,
                Description = "Compiled shader caches. Rebuild automatically; first launch of a game/app is slower.",
                Paths =
                [
                    Path.Combine(local, "D3DSCache"),
                    Path.Combine(local, "NVIDIA", "DXCache"),
                    Path.Combine(local, "AMD", "DxCache"),
                ],
            },
            new CleanupTarget
            {
                Id = "setup-logs", Name = "Windows setup & servicing logs", RecommendedDefault = false,
                Description = "CBS / DISM / Panther logs left over from updates and feature installs.",
                Paths =
                [
                    Path.Combine(win, "Logs", "CBS"),
                    Path.Combine(win, "Logs", "DISM"),
                    Path.Combine(win, "Panther"),
                    Path.Combine(win, "SoftwareDistribution", "DataStore", "Logs"),
                ],
            },
            new CleanupTarget
            {
                Id = "kernel-dumps", Name = "Kernel & live dumps", RecommendedDefault = true,
                Description = "MEMORY.DMP and LiveKernelReports. Full and kernel crash dumps, often hundreds of MB each.",
                Paths =
                [
                    Path.Combine(win, "LiveKernelReports"),
                    Path.Combine(local, "..", "SystemTemp"),
                ],
            },
            new CleanupTarget
            {
                Id = "inet-cache", Name = "Legacy internet cache", RecommendedDefault = false,
                Description = "The old WinINet / Internet Explorer cache still used by some apps and installers.",
                Paths = [Path.Combine(local, "Microsoft", "Windows", "INetCache")],
            },
            new CleanupTarget
            {
                Id = "edge-cache", Name = "Microsoft Edge cache", RecommendedDefault = false,
                Description = "Edge's browser cache (not history, cookies or passwords). Close Edge first or locked files are skipped.",
                Paths =
                [
                    Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"),
                    Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Code Cache"),
                ],
            },
            new CleanupTarget
            {
                Id = "chrome-cache", Name = "Google Chrome cache", RecommendedDefault = false,
                Description = "Chrome's browser cache (not history, cookies or passwords). Close Chrome first.",
                Paths =
                [
                    Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"),
                    Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Code Cache"),
                ],
            },
        ];
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBinW(string? rootPath, ref SHQUERYRBINFO info);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinW(nint hwnd, string? rootPath, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    private static readonly EnumerationOptions Recurse = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,          // don't abort the whole walk on one denied folder
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static void Measure(CleanupTarget target)
    {
        if (target.IsRecycleBin)
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            if (SHQueryRecycleBinW(null, ref info) == 0)
            {
                target.SizeBytes = info.i64Size;
                target.FileCount = (int)Math.Min(int.MaxValue, info.i64NumItems);
            }
            return;
        }

        long size = 0;
        int count = 0;
        foreach (var root in target.Paths)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", Recurse))
                {
                    try { size += new FileInfo(file).Length; count++; }
                    catch (Exception) { /* locked / gone between listing and stat */ }
                }
            }
            catch (Exception) { /* root itself became inaccessible */ }
        }
        target.SizeBytes = size;
        target.FileCount = count;
    }

    /// <summary>
    /// Delete the target's contents. Returns (freedBytes, deletedFiles, failedFiles).
    /// <paramref name="progress"/> receives the running freed-bytes total as it goes.
    /// </summary>
    public static (long Freed, int Deleted, int Failed) Clean(
        CleanupTarget target, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        if (target.IsRecycleBin)
        {
            long before = target.SizeBytes;
            const uint noConfirm = 0x1, noProgress = 0x2, noSound = 0x4;
            int hr = SHEmptyRecycleBinW(0, null, noConfirm | noProgress | noSound);
            progress?.Report(before);
            return hr is 0 or unchecked((int)0x8000FFFF)
                ? (before, target.FileCount, 0)
                : (0, 0, target.FileCount);
        }

        long freed = 0;
        int deleted = 0, failed = 0;
        long sinceReport = 0;
        foreach (var root in target.Paths)
        {
            if (ct.IsCancellationRequested) break;
            if (!Directory.Exists(root)) continue;
            foreach (var file in SafeEnumerate(root))
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    long len = new FileInfo(file).Length;
                    File.Delete(file);
                    freed += len;
                    deleted++;
                    sinceReport += len;
                    if (progress is not null && sinceReport > 8 * 1024 * 1024)
                    {
                        progress.Report(freed);
                        sinceReport = 0;
                    }
                }
                catch (Exception) { failed++; }
            }
            // remove now-empty subdirectories (never the root itself)
            foreach (var dir in SafeEnumerateDirs(root).OrderByDescending(d => d.Length))
            {
                try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
                catch (Exception) { /* ignore */ }
            }
        }
        progress?.Report(freed);
        return (freed, deleted, failed);
    }

    private static IEnumerable<string> SafeEnumerate(string root)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*", Recurse); }
        catch (Exception) { yield break; }
        foreach (var f in files) yield return f;
    }

    private static IEnumerable<string> SafeEnumerateDirs(string root)
    {
        try { return Directory.EnumerateDirectories(root, "*", Recurse); }
        catch (Exception) { return []; }
    }
}

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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
                    Path.Combine(win, "MEMORY.DMP"),
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
            if (File.Exists(root))
            {
                // A single well-known file (e.g. MEMORY.DMP) rather than a folder.
                try { size += new FileInfo(root).Length; count++; }
                catch (Exception) { /* locked / gone */ }
                continue;
            }
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

            if (File.Exists(root))
            {
                // A single well-known file (e.g. MEMORY.DMP): it must still be where we expect it.
                string? parent = Path.GetDirectoryName(root);
                string? parentFinal = parent is null ? null : FinalDirectoryPath(parent);
                if (parentFinal is not null && TryDeleteContained(root, isDirectory: false, parentFinal, out long one))
                {
                    freed += one; deleted++;
                }
                else failed++;
                continue;
            }

            if (!Directory.Exists(root)) continue;

            // Resolve the root once. Everything deleted below must still resolve to a path inside
            // it at the moment it is deleted (see TryDeleteContained).
            string? rootFinal = FinalDirectoryPath(root);
            if (rootFinal is null) continue;

            foreach (var file in SafeEnumerate(root))
            {
                if (ct.IsCancellationRequested) break;
                if (TryDeleteContained(file, isDirectory: false, rootFinal, out long len))
                {
                    freed += len;
                    deleted++;
                    sinceReport += len;
                    if (progress is not null && sinceReport > 8 * 1024 * 1024)
                    {
                        progress.Report(freed);
                        sinceReport = 0;
                    }
                }
                else failed++;
            }
            // remove now-empty subdirectories (never the root itself). Deleting a directory by
            // handle fails on its own when it is not empty, so there is no emptiness race.
            foreach (var dir in SafeEnumerateDirs(root).OrderByDescending(d => d.Length))
                TryDeleteContained(dir, isDirectory: true, rootFinal, out _);
        }
        progress?.Report(freed);
        return (freed, deleted, failed);
    }

    // ---------------------------------------------------------------- containment-checked delete
    //
    // PowerX runs elevated, and some cleanup roots (C:\Windows\Temp, the user's %TEMP%) are
    // writable by unprivileged processes. A path-based File.Delete is then open to a classic race:
    // after the folder walk, a sub-folder is swapped for a junction pointing somewhere else (e.g.
    // System32), and the elevated delete follows it. So every delete here opens the item first —
    // with FILE_FLAG_OPEN_REPARSE_POINT so a symlink is deleted as a link, never followed — asks
    // Windows where that handle really lives, and only deletes (through the same handle) when the
    // final path is still inside the cleanup root.

    private const uint DELETE = 0x00010000;
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_ALL = 0x1 | 0x2 | 0x4;    // read | write | delete
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;   // required to open a directory
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const int FileDispositionInfo = 4;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string fileName, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle file, char* buffer, uint length, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref byte info, uint size);

    /// <summary>The fully resolved path of an existing directory, with a trailing separator.</summary>
    private static string? FinalDirectoryPath(string dir)
    {
        using var h = CreateFile(dir, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (h.IsInvalid) return null;
        string? p = FinalPath(h);
        return p is null ? null : WithTrailingSeparator(p);
    }

    private static unsafe string? FinalPath(SafeFileHandle h)
    {
        char[] buf = new char[1024];
        while (true)
        {
            uint n;
            fixed (char* b = buf) n = GetFinalPathNameByHandle(h, b, (uint)buf.Length, 0);
            if (n == 0) return null;
            if (n < buf.Length) return new string(buf, 0, (int)n);
            if (n > 32_768) return null;
            buf = new char[n + 1];
        }
    }

    internal static string WithTrailingSeparator(string p) =>
        p.EndsWith('\\') ? p : p + "\\";

    /// <summary>True when <paramref name="finalPath"/> lies strictly inside <paramref name="rootFinalWithSeparator"/>.</summary>
    internal static bool IsContained(string finalPath, string rootFinalWithSeparator) =>
        finalPath.Length > rootFinalWithSeparator.Length &&
        finalPath.StartsWith(rootFinalWithSeparator, StringComparison.OrdinalIgnoreCase);

    private static bool TryDeleteContained(string path, bool isDirectory, string rootFinal, out long length)
    {
        length = 0;
        try
        {
            uint flags = FILE_FLAG_OPEN_REPARSE_POINT | (isDirectory ? FILE_FLAG_BACKUP_SEMANTICS : 0);
            using var h = CreateFile(path, DELETE | FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, 0, OPEN_EXISTING, flags, 0);
            if (h.IsInvalid) return false;

            string? final = FinalPath(h);
            if (final is null || !IsContained(final, rootFinal)) return false;

            if (!isDirectory)
            {
                try { length = RandomAccess.GetLength(h); }
                catch (Exception)
                {
                    try { length = new FileInfo(path).Length; } catch (Exception) { length = 0; }
                }
            }

            byte deleteFile = 1;   // FILE_DISPOSITION_INFO { BOOLEAN DeleteFile }
            if (!SetFileInformationByHandle(h, FileDispositionInfo, ref deleteFile, 1))
            {
                length = 0;
                return false;
            }
            return true;   // the item goes away when the handle closes
        }
        catch (Exception)
        {
            length = 0;
            return false;
        }
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

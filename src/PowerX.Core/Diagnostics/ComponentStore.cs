using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace PowerX.Core.Diagnostics;

public sealed record ComponentStoreInfo
{
    public long ActualSizeBytes { get; init; }
    public long SharedWithWindowsBytes { get; init; }
    public long BackupsAndDisabledBytes { get; init; }
    public long CacheAndTempBytes { get; init; }
    public int ReclaimablePackages { get; init; }
    public bool CleanupRecommended { get; init; }
    public DateTimeOffset? LastCleanup { get; init; }
    public string? Error { get; init; }

    /// <summary>A rough upper bound on what a component cleanup could return.</summary>
    public long PotentialSavingsBytes => BackupsAndDisabledBytes + CacheAndTempBytes;
}

/// <summary>
/// Reads the WinSxS component-store size breakdown via
/// <c>DISM /Online /Cleanup-Image /AnalyzeComponentStore</c>, and can run the Microsoft-recommended
/// <c>/StartComponentCleanup</c>. It never runs <c>/ResetBase</c> (which permanently blocks
/// uninstalling installed updates). Needs administrator rights; the analyze pass can take a minute.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ComponentStore
{
    public static async Task<ComponentStoreInfo> AnalyzeAsync(CancellationToken ct = default)
    {
        try
        {
            var (code, output) = await RunDismAsync("/Online /Cleanup-Image /AnalyzeComponentStore", null, ct);
            if (code != 0 && output.Length == 0)
                return new ComponentStoreInfo { Error = $"DISM exited with code {code}." };
            return Parse(output);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ComponentStoreInfo { Error = ex.Message };
        }
    }

    /// <summary>Run <c>/StartComponentCleanup</c>, streaming DISM's progress lines. Reversible in the
    /// sense that it only removes superseded components; installed updates stay uninstallable.</summary>
    public static async Task<int> StartCleanupAsync(Action<string> onLine, CancellationToken ct = default)
    {
        try
        {
            var (code, _) = await RunDismAsync("/Online /Cleanup-Image /StartComponentCleanup", onLine, ct);
            onLine(code == 0 ? "Component cleanup finished." : $"DISM exited with code {code}.");
            return code;
        }
        catch (OperationCanceledException)
        {
            onLine("Cleanup cancelled. DISM finishes the current step before stopping.");
            return -1;
        }
        catch (Exception ex)
        {
            onLine("Could not run the cleanup: " + ex.Message);
            return -1;
        }
    }

    internal static ComponentStoreInfo Parse(string text)
    {
        long Size(string label)
        {
            var m = Regex.Match(text, Regex.Escape(label) + @"\s*:\s*([\d.,]+)\s*(KB|MB|GB|TB|B)",
                RegexOptions.IgnoreCase);
            if (!m.Success) return 0;
            if (!double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                return 0;
            return (long)(n * m.Groups[2].Value.ToUpperInvariant() switch
            {
                "TB" => 1024L * 1024 * 1024 * 1024,
                "GB" => 1024L * 1024 * 1024,
                "MB" => 1024L * 1024,
                "KB" => 1024L,
                _ => 1L,
            });
        }

        int packages = 0;
        var pm = Regex.Match(text, @"Number of Reclaimable Packages\s*:\s*(\d+)", RegexOptions.IgnoreCase);
        if (pm.Success) int.TryParse(pm.Groups[1].Value, out packages);

        bool recommended = Regex.IsMatch(text, @"Component Store Cleanup Recommended\s*:\s*Yes", RegexOptions.IgnoreCase);

        DateTimeOffset? lastCleanup = null;
        var dm = Regex.Match(text, @"Date of Last Cleanup\s*:\s*(.+)");
        if (dm.Success && DateTimeOffset.TryParse(dm.Groups[1].Value.Trim(), out var d)) lastCleanup = d;

        return new ComponentStoreInfo
        {
            ActualSizeBytes = Size("Actual Size of Component Store"),
            SharedWithWindowsBytes = Size("Shared with Windows"),
            BackupsAndDisabledBytes = Size("Backups and Disabled Features"),
            CacheAndTempBytes = Size("Cache and Temporary Data"),
            ReclaimablePackages = packages,
            CleanupRecommended = recommended,
            LastCleanup = lastCleanup,
        };
    }

    private static async Task<(int Code, string Output)> RunDismAsync(string args, Action<string>? onLine, CancellationToken ct)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo("dism.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };
        var sb = new System.Text.StringBuilder();
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            sb.AppendLine(e.Data);
            if (onLine is not null && e.Data.Trim().Length > 0) onLine(e.Data.Trim());
        };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) sb.AppendLine(e.Data); };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        return (p.ExitCode, sb.ToString());
    }
}

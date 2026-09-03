using System.Diagnostics;

namespace PowerX.Core.Diagnostics;

/// <summary>
/// Runs legitimate Windows repair / diagnostic tools and streams their output line by line.
/// The job list is a fixed, curated allow-list — this is not an arbitrary command executor.
/// </summary>
public static class CommandRunner
{
    public sealed record Step(string File, string Arguments);

    public sealed record Job(
        string Category,
        string Title,
        string Explanation,
        IReadOnlyList<Step> Steps,
        bool Destructive = false,
        string? OpenReportPath = null)
    {
        public Job(string category, string title, string explanation, string file, string arguments,
                   bool destructive = false, string? openReport = null)
            : this(category, title, explanation, new Step[] { new(file, arguments) }, destructive, openReport) { }
    }

    private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string Temp => Path.GetTempPath();

    public static IReadOnlyList<Job> Jobs { get; } =
    [
        // ---- System file & image integrity ----
        new("Integrity", "Scan & repair system files (SFC)",
            "Scans every protected Windows file and restores corrupt ones from a local cache. Run DISM RestoreHealth first if SFC can't fix something.",
            "sfc.exe", "/scannow"),
        new("Integrity", "Verify system files only (SFC, read-only)",
            "Checks protected files and reports problems without changing anything.",
            "sfc.exe", "/verifyonly"),
        new("Integrity", "Check component store health (DISM, fast)",
            "A quick flag check of the Windows image. Read-only.",
            "DISM.exe", "/Online /Cleanup-Image /CheckHealth"),
        new("Integrity", "Scan component store health (DISM)",
            "A deeper scan of the Windows component store for corruption. Read-only, a few minutes.",
            "DISM.exe", "/Online /Cleanup-Image /ScanHealth"),
        new("Integrity", "Repair Windows image (DISM RestoreHealth)",
            "Repairs the component store using Windows Update as the source. Needs internet; can take 10 to 30 minutes.",
            "DISM.exe", "/Online /Cleanup-Image /RestoreHealth"),
        new("Integrity", "Analyze component store size (DISM)",
            "Reports how much space WinSxS is using and whether a cleanup is recommended.",
            "DISM.exe", "/Online /Cleanup-Image /AnalyzeComponentStore"),
        new("Integrity", "Clean up superseded components (DISM)",
            "Removes older, superseded versions of components. Frees space; makes recent updates permanent (they can't be uninstalled afterwards).",
            "DISM.exe", "/Online /Cleanup-Image /StartComponentCleanup", destructive: true),

        // ---- Disk ----
        new("Disk", "Scan the system drive (CHKDSK, online)",
            "Checks the C: volume for file-system errors without taking it offline or making changes.",
            "chkdsk.exe", "C: /scan"),
        new("Disk", "Schedule a full check of C: on next restart",
            "Queues chkdsk /f /r for the next boot (fixes errors and recovers readable data, and can take a long time). Answers 'Yes' to the schedule prompt for you.",
            "cmd.exe", "/c echo Y| chkdsk C: /f /r /x", destructive: true),

        // ---- Network ----
        new("Network", "Flush DNS cache", "Clears cached DNS lookups. Harmless; fixes many 'site won't load' issues.",
            "ipconfig.exe", "/flushdns"),
        new("Network", "Release & renew IP address", "Drops and re-requests the DHCP lease on all adapters.",
            new Step[] { new("ipconfig.exe", "/release"), new("ipconfig.exe", "/renew") }),
        new("Network", "Reset Winsock", "Resets the Windows Sockets catalog. Fixes networking corruption; restart afterwards. Removes custom LSP/VPN hooks.",
            "netsh.exe", "winsock reset", destructive: true),
        new("Network", "Reset the TCP/IP stack", "Rewrites the core TCP/IP registry keys to defaults. Restart afterwards.",
            "netsh.exe", "int ip reset", destructive: true),
        new("Network", "Clear the ARP cache", "Forgets the learned MAC-to-IP address mappings. Harmless.",
            "arp.exe", "-d *"),
        new("Network", "Reset WinHTTP proxy", "Clears the system-wide WinHTTP proxy used by services and updates.",
            "netsh.exe", "winhttp reset proxy"),
        new("Network", "Reset Windows Firewall to defaults", "Removes all custom inbound/outbound rules and restores the shipped policy.",
            "netsh.exe", "advfirewall reset", destructive: true),

        // ---- Windows Update ----
        new("Windows Update", "Reset Windows Update components",
            "Stops the update services, clears the SoftwareDistribution and catroot2 caches, then restarts them. This is the standard fix for stuck updates.",
            new Step[]
            {
                new("net.exe", "stop wuauserv"),
                new("net.exe", "stop cryptSvc"),
                new("net.exe", "stop bits"),
                new("net.exe", "stop msiserver"),
                new("cmd.exe", $"/c ren \"{Win}\\SoftwareDistribution\" SoftwareDistribution.old"),
                new("cmd.exe", $"/c ren \"{Win}\\System32\\catroot2\" catroot2.old"),
                new("net.exe", "start wuauserv"),
                new("net.exe", "start cryptSvc"),
                new("net.exe", "start bits"),
                new("net.exe", "start msiserver"),
            }, Destructive: true),
        new("Windows Update", "Reset the Microsoft Store cache",
            "Clears the Store's local cache. Opens the Store when done. Fixes many Store download failures.",
            "wsreset.exe", ""),
        new("Windows Update", "Force a Group Policy refresh", "Re-applies machine and user policy immediately.",
            "gpupdate.exe", "/force"),

        // ---- Reports ----
        new("Reports", "Generate a power / energy report",
            "Traces power usage for 60 seconds and writes an HTML report of drivers and settings that hurt battery / idle power.",
            "powercfg.exe", $"/energy /output \"{Path.Combine(Temp, "powerx-energy.html")}\" /duration 60",
            openReport: Path.Combine(Temp, "powerx-energy.html")),
        new("Reports", "Generate a battery report",
            "Writes an HTML report of battery capacity history and recent usage (laptops).",
            "powercfg.exe", $"/batteryreport /output \"{Path.Combine(Temp, "powerx-battery.html")}\"",
            openReport: Path.Combine(Temp, "powerx-battery.html")),
        new("Reports", "Generate a system diagnostics report",
            "Runs perfmon's system diagnostics collector for 60 seconds and opens the result.",
            "perfmon.exe", "/report"),
        new("Reports", "List boot configuration (BCD)",
            "Prints the boot menu entries and settings. Read-only.",
            "bcdedit.exe", "/enum"),
        new("Reports", "Driver list",
            "Lists installed kernel drivers with their type and link date. Read-only.",
            "driverquery.exe", "/fo table /si"),
    ];

    /// <summary>Run every step of a job, streaming output. Returns the last non-zero exit code, or 0.</summary>
    public static async Task<int> RunAsync(Job job, Action<string> onLine, CancellationToken ct = default)
    {
        int result = 0;
        foreach (var step in job.Steps)
        {
            if (job.Steps.Count > 1) onLine($"\n> {step.File} {step.Arguments}");
            int code = await RunStepAsync(step, onLine, ct);
            if (code != 0) result = code;
            if (ct.IsCancellationRequested) break;
        }
        return result;
    }

    private static async Task<int> RunStepAsync(Step step, Action<string> onLine, CancellationToken ct)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(step.File, step.Arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            },
            EnableRaisingEvents = true,
        };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };

        try
        {
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync(ct);
            return p.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            onLine("cancelled");
            return -1;
        }
        catch (Exception ex)
        {
            onLine($"Could not start {step.File}: {ex.Message}");
            return -1;
        }
    }
}

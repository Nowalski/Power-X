namespace PowerX.Core.Diagnostics;

/// <summary>
/// Runs Windows' built-in "System Diagnostics" data collector and hands back the HTML report
/// it writes.
///
/// perfmon.exe /report collects exactly this data, but it is a GUI program: it opens the
/// Resource and Performance Monitor window, prints nothing to stdout, and does not exit until
/// that window is closed. Driven as a plain <see cref="CommandRunner"/> step that produced a
/// job which popped up a window, streamed nothing for a minute, and handed back no report.
///
/// The report is not a feature of that window. It is configured on the collector set itself
/// (DataManager.Enabled, ReportFileName = report.html) and produced by the Performance Logs
/// and Alerts service when the set stops. Driving the set through PLA's own COM API therefore
/// gets the same report with no window, and gives a real status to show progress from instead
/// of a silent console.
/// </summary>
public static class SystemDiagnosticsReport
{
    private const string SetName = @"System\System Diagnostics";

    /// <summary>How long to collect. perfmon /report uses a minute, as does the set's own segment limit.</summary>
    private static readonly TimeSpan CollectFor = TimeSpan.FromSeconds(60);

    /// <summary>Cap on the rules pass that turns the raw logs into report.html (about 7 s when measured).</summary>
    private static readonly TimeSpan CompileTimeout = TimeSpan.FromMinutes(3);

    // PLA_STATUS
    private const int Stopped = 0, Running = 1;

    public static async Task<CommandRunner.JobRun> RunAsync(Action<string> onLine, CancellationToken ct)
    {
        dynamic set;
        try
        {
            var type = Type.GetTypeFromProgID("Pla.DataCollectorSet");
            if (type is null)
            {
                onLine("Performance Logs and Alerts is not available on this system.");
                return new CommandRunner.JobRun(-1, null);
            }
            set = Activator.CreateInstance(type)!;
            set.Query(SetName, null);
        }
        catch (Exception ex)
        {
            onLine($"Could not open the System Diagnostics collector: {ex.Message}");
            return new CommandRunner.JobRun(-1, null);
        }

        try
        {
            if (Status(set) != Stopped)
            {
                onLine("The System Diagnostics collector is already running, so PowerX left it alone.");
                onLine("Wait for it to finish, or stop it in Performance Monitor, then run this again.");
                return new CommandRunner.JobRun(-1, null);
            }

            onLine("Starting the Windows System Diagnostics collector. No window opens; PowerX drives it directly.");
            var startedAtUtc = DateTime.UtcNow;
            set.start(true);

            var until = DateTimeOffset.UtcNow + CollectFor;
            onLine($"Collecting for {CollectFor.TotalSeconds:0} seconds. Counters, kernel trace, drivers, services and hardware inventory.");
            while (true)
            {
                var left = until - DateTimeOffset.UtcNow;
                if (left <= TimeSpan.Zero) break;
                await Task.Delay(left < TimeSpan.FromSeconds(5) ? left : TimeSpan.FromSeconds(5), ct);
                int secs = (int)Math.Round((until - DateTimeOffset.UtcNow).TotalSeconds);
                if (secs > 0 && secs % 15 == 0) onLine($"  {secs} seconds left");
            }

            onLine("Collection finished. Windows is compiling the report.");
            if (Status(set) == Running)
                try { set.Stop(true); } catch (Exception ex) { onLine($"  stop returned {ex.Message}"); }

            // The set reports Stopped only once the rules pass has produced report.xml/report.html.
            var deadline = DateTimeOffset.UtcNow + CompileTimeout;
            while (Status(set) != Stopped && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(TimeSpan.FromSeconds(2), ct);

            string folder = OutputFolder(set);
            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                onLine("The collector finished but did not report where it wrote its data.");
                return new CommandRunner.JobRun(-1, null);
            }
            onLine($"Data written to {folder}");

            // Only this run's report will do. Each run gets its own folder, so a report.html older
            // than the moment we started the set belongs to a previous run, and opening that would
            // quietly show stale data as if it were fresh.
            string report = Path.Combine(folder, "report.html");
            while (!IsFresh(report, startedAtUtc) && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(TimeSpan.FromSeconds(2), ct);

            if (!IsFresh(report, startedAtUtc))
            {
                onLine("Windows collected the data but did not produce a report for this run.");
                onLine("The raw logs are in the folder above and can be opened in Performance Monitor.");
                return new CommandRunner.JobRun(-1, null);
            }

            onLine($"Report ready: {report} ({new FileInfo(report).Length / (1024.0 * 1024.0):0.0} MB). Opening it now.");
            return new CommandRunner.JobRun(0, report);
        }
        catch (OperationCanceledException)
        {
            onLine("cancelled, stopping the collector");
            try { if (Status(set) != Stopped) set.Stop(true); } catch (Exception) { /* best effort */ }
            return new CommandRunner.JobRun(-1, null);
        }
        catch (Exception ex)
        {
            onLine($"The System Diagnostics collector failed: {ex.Message}");
            try { if (Status(set) != Stopped) set.Stop(true); } catch (Exception) { /* best effort */ }
            return new CommandRunner.JobRun(-1, null);
        }
    }

    /// <summary>
    /// PLA's properties are a snapshot taken by Query, not live values, so anything read off the
    /// object keeps describing the state it was in when Query last ran. Measured before this was
    /// added: after a full 60 s collection the object still reported Stopped and still named the
    /// *previous* run's folder, so PowerX skipped its wait for the report and then opened a report
    /// from an earlier run as if it were the new one.
    /// </summary>
    private static void Refresh(dynamic set)
    {
        try { set.Query(SetName, null); }
        catch (Exception) { /* keep the last snapshot rather than inventing a state */ }
    }

    private static int Status(dynamic set)
    {
        Refresh(set);
        try { return (int)set.Status; }
        catch (Exception) { return Stopped; }
    }

    private static string OutputFolder(dynamic set)
    {
        Refresh(set);
        try { return (string?)set.LatestOutputLocation ?? ""; }
        catch (Exception) { return ""; }
    }

    private static bool IsFresh(string report, DateTime startedAtUtc)
        => CommandRunner.IsFresh(report, startedAtUtc);
}

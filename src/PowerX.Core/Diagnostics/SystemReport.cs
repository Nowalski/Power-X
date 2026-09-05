using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using PowerX.Core.Diagnostics.Crash;
using PowerX.Core.Telemetry;
using PowerX.Core.Transactions;
using PowerX.Core.Tweaks;

namespace PowerX.Core.Diagnostics;

public sealed record ReportOptions
{
    /// <summary>Replace the user name, machine name and MAC addresses with placeholders.</summary>
    public bool Redact { get; init; } = true;
    public int ChangeHistoryCount { get; init; } = 25;
    public TimeSpan EventWindow { get; init; } = TimeSpan.FromDays(7);
    public bool IncludeCrashes { get; init; } = true;
}

/// <summary>
/// Builds a plain-text system report for support and bug reports: hardware, OS, storage,
/// the tweaks PowerX has applied, recent change history, an event-log error summary and a
/// crash summary. By default it scrubs the user name, machine name and MAC addresses.
/// Read-only. Every section is best-effort: a section that cannot be collected says so rather
/// than failing the whole report.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SystemReport
{
    public static string BuildMarkdown(ReportOptions? options = null)
    {
        var opt = options ?? new ReportOptions();
        var sb = new StringBuilder();

        sb.AppendLine("# PowerX system report");
        sb.AppendLine();
        sb.AppendLine($"Generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm} by PowerX "
                    + $"{typeof(SystemReport).Assembly.GetName().Version?.ToString(3)}.");
        if (opt.Redact)
            sb.AppendLine("User name, machine name and MAC addresses are redacted.");
        sb.AppendLine();

        // Each section below is independent read-only work (registry, WMI, event logs, WER/minidump
        // files) that touches nothing another section touches, so they run concurrently on the thread
        // pool instead of one after another and get stitched back together in a fixed order afterwards
        // — same idea as HealthCheck.ScanAsync. Measured on the dev machine (elevated): the Storage
        // and Event-log-errors sections alone are ~100ms each and used to run back to back; running
        // every section concurrently took BuildMarkdown from ~240ms to ~110ms. BuildMarkdown is itself
        // always invoked from a background thread (Task.Run) by both its callers (Settings page, CLI),
        // so blocking here on the section tasks never touches a UI thread.
        var sectionTasks = new List<Task<string>>
        {
            Task.Run(() => RenderSection("System", () => System(opt))),
            Task.Run(() => RenderSection("Hardware", () => Hardware())),
            Task.Run(() => RenderSection("Storage", () => Storage())),
            Task.Run(() => RenderSection("Applied tweaks", () => AppliedTweaks())),
            Task.Run(() => RenderSection("Recent changes", () => RecentChanges(opt.ChangeHistoryCount))),
            Task.Run(() => RenderSection("Event-log errors", () => EventErrors(opt.EventWindow))),
        };
        Task<string>? crashesTask = opt.IncludeCrashes
            ? Task.Run(() => RenderSection("Crashes", () => Crashes(opt.EventWindow)))
            : null;

        Task.WaitAll(crashesTask is null ? [.. sectionTasks] : [.. sectionTasks, crashesTask]);

        foreach (var t in sectionTasks) sb.Append(t.Result);
        if (crashesTask is not null) sb.Append(crashesTask.Result);

        var text = sb.ToString();
        return opt.Redact ? Scrub(text) : text;
    }

    private static string RenderSection(string title, Func<string> body)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        try { sb.AppendLine(body().TrimEnd()); }
        catch (Exception ex) { sb.AppendLine($"_Could not collect this section: {ex.Message}_"); }
        sb.AppendLine();
        return sb.ToString();
    }

    // -------------------------------------------------------------- sections

    private static string System(ReportOptions opt)
    {
        var i = SystemInfoProvider.Collect();
        var sb = new StringBuilder();
        sb.AppendLine($"- Edition: {i.WindowsEdition}");
        sb.AppendLine($"- Version: {i.DisplayVersion}, build {i.BuildString}, {i.Architecture}");
        if (i.InstallDate is { } d) sb.AppendLine($"- Installed: {d:yyyy-MM-dd}");
        sb.AppendLine($"- Running elevated: {(i.IsElevated ? "yes" : "no")}");
        try
        {
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            sb.AppendLine($"- Uptime: {(int)up.TotalDays}d {up.Hours}h {up.Minutes}m");
        }
        catch { /* ignore */ }
        if (!opt.Redact) sb.AppendLine($"- Machine: {i.MachineName}");
        return sb.ToString();
    }

    private static string Hardware()
    {
        var sb = new StringBuilder();
        try
        {
            var c = CpuInfo.Query();
            sb.AppendLine($"- CPU: {c.Name} ({c.PhysicalCores}C / {c.LogicalProcessors}T"
                        + (c.IsHybrid ? $", {c.PerformanceCores}P + {c.EfficiencyCores}E" : "") + ")");
            if (c.MaxClockMhz > 0) sb.AppendLine($"  - Max clock: {c.MaxClockMhz / 1000.0:0.00} GHz");
            sb.AppendLine($"  - Virtualization in firmware: {(c.VirtualizationFirmwareEnabled ? "enabled" : "disabled")}");
        }
        catch (Exception ex) { sb.AppendLine($"- CPU: unavailable ({ex.Message})"); }

        try
        {
            var m = MemoryHardware.Query();
            sb.AppendLine($"- Memory: {Bytes(m.TotalPhysicalBytes)} {m.DominantType}, "
                        + $"{m.SlotsUsed}/{m.SlotsTotal} slots, {m.EffectiveSpeedMtps} MT/s");
        }
        catch (Exception ex) { sb.AppendLine($"- Memory: unavailable ({ex.Message})"); }

        try
        {
            foreach (var g in GpuMetricsProvider.QueryAdapters())
                sb.AppendLine($"- GPU: {g.Name}"
                            + (g.DedicatedMemoryTotal > 0 ? $", {Bytes(g.DedicatedMemoryTotal)} VRAM" : "")
                            + (string.IsNullOrEmpty(g.DriverVersion) ? "" : $", driver {g.DriverVersion}"));
        }
        catch (Exception ex) { sb.AppendLine($"- GPU: unavailable ({ex.Message})"); }
        return sb.ToString();
    }

    private static string Storage()
    {
        var sb = new StringBuilder();
        foreach (var d in StorageInfo.PhysicalDisks())
        {
            sb.Append($"- {d.Name}: {Bytes(d.SizeBytes)} {d.MediaType} over {d.BusType}, health {d.Health}");
            if (d.TemperatureC is { } t) sb.Append($", {t} C");
            if (d.WearPercent is { } w) sb.Append($", {w}% endurance used");
            sb.AppendLine();
        }
        foreach (var v in StorageInfo.Volumes())
            sb.AppendLine($"  - {v.Drive} {v.FileSystem}: {Bytes(v.FreeBytes)} free of {Bytes(v.TotalBytes)} ({v.UsedPercent:0}% used)");
        return sb.Length == 0 ? "No disks reported." : sb.ToString();
    }

    private static string AppliedTweaks()
    {
        var engine = new TweakEngine(TweakCatalog.Default);
        var applied = engine.GetAllStatus()
            .Where(s => s.State == TweakState.Applied)
            .OrderBy(s => s.Definition.Category).ThenBy(s => s.Definition.Id)
            .ToList();
        if (applied.Count == 0) return "None. Every tweak is at the Windows default.";

        var log = new ChangeLog().ReadAll();
        var lastApply = log.Where(r => r.Success && r.Action == ChangeAction.Apply)
            .GroupBy(r => r.TweakId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Timestamp));

        var sb = new StringBuilder();
        foreach (var s in applied)
        {
            sb.Append($"- `{s.Definition.Id}`  {s.Definition.Name}");
            if (lastApply.TryGetValue(s.Definition.Id, out var when)) sb.Append($"  (applied {when:yyyy-MM-dd})");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string RecentChanges(int count)
    {
        var log = new ChangeLog().ReadAll();
        if (log.Count == 0) return "The change history is empty.";
        var sb = new StringBuilder();
        foreach (var r in log.OrderByDescending(r => r.Timestamp).Take(count))
        {
            string verb = r.Action == ChangeAction.Apply ? "apply" : "revert";
            string outcome = !r.Success ? "FAILED"
                : r.PreviousState == r.ResultingState ? "no change"
                : $"{r.PreviousState} to {r.ResultingState}";
            sb.AppendLine($"- {r.Timestamp:yyyy-MM-dd HH:mm}  {verb} `{r.TweakId}`  {outcome}");
        }
        return sb.ToString();
    }

    private static string EventErrors(TimeSpan window)
    {
        var since = DateTimeOffset.UtcNow - window;

        // The Application and System logs are two independent channels — reading them is two
        // unrelated EventLogReader round-trips (measured ~55ms and ~40ms on the dev machine), so
        // they run concurrently and get merged afterwards instead of one after another.
        var logTasks = new[] { "Application", "System" }
            .Select(logName => Task.Run(() => ReadOneEventLog(logName, since)))
            .ToArray();
        Task.WaitAll(logTasks);
        var results = logTasks.Select(t => t.Result).ToList();

        var counts = new Dictionary<string, (int n, DateTimeOffset last)>();
        var problems = new List<string>();
        foreach (var (logCounts, problem) in results)
        {
            foreach (var kv in logCounts)
            {
                if (counts.TryGetValue(kv.Key, out var cur))
                    counts[kv.Key] = (cur.n + kv.Value.n, kv.Value.last > cur.last ? kv.Value.last : cur.last);
                else
                    counts[kv.Key] = kv.Value;
            }
            if (problem is not null) problems.Add(problem);
        }

        var sb = new StringBuilder();
        if (counts.Count == 0)
        {
            sb.AppendLine($"No errors in the Application or System log in the last {window.TotalDays:0} days.");
        }
        else
        {
            sb.AppendLine($"Top error sources in the last {window.TotalDays:0} days:");
            foreach (var kv in counts.OrderByDescending(k => k.Value.n).Take(12))
                sb.AppendLine($"- {kv.Value.n,4}x  {kv.Key}  (last {kv.Value.last.LocalDateTime:yyyy-MM-dd HH:mm})");
        }
        foreach (var p in problems) sb.AppendLine($"- _{p}_");
        return sb.ToString();
    }

    private static (Dictionary<string, (int n, DateTimeOffset last)> Counts, string? Problem) ReadOneEventLog(
        string logName, DateTimeOffset since)
    {
        string iso = since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var counts = new Dictionary<string, (int n, DateTimeOffset last)>();
        try
        {
            var q = new EventLogQuery(logName, PathType.LogName,
                $"*[System[(Level=1 or Level=2) and TimeCreated[@SystemTime>='{iso}']]]")
            { ReverseDirection = true };
            using var reader = new EventLogReader(q);
            int seen = 0;
            for (EventRecord? e = SafeRead(reader); e is not null && seen < 4000; e = SafeRead(reader), seen++)
            {
                using (e)
                {
                    string key = $"{logName}  {e.ProviderName}  id {e.Id}";
                    var when = e.TimeCreated is { } tc ? new DateTimeOffset(tc.ToUniversalTime(), TimeSpan.Zero) : since;
                    if (counts.TryGetValue(key, out var cur))
                        counts[key] = (cur.n + 1, when > cur.last ? when : cur.last);
                    else
                        counts[key] = (1, when);
                }
            }
            return (counts, null);
        }
        catch (Exception ex) { return (counts, $"Could not read the {logName} log: {ex.Message}"); }
    }

    private static EventRecord? SafeRead(EventLogReader reader)
    {
        try { return reader.ReadEvent(); }
        catch (Exception) { return null; }
    }

    private static string Crashes(TimeSpan window)
    {
        var insights = CrashScanner.Scan(new CrashScanner.ScanOptions { Window = window, Max = 40 });
        if (insights.Count == 0) return $"No crashes, hangs or stop errors recorded in the last {window.TotalDays:0} days.";
        var sb = new StringBuilder();
        foreach (var i in insights.OrderByDescending(i => i.When))
            sb.AppendLine($"- {i.When.LocalDateTime:yyyy-MM-dd HH:mm}  {i.Kind}  {i.Subject}  ({i.Confidence} confidence)");
        return sb.ToString();
    }

    // -------------------------------------------------------------- helpers

    private static readonly Regex MacRx =
        new(@"\b([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b", RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

    private static string Scrub(string text)
    {
        string user = Environment.UserName;
        string machine = Environment.MachineName;

        // Path forms first — these are exact and must always be scrubbed, whatever the name is.
        text = text.Replace($@"\{user}\", @"\<user>\", StringComparison.OrdinalIgnoreCase)
                   .Replace($"/{user}/", "/<user>/", StringComparison.OrdinalIgnoreCase);

        try
        {
            // Whole-word only, so a short login name ("sam", "admin") does not eat a substring
            // of an unrelated word ("Samsung", "administrator") in an event-log message.
            if (user.Length >= 2)
                text = Regex.Replace(text, $@"\b{Regex.Escape(user)}\b", "<user>",
                    RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
            if (machine.Length >= 2)
                text = Regex.Replace(text, $@"\b{Regex.Escape(machine)}\b", "<machine>",
                    RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
            text = MacRx.Replace(text, "<mac>");
        }
        catch (RegexMatchTimeoutException)
        {
            // pathological input; the user/machine/MAC that got through is on the user's own report
        }
        return text;
    }

    private static string Bytes(ulong b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = b; int k = 0;
        while (v >= 1024 && k < u.Length - 1) { v /= 1024; k++; }
        return k <= 1 ? $"{v:0} {u[k]}" : $"{v:0.0} {u[k]}";
    }
}

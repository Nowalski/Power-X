using System.Runtime.Versioning;
using PowerX.Core.Diagnostics.Crash;
using PowerX.Core.Startup;
using PowerX.Core.Tweaks;

namespace PowerX.Core.Diagnostics;

public enum RecommendationImpact { High, Medium, Low }

/// <summary>One finding from a health check, pointing at the page that can act on it. PowerX never
/// applies anything from here itself — every recommendation is "go look at this page".</summary>
public sealed record Recommendation
{
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required RecommendationImpact Impact { get; init; }
    /// <summary>The nav tag of the page that addresses this, e.g. "tools", "drivers".</summary>
    public string? NavigateTag { get; init; }
    public string? NavigateLabel { get; init; }
}

public sealed record HealthReport
{
    public required DateTimeOffset When { get; init; }
    public required IReadOnlyList<Recommendation> Items { get; init; }
    public required bool Deep { get; init; }

    public int High => Items.Count(i => i.Impact == RecommendationImpact.High);
    public int Medium => Items.Count(i => i.Impact == RecommendationImpact.Medium);
    public int Low => Items.Count(i => i.Impact == RecommendationImpact.Low);

    /// <summary>A rough 0-100 sense of how much is outstanding — not a score to chase, just
    /// something to watch trend over time.</summary>
    public int Score => Math.Clamp(100 - High * 14 - Medium * 6 - Low * 2, 0, 100);
}

/// <summary>
/// Runs the checks PowerX already knows how to make (pending restart, driver age, battery wear,
/// boot slowdowns, firewall, event-log errors, disk space and health, crashes, broken startup
/// entries, unapplied recommended tweaks, antivirus status) and turns them into one prioritised
/// list. Every item just points at the existing page that handles it — this page never changes
/// anything by itself. Each check is independent and best-effort: one failing does not blank the
/// rest of the report.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HealthCheck
{
    /// <summary><paramref name="deep"/> additionally runs the component-store (WinSxS) analysis,
    /// which calls DISM and can take up to a minute — skipped by default.</summary>
    public static async Task<HealthReport> ScanAsync(bool deep = false, CancellationToken ct = default)
    {
        var items = new List<Recommendation>();

        void Add(string category, string title, string detail, RecommendationImpact impact, string? tag = null, string? label = null)
            => items.Add(new Recommendation { Category = category, Title = title, Detail = detail, Impact = impact, NavigateTag = tag, NavigateLabel = label ?? "Go to page" });

        void Safe(string category, Action check)
        {
            try { check(); }
            catch (Exception) { /* one provider failing should not blank the report */ }
        }

        Safe("Restart", () =>
        {
            var pending = PendingReboot.Check();
            if (pending.Pending)
                Add("Restart", "A restart is pending", pending.Reasons.FirstOrDefault() ?? "Windows is waiting on a restart.",
                    RecommendationImpact.High, "tools", "Open Tools");
        });

        Safe("Security", () =>
        {
            var status = Defender.Status();
            if (status.Unprotected)
                Add("Security", "No active real-time antivirus", "Nothing is watching for malware in real time right now.",
                    RecommendationImpact.High, "security", "Open Security");
        });

        Safe("Firewall", () =>
        {
            var state = FirewallRules.ProfileState();
            if (state.AnyOff)
                Add("Firewall", "Firewall is off for a network profile",
                    "At least one Windows Firewall profile (domain, private or public) is turned off.",
                    RecommendationImpact.High, "firewall", "Open Firewall");

            int review = FirewallRules.Rules().Count(r => r.WorthReviewing);
            if (review > 0)
                Add("Firewall", $"{review} broad inbound rule{(review == 1 ? "" : "s")} worth a look",
                    "An enabled rule allows any program in over the public network on a specific port.",
                    RecommendationImpact.Medium, "firewall", "Open Firewall");
        });

        Safe("Storage", () =>
        {
            foreach (var v in StorageInfo.Volumes())
            {
                if (v.UsedPercent >= 95)
                    Add("Storage", $"Drive {v.Drive} is almost full", $"{v.UsedPercent:0}% used, {Bytes(v.FreeBytes)} free.",
                        RecommendationImpact.High, "storage", "Open Storage explorer");
                else if (v.UsedPercent >= 90)
                    Add("Storage", $"Drive {v.Drive} is nearly full", $"{v.UsedPercent:0}% used, {Bytes(v.FreeBytes)} free.",
                        RecommendationImpact.Medium, "storage", "Open Storage explorer");
            }
            foreach (var d in StorageInfo.PhysicalDisks())
                if (d.Health is "Warning" or "Unhealthy")
                    Add("Storage", $"{d.Name} reports {d.Health.ToLowerInvariant()} health",
                        "Back up what matters from this disk and consider replacing it.",
                        RecommendationImpact.High, "tools", "Open Tools");
        });

        Safe("Startup", () =>
        {
            var entries = StartupProvider.Enumerate();
            int broken = entries.Count(e => e.Broken);
            if (broken > 0)
                Add("Startup", $"{broken} startup entr{(broken == 1 ? "y points" : "ies point")} at missing programs",
                    "Left behind by an app that was removed without cleaning up after itself. Safe to remove.",
                    RecommendationImpact.Low, "startup", "Open Startup");

            var (boot, bootItems) = BootPerformance.Read();
            if (boot is { Degraded: true })
                Add("Startup", "Your last boot was slower than usual", "Windows itself flagged this boot as degraded.",
                    RecommendationImpact.Medium, "startup", "Open Startup");
            int highImpact = bootItems.Count(b => b.Impact == StartupImpact.High);
            if (highImpact > 0)
                Add("Startup", $"{highImpact} startup app{(highImpact == 1 ? "" : "s")} measured as high boot impact",
                    "Consider disabling or delaying the slowest ones.",
                    RecommendationImpact.Low, "startup", "Open Startup");
        });

        Safe("Scheduled tasks", () =>
        {
            int telemetry = TaskInventory.Enumerate().Count(t => t.Stance == TaskStance.Telemetry && t.Enabled);
            if (telemetry > 0)
                Add("Scheduled tasks", $"{telemetry} telemetry task{(telemetry == 1 ? "" : "s")} enabled",
                    "Reporting tasks you can safely turn off if you would rather they didn't run.",
                    RecommendationImpact.Low, "tasks", "Open Scheduled tasks");
        });

        Safe("Drivers", () =>
        {
            var drivers = DriverInventory.Read();
            int veryOld = drivers.Count(d => d.Age == DriverAge.VeryOld);
            int unsigned = drivers.Count(d => !d.Signed);
            if (veryOld > 0)
                Add("Drivers", $"{veryOld} driver{(veryOld == 1 ? "" : "s")} are five years old or more",
                    "Worth checking the vendor for a newer version.", RecommendationImpact.Medium, "drivers", "Open Drivers");
            if (unsigned > 0)
                Add("Drivers", $"{unsigned} unsigned driver{(unsigned == 1 ? "" : "s")}",
                    "Not necessarily a problem, but worth knowing what they are.",
                    RecommendationImpact.Low, "drivers", "Open Drivers");
        });

        Safe("Battery", () =>
        {
            var battery = BatteryHealth.ReadAsync(ct).GetAwaiter().GetResult();
            if (battery.HasBattery && battery.WearPercent >= 50)
                Add("Battery", $"Battery health is poor ({battery.WearPercent}% capacity lost)",
                    "Runtime on battery will be noticeably shorter than when it was new.",
                    RecommendationImpact.Medium, "tools", "Open Tools");
            else if (battery.HasBattery && battery.WearPercent >= 30)
                Add("Battery", $"Battery is wearing ({battery.WearPercent}% capacity lost)",
                    "Normal for an older laptop, worth knowing about.", RecommendationImpact.Low, "tools", "Open Tools");
        });

        Safe("Event log", () =>
        {
            var groups = EventLogBrowser.Read(TimeSpan.FromDays(7), includeWarnings: false);
            int critical = groups.Count(g => g.Level == EventLevel2.Critical);
            if (critical > 0)
                Add("Event log", $"{critical} critical event{(critical == 1 ? "" : "s")} in the last 7 days",
                    "Often an unexpected shutdown or a serious driver fault.", RecommendationImpact.Medium, "events", "Open Event log");
        });

        Safe("Crashes", () =>
        {
            var crashes = CrashScanner.Scan(new CrashScanner.ScanOptions { Window = TimeSpan.FromDays(7), Max = 20 });
            if (crashes.Count > 0)
                Add("Crashes", $"{crashes.Count} crash or hang in the last 7 days", "See Crash insights for what Windows recorded about each one.",
                    RecommendationImpact.Medium, "crashes", "Open Crash insights");
        });

        Safe("Tweaks", () =>
        {
            var engine = new TweakEngine(TweakCatalog.Default);
            int missing = engine.GetAllStatus().Count(s => s.Definition.Recommended && s.State != TweakState.Applied && s.State != TweakState.NotApplicable);
            if (missing > 0)
                Add("Tweaks", $"{missing} recommended tweak{(missing == 1 ? " is" : "s are")} not applied",
                    "Conservative, broadly safe changes you have not turned on yet.", RecommendationImpact.Low, "tweaks", "Open Tweaks");
        });

        if (deep)
        {
            try
            {
                var store = await ComponentStore.AnalyzeAsync(ct);
                if (store.Error is null && store.CleanupRecommended)
                    Add("Storage", $"Component store cleanup recommended ({Bytes((ulong)store.PotentialSavingsBytes)} reclaimable)",
                        "Windows itself flags this as worth doing.", RecommendationImpact.Low, "tools", "Open Tools");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { }
        }

        var ordered = items
            .OrderBy(i => i.Impact)
            .ThenBy(i => i.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new HealthReport { When = DateTimeOffset.Now, Items = ordered, Deep = deep };
    }

    private static string Bytes(ulong b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = b; int k = 0;
        while (v >= 1024 && k < u.Length - 1) { v /= 1024; k++; }
        return k <= 1 ? $"{v:0} {u[k]}" : $"{v:0.0} {u[k]}";
    }
}

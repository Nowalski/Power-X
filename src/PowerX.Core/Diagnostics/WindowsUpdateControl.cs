using System.Text.Json;
using Microsoft.Win32;
using PowerX.Core.Processes;
using PowerX.Core.Startup;

namespace PowerX.Core.Diagnostics;

public enum WindowsUpdateState { Normal, Paused, Disabled }

public sealed record WindowsUpdateStatus(WindowsUpdateState State, DateTimeOffset? PausedUntil, int MaxPauseDays);

/// <summary>
/// Pause / disable / restore Windows Update using documented UX settings, Group Policy keys,
/// service start-type values and the Update scheduled tasks. "Disable" is a security trade-off;
/// <see cref="Restore"/> puts every one of those back.
/// </summary>
public static class WindowsUpdateControl
{
    private const string Ux = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
    private const string AuPolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string WuPolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string Services = @"SYSTEM\CurrentControlSet\Services";

    private static readonly string[] UpdateServices = ["wuauserv", "UsoSvc", "WaaSMedicSvc", "uhssvc"];

    private static readonly string[] UpdateTasks =
    [
        @"\Microsoft\Windows\WindowsUpdate\Scheduled Start",
        @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan",
        @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan Static Task",
        @"\Microsoft\Windows\UpdateOrchestrator\UpdateModelTask",
        @"\Microsoft\Windows\UpdateOrchestrator\USO_UxBroker",
        @"\Microsoft\Windows\WaaSMedic\PerformRemediation",
        @"\Microsoft\Windows\InstallService\ScanForUpdates",
        @"\Microsoft\Windows\InstallService\ScanForUpdatesAsUser",
    ];

    public static WindowsUpdateStatus Status()
    {
        using var ux = Registry.LocalMachine.OpenSubKey(Ux);
        int maxPause = ux?.GetValue("FlightSettingsMaxPauseDays") is int m ? m : 35;

        // "Disabled" if any of the levers our Disable() pulls is down: the core service is set to
        // disabled, the update orchestrator service is disabled, or the AU 'never check' policy is
        // in force. (A single check on wuauserv missed machines disabled by other tools.)
        if (ServiceStart("wuauserv") == 4
            || ServiceStart("UsoSvc") == 4
            || (PolicyDword(AuPolicy, "NoAutoUpdate") == 1 && PolicyDword(AuPolicy, "AUOptions") == 1))
        {
            return new WindowsUpdateStatus(WindowsUpdateState.Disabled, null, maxPause);
        }

        var expiry = ParseDate(ux?.GetValue("PauseUpdatesExpiryTime")?.ToString());
        if (expiry is { } e && e > DateTimeOffset.UtcNow)
            return new WindowsUpdateStatus(WindowsUpdateState.Paused, e, maxPause);

        return new WindowsUpdateStatus(WindowsUpdateState.Normal, null, maxPause);
    }

    private static int ServiceStart(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{Services}\{name}");
        return key?.GetValue("Start") is int s ? s : -1;
    }

    private static int PolicyDword(string subKey, string name) => PolicyDwordOrNull(subKey, name) ?? -1;

    private static int? PolicyDwordOrNull(string subKey, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKey);
        return key?.GetValue(name) is int v ? v : null;
    }

    // ---- pre-change snapshot so Restore() puts back exactly what was there ----

    private const string BackupKey = @"SOFTWARE\PowerX";
    private const string BackupValue = "WindowsUpdateSnapshot";

    private sealed class WuSnapshot
    {
        public Dictionary<string, int> Services { get; set; } = new();   // only services whose key existed
        public Dictionary<string, int> AuPolicy { get; set; } = new();   // only values that existed
        public Dictionary<string, int> WuPolicy { get; set; } = new();
        public Dictionary<string, bool> Tasks { get; set; } = new();     // only tasks that existed
        public string TakenUtc { get; set; } = "";
    }

    private static WuSnapshot? LoadSnapshot()
    {
        using var k = Registry.LocalMachine.OpenSubKey(BackupKey);
        if (k?.GetValue(BackupValue) is not string json) return null;
        try { return JsonSerializer.Deserialize<WuSnapshot>(json); }
        catch (JsonException) { return null; }
    }

    private static void SaveSnapshot(WuSnapshot s)
    {
        using var k = Registry.LocalMachine.CreateSubKey(BackupKey, writable: true);
        k.SetValue(BackupValue, JsonSerializer.Serialize(s), RegistryValueKind.String);
    }

    private static void ClearSnapshot()
    {
        using var k = Registry.LocalMachine.OpenSubKey(BackupKey, writable: true);
        k?.DeleteValue(BackupValue, throwOnMissingValue: false);
    }

    private static readonly string[] AuValues = ["NoAutoUpdate", "AUOptions"];
    private const string DoNotConnect = "DoNotConnectToWindowsUpdateInternetLocations";

    private static readonly string[] UxPauseValues =
    [
        "PauseUpdatesStartTime", "PauseUpdatesExpiryTime",
        "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime",
        "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime",
    ];

    public static ActionResult Pause(int days)
    {
        try
        {
            var start = DateTimeOffset.UtcNow;
            var end = start.AddDays(Math.Clamp(days, 1, 35));
            string s = start.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string e = end.ToString("yyyy-MM-ddTHH:mm:ssZ");

            using var ux = Registry.LocalMachine.CreateSubKey(Ux, writable: true);
            ux.SetValue("PauseUpdatesStartTime", s, RegistryValueKind.String);
            ux.SetValue("PauseUpdatesExpiryTime", e, RegistryValueKind.String);
            ux.SetValue("PauseFeatureUpdatesStartTime", s, RegistryValueKind.String);
            ux.SetValue("PauseFeatureUpdatesEndTime", e, RegistryValueKind.String);
            ux.SetValue("PauseQualityUpdatesStartTime", s, RegistryValueKind.String);
            ux.SetValue("PauseQualityUpdatesEndTime", e, RegistryValueKind.String);
            return ActionResult.Ok;
        }
        catch (UnauthorizedAccessException) { return ActionResult.Fail("Administrator rights required."); }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }

    public static ActionResult Disable()
    {
        try
        {
            // Snapshot the current state once, before we touch anything, so Restore() can put
            // back exactly what was here (a user or their org may have deliberately configured
            // these). Don't overwrite an existing snapshot from an earlier Disable().
            if (LoadSnapshot() is null)
            {
                var snap = new WuSnapshot { TakenUtc = DateTimeOffset.UtcNow.ToString("o") };
                foreach (var name in UpdateServices)
                {
                    int s = ServiceStart(name);
                    if (s >= 0) snap.Services[name] = s;
                }
                foreach (var v in AuValues)
                    if (PolicyDwordOrNull(AuPolicy, v) is { } x) snap.AuPolicy[v] = x;
                if (PolicyDwordOrNull(WuPolicy, DoNotConnect) is { } y) snap.WuPolicy[DoNotConnect] = y;
                foreach (var t in UpdateTasks)
                    if (ScheduledTasks.GetEnabled(t) is { } b) snap.Tasks[t] = b;
                SaveSnapshot(snap);
            }

            foreach (var name in UpdateServices) SetServiceStart(name, 4);

            using (var au = Registry.LocalMachine.CreateSubKey(AuPolicy, writable: true))
            {
                au.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                au.SetValue("AUOptions", 1, RegistryValueKind.DWord); // "never check"
            }
            using (var wu = Registry.LocalMachine.CreateSubKey(WuPolicy, writable: true))
                wu.SetValue(DoNotConnect, 1, RegistryValueKind.DWord);

            ScheduledTasks.SetEnabledMany(UpdateTasks, false);
            return ActionResult.Ok;
        }
        catch (UnauthorizedAccessException) { return ActionResult.Fail("Administrator rights required."); }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }

    public static ActionResult Restore()
    {
        try
        {
            ClearUxPause();

            var snap = LoadSnapshot();
            if (snap is not null)
            {
                foreach (var name in UpdateServices)
                    if (snap.Services.TryGetValue(name, out int prior))
                        SetServiceStart(name, prior);
                    // absent from the snapshot ⇒ the service key didn't exist then ⇒ leave it

                using (var au = Registry.LocalMachine.OpenSubKey(AuPolicy, writable: true))
                    foreach (var v in AuValues)
                        RestoreOrDelete(au, v, snap.AuPolicy);

                using (var wu = Registry.LocalMachine.OpenSubKey(WuPolicy, writable: true))
                    RestoreOrDelete(wu, DoNotConnect, snap.WuPolicy);

                foreach (var task in UpdateTasks)
                    ScheduledTasks.SetEnabled(task, snap.Tasks.TryGetValue(task, out bool b) ? b : true);

                ClearSnapshot();
                return ActionResult.Ok;
            }

            // No snapshot (disabled by an older PowerX or another tool) — fall back to the
            // documented Windows defaults.
            SetServiceStart("wuauserv", 3);
            SetServiceStart("UsoSvc", 2);
            SetServiceStart("WaaSMedicSvc", 3);
            SetServiceStart("uhssvc", 3);

            using (var au = Registry.LocalMachine.OpenSubKey(AuPolicy, writable: true))
            {
                au?.DeleteValue("NoAutoUpdate", false);
                au?.DeleteValue("AUOptions", false);
            }
            using (var wu = Registry.LocalMachine.OpenSubKey(WuPolicy, writable: true))
                wu?.DeleteValue(DoNotConnect, false);

            ScheduledTasks.SetEnabledMany(UpdateTasks, true);
            return ActionResult.Ok;
        }
        catch (UnauthorizedAccessException) { return ActionResult.Fail("Administrator rights required."); }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }

    private static void RestoreOrDelete(RegistryKey? key, string name, Dictionary<string, int> prior)
    {
        if (key is null) return;
        if (prior.TryGetValue(name, out int v)) key.SetValue(name, v, RegistryValueKind.DWord);
        else key.DeleteValue(name, throwOnMissingValue: false);
    }

    private static void ClearUxPause()
    {
        using var ux = Registry.LocalMachine.OpenSubKey(Ux, writable: true);
        if (ux is null) return;
        foreach (var v in UxPauseValues) ux.DeleteValue(v, throwOnMissingValue: false);
    }

    private static void SetServiceStart(string name, int start)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{Services}\{name}", writable: true);
        key?.SetValue("Start", start, RegistryValueKind.DWord);
    }

    private static DateTimeOffset? ParseDate(string? s) =>
        DateTimeOffset.TryParse(s, out var d) ? d : null;
}

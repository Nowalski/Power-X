using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;

namespace PowerX.Core.Diagnostics;

public enum DefenderMode { NotAvailable, Normal, Passive, EdrBlock, Disabled }

public sealed record DefenderStatus
{
    public DefenderMode Mode { get; init; } = DefenderMode.NotAvailable;
    public string ModeText { get; init; } = "unknown";
    public bool RealTimeProtection { get; init; }
    public bool CloudProtection { get; init; }
    public bool BehaviorMonitor { get; init; }
    public bool TamperProtection { get; init; }
    public bool NetworkProtection { get; init; }
    public string PuaProtection { get; init; } = "unknown";
    public string SignatureVersion { get; init; } = "";
    public DateTimeOffset? SignatureUpdated { get; init; }
    public int SignatureAgeDays { get; init; }
    public DateTimeOffset? LastQuickScan { get; init; }
    public DateTimeOffset? LastFullScan { get; init; }
    public int ExclusionCount { get; init; }
    public string? Detail { get; init; }

    /// <summary>True when the machine has no active real-time antivirus at all.</summary>
    public bool Unprotected => Mode == DefenderMode.Disabled
        || (Mode == DefenderMode.Normal && !RealTimeProtection);
}

public enum DefenderThreatState { Unknown, Detected, Cleaned, Quarantined, Removed, Allowed, Blocked, ActionFailed }

public sealed record DefenderThreat
{
    public required string Name { get; init; }
    public required string Severity { get; init; }       // Low / Moderate / High / Severe
    public required DateTimeOffset When { get; init; }
    public required DefenderThreatState State { get; init; }
    public bool Active { get; init; }
    public bool DidExecute { get; init; }
    public string? Resource { get; init; }               // the file / path involved
}

/// <summary>
/// Reads Microsoft Defender's own status and history through its WMI provider, and can start a
/// Defender scan. PowerX is not an antivirus: this surfaces the protection that is already on the
/// machine, it does not replace it. Read-only except for <see cref="RunScanAsync"/>, which just
/// launches the built-in scanner.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Defender
{
    private const string Namespace = @"\\.\root\Microsoft\Windows\Defender";

    public static DefenderStatus Status()
    {
        try
        {
            var scope = new ManagementScope(Namespace);
            scope.Connect();

            ManagementBaseObject? s = null, p = null;
            using (var q = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_MpComputerStatus")))
                foreach (ManagementBaseObject o in q.Get()) { s = o; break; }
            using (var q = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_MpPreference")))
                foreach (ManagementBaseObject o in q.Get()) { p = o; break; }

            if (s is null)
                return new DefenderStatus { Detail = "The Defender status provider returned nothing. Another antivirus may be managing protection." };

            string runningMode = s["AMRunningMode"]?.ToString() ?? "Normal";
            bool rtp = Bool(s["RealTimeProtectionEnabled"]);
            var mode = runningMode switch
            {
                var m when m.Contains("Passive", StringComparison.OrdinalIgnoreCase) => DefenderMode.Passive,
                var m when m.Contains("EDR", StringComparison.OrdinalIgnoreCase) => DefenderMode.EdrBlock,
                _ when !Bool(s["AMServiceEnabled"]) || !Bool(s["AntivirusEnabled"]) => DefenderMode.Disabled,
                _ => DefenderMode.Normal,
            };

            int exclusions = 0;
            if (p is not null)
                foreach (var k in (string[])["ExclusionPath", "ExclusionExtension", "ExclusionProcess"])
                    if (p[k] is string[] arr) exclusions += arr.Length;

            return new DefenderStatus
            {
                Mode = mode,
                ModeText = mode switch
                {
                    DefenderMode.Normal => "Microsoft Defender is the active antivirus",
                    DefenderMode.Passive => "Microsoft Defender is in passive mode (another antivirus is primary)",
                    DefenderMode.EdrBlock => "Microsoft Defender is in EDR block mode",
                    DefenderMode.Disabled => "Microsoft Defender antivirus is turned off",
                    _ => "unknown",
                },
                RealTimeProtection = rtp,
                CloudProtection = Bool(s["IoavProtectionEnabled"]) || (p is not null && ToUInt(p["MAPSReporting"]) > 0),
                BehaviorMonitor = Bool(s["BehaviorMonitorEnabled"]),
                TamperProtection = Bool(s["IsTamperProtected"]),
                NetworkProtection = Bool(s["NISEnabled"]),
                PuaProtection = p is null ? "unknown" : ToUInt(p["PUAProtection"]) switch { 1 => "on", 2 => "audit", _ => "off" },
                SignatureVersion = s["AntivirusSignatureVersion"]?.ToString() ?? "",
                SignatureUpdated = Wmi(s["AntivirusSignatureLastUpdated"]),
                SignatureAgeDays = (int)ToUInt(s["AntivirusSignatureAge"]),
                LastQuickScan = Wmi(s["QuickScanEndTime"]),
                LastFullScan = Wmi(s["FullScanEndTime"]),
                ExclusionCount = exclusions,
            };
        }
        catch (Exception ex)
        {
            return new DefenderStatus { Detail = $"Could not read Defender status: {ex.Message}" };
        }
        finally { }
    }

    public static IReadOnlyList<DefenderThreat> ThreatHistory(int max = 100)
    {
        var result = new List<DefenderThreat>();
        try
        {
            var scope = new ManagementScope(Namespace);
            scope.Connect();

            var threats = new Dictionary<ulong, (string name, string sev, bool active, bool exec)>();
            using (var q = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_MpThreat")))
                foreach (ManagementBaseObject o in q.Get())
                    threats[ToU64(o["ThreatID"])] = (
                        o["ThreatName"]?.ToString() ?? "Unknown threat",
                        ToUInt(o["SeverityID"]) switch { 1 => "Low", 2 => "Moderate", 4 => "High", 5 => "Severe", _ => "Unknown" },
                        Bool(o["IsActive"]),
                        Bool(o["DidThreatExecute"]));

            using var d = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_MpThreatDetection"));
            foreach (ManagementBaseObject o in d.Get())
            {
                ulong id = ToU64(o["ThreatID"]);
                threats.TryGetValue(id, out var t);
                var res = o["Resources"] as string[];
                result.Add(new DefenderThreat
                {
                    Name = t.name ?? $"Threat {id}",
                    Severity = t.sev ?? "Unknown",
                    When = Wmi(o["InitialDetectionTime"]) ?? Wmi(o["LastThreatStatusChangeTime"]) ?? DateTimeOffset.MinValue,
                    State = ToUInt(o["ThreatStatusID"]) switch
                    {
                        1 => DefenderThreatState.Detected,
                        2 => DefenderThreatState.Cleaned,
                        3 => DefenderThreatState.Quarantined,
                        4 => DefenderThreatState.Removed,
                        5 => DefenderThreatState.Allowed,
                        6 => DefenderThreatState.Blocked,
                        >= 102 => DefenderThreatState.ActionFailed,
                        _ => DefenderThreatState.Unknown,
                    },
                    Active = t.active,
                    DidExecute = t.exec,
                    Resource = CleanResource(res is { Length: > 0 } ? res[0] : o["ProcessName"]?.ToString()),
                });
            }
        }
        catch (Exception)
        {
            return result;   // best effort: an empty list, never an exception across the boundary
        }
        return result.OrderByDescending(t => t.When).Take(max).ToList();
    }

    /// <summary>Launch a Defender scan and stream MpCmdRun's output. The token kills the scanner.</summary>
    public static async Task<int> RunScanAsync(bool full, Action<string> onLine, CancellationToken ct = default)
    {
        string? exe = ResolveMpCmdRun();
        if (exe is null)
        {
            onLine("MpCmdRun.exe was not found. Defender may not be installed on this machine.");
            return -1;
        }

        onLine(full ? "Starting a full Defender scan. This can take a long time." : "Starting a quick Defender scan.");
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(exe, $"-Scan -ScanType {(full ? 2 : 1)}")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLine(e.Data); };

        try
        {
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync(ct);
            onLine(p.ExitCode == 0 ? "Scan finished. No further action needed if nothing was reported above." : $"Scan exited with code {p.ExitCode}.");
            return p.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            try { Process.Start(new ProcessStartInfo(exe, "-Scan -CancelScan") { UseShellExecute = false, CreateNoWindow = true }); } catch { /* ignore */ }
            onLine("Scan cancelled.");
            return -1;
        }
        catch (Exception ex)
        {
            onLine($"Could not run the scan: {ex.Message}");
            return -1;
        }
    }

    private static string? ResolveMpCmdRun()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string stub = Path.Combine(pf, "Windows Defender", "MpCmdRun.exe");
        if (File.Exists(stub)) return stub;

        // The active copy lives under a versioned Platform folder.
        string platform = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows Defender", "Platform");
        if (Directory.Exists(platform))
        {
            var newest = new DirectoryInfo(platform).GetDirectories()
                .OrderByDescending(d => d.Name).FirstOrDefault();
            string p = Path.Combine(newest?.FullName ?? "", "MpCmdRun.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static string? CleanResource(string? r)
    {
        if (string.IsNullOrWhiteSpace(r)) return null;
        // Defender prefixes with "file:_" / "containerfile:_" / "webfile:_" etc.
        int i = r.IndexOf(":_", StringComparison.Ordinal);
        return i >= 0 ? r[(i + 2)..] : r;
    }

    private static bool Bool(object? o) => o is bool b && b;
    private static uint ToUInt(object? o) => o is null ? 0 : uint.TryParse(o.ToString(), out var v) ? v : 0;
    private static ulong ToU64(object? o) => o is null ? 0 : ulong.TryParse(o.ToString(), out var v) ? v : 0;

    private static DateTimeOffset? Wmi(object? o)
    {
        if (o is null) return null;
        try { return new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(o.ToString())); }
        catch { return null; }
    }
}

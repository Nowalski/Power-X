using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace PowerX.Core.Diagnostics;

public sealed record BatteryInfo
{
    public bool HasBattery { get; init; }
    public string? Name { get; init; }
    public string? Manufacturer { get; init; }
    public string? Chemistry { get; init; }

    public long DesignCapacityMwh { get; init; }
    public long FullChargeCapacityMwh { get; init; }
    public int CycleCount { get; init; }

    /// <summary>0-100. How much of the original capacity the battery has lost.</summary>
    public int WearPercent =>
        DesignCapacityMwh > 0 && FullChargeCapacityMwh > 0
            ? Math.Clamp((int)Math.Round(100.0 * (DesignCapacityMwh - FullChargeCapacityMwh) / DesignCapacityMwh), 0, 100)
            : 0;

    // live state
    public int ChargePercent { get; init; }
    public bool OnAcPower { get; init; }
    public bool Charging { get; init; }
    public TimeSpan? EstimatedRuntime { get; init; }

    /// <summary>Full-charge runtime estimate from the battery report, if present.</summary>
    public TimeSpan? DesignRuntime { get; init; }
    public TimeSpan? CurrentRuntimeAtFullCharge { get; init; }

    public string? Error { get; init; }

    public string Health => WearPercent switch
    {
        0 when FullChargeCapacityMwh == 0 => "unknown",
        <= 15 => "good",
        <= 30 => "fair",
        <= 50 => "worn",
        _ => "poor",
    };
}

/// <summary>
/// Battery wear, cycle count and runtime from <c>powercfg /batteryreport</c>, plus live charge
/// state from <c>GetSystemPowerStatus</c>. Read-only. On a desktop with no battery
/// <see cref="Read"/> returns <see cref="BatteryInfo.HasBattery"/> = false.
/// </summary>
[SupportedOSPlatform("windows")]
public static class BatteryHealth
{
    public static async Task<BatteryInfo> ReadAsync(CancellationToken ct = default)
    {
        var live = LivePowerState();
        if (!HasBatteryDevice())
            return new BatteryInfo { HasBattery = false };

        try
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"powerx-battery-{Guid.NewGuid():N}.xml");
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("powercfg.exe", $"/batteryreport /xml /output \"{tmp}\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                }))
                {
                    if (p is not null) await p.WaitForExitAsync(ct);
                }

                if (!File.Exists(tmp))
                    return live with { HasBattery = true, Error = "powercfg did not produce a battery report." };

                return ParseReport(await File.ReadAllTextAsync(tmp, ct)) with
                {
                    HasBattery = true,
                    ChargePercent = live.ChargePercent,
                    OnAcPower = live.OnAcPower,
                    Charging = live.Charging,
                    EstimatedRuntime = live.EstimatedRuntime,
                };
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return live with { HasBattery = true, Error = ex.Message };
        }
    }

    internal static BatteryInfo ParseReport(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return new BatteryInfo { HasBattery = true, Error = "The battery report could not be parsed." }; }

        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var battery = doc.Descendants(ns + "Battery").FirstOrDefault();

        long L(XElement? e, string name) => long.TryParse((string?)e?.Element(ns + name), out var v) ? v : 0;
        int I(XElement? e, string name) => int.TryParse((string?)e?.Element(ns + name), out var v) ? v : 0;

        long design = L(battery, "DesignCapacity");
        long full = L(battery, "FullChargeCapacity");

        // Runtime estimates: the report lists many rows over time; take the most recent.
        TimeSpan? Dur(string? s) => TimeSpan.TryParse(s, out var t) ? t
            : int.TryParse(s, out var secs) ? TimeSpan.FromSeconds(secs) : null;

        var lastEstimate = doc.Descendants(ns + "RuntimeEstimate").LastOrDefault();
        TimeSpan? atFull = Dur((string?)lastEstimate?.Attribute("FullChargeCapacity"))
                        ?? Dur((string?)lastEstimate?.Attribute("ActiveRuntime"));
        TimeSpan? atDesign = Dur((string?)lastEstimate?.Attribute("DesignCapacity"));

        return new BatteryInfo
        {
            HasBattery = true,
            Name = (string?)battery?.Element(ns + "Id"),
            Manufacturer = (string?)battery?.Element(ns + "Manufacturer"),
            Chemistry = (string?)battery?.Element(ns + "Chemistry"),
            DesignCapacityMwh = design,
            FullChargeCapacityMwh = full,
            CycleCount = I(battery, "CycleCount"),
            CurrentRuntimeAtFullCharge = atFull,
            DesignRuntime = atDesign,
        };
    }

    private static bool HasBatteryDevice()
    {
        if (!GetSystemPowerStatus(out var s)) return false;
        // BatteryFlag 128 = "no system battery"; 255 = unknown.
        return s.BatteryFlag != 128 && s.BatteryFlag != 255;
    }

    private static BatteryInfo LivePowerState()
    {
        if (!GetSystemPowerStatus(out var s)) return new BatteryInfo();
        TimeSpan? runtime = s.BatteryLifeTime is not uint.MaxValue and > 0
            ? TimeSpan.FromSeconds(s.BatteryLifeTime) : null;
        return new BatteryInfo
        {
            ChargePercent = s.BatteryLifePercent is <= 100 ? s.BatteryLifePercent : 0,
            OnAcPower = s.ACLineStatus == 1,
            Charging = (s.BatteryFlag & 8) != 0,
            EstimatedRuntime = runtime,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}

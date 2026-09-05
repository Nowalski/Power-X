using System.Management;
using System.Runtime.Versioning;

namespace PowerX.Core.Diagnostics;

public enum ThermalCategory { System, Disk }

public sealed record ThermalReading
{
    public required string Name { get; init; }
    public required ThermalCategory Category { get; init; }
    public required double TemperatureC { get; init; }
    /// <summary>Extra context for the row, e.g. a disk's bus type.</summary>
    public string Detail { get; init; } = "";
}

public sealed record ThermalReport
{
    public required IReadOnlyList<ThermalReading> Readings { get; init; }
    /// <summary>True if this machine exposes ACPI thermal-zone data at all (most desktops do not —
    /// it depends entirely on what the motherboard firmware chooses to report). When false, the
    /// absence of a CPU/system reading is not a bug: Windows has no built-in API for it without
    /// that firmware support, and PowerX does not reach for a vendor SDK or a kernel driver to
    /// work around that (same reasoning as GPU temperature — see docs/DECISIONS.md).</summary>
    public required bool AcpiThermalZoneSupported { get; init; }
}

/// <summary>
/// Every temperature reading PowerX can get from a public, in-box Windows API: ACPI thermal
/// zones (<c>MSAcpi_ThermalZoneTemperature</c>, <c>root\WMI</c> — mainly laptops; most desktop
/// motherboards do not populate this) and per-disk temperature (already read by
/// <see cref="StorageInfo.PhysicalDisks"/> from the storage reliability counters). CPU package and
/// GPU temperature are not exposed by any in-box Windows API — reading them needs a vendor SDK
/// (NVAPI, ADL, a CPU vendor tool) or a kernel driver, neither of which PowerX uses, so they are
/// honestly left out rather than faked or fetched via an undocumented path.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ThermalInfo
{
    public static ThermalReport Read()
    {
        var readings = new List<ThermalReading>();
        bool acpiSupported = TryReadAcpiThermalZones(readings);

        try
        {
            foreach (var d in StorageInfo.PhysicalDisks())
                if (d.TemperatureC is { } t)
                    readings.Add(new ThermalReading { Name = d.Name, Category = ThermalCategory.Disk, TemperatureC = t, Detail = d.BusType });
        }
        catch (Exception) { /* one source failing should not blank the report */ }

        return new ThermalReport
        {
            Readings = readings.OrderBy(r => r.Category).ThenByDescending(r => r.TemperatureC).ToList(),
            AcpiThermalZoneSupported = acpiSupported,
        };
    }

    private static bool TryReadAcpiThermalZones(List<ThermalReading> readings)
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            using var results = s.Get();
            bool any = false;
            foreach (var o in results)
            {
                any = true;
                if (o["CurrentTemperature"] is not { } raw) continue;
                // Tenths of a Kelvin, per the ACPI spec this class wraps.
                double celsius = Convert.ToDouble(raw) / 10.0 - 273.15;
                if (celsius is < -50 or > 150) continue;   // implausible — a broken/placeholder sensor
                string name = o["InstanceName"]?.ToString() ?? "Thermal zone";
                readings.Add(new ThermalReading { Name = CleanZoneName(name), Category = ThermalCategory.System, TemperatureC = celsius });
            }
            return any;
        }
        catch (ManagementException) { return false; }
        catch (Exception) { return false; }
    }

    // ACPI instance names look like "ACPI\ThermalZone\TZ00_0" — show just the zone id.
    private static string CleanZoneName(string instanceName)
    {
        int i = instanceName.LastIndexOf('\\');
        string tail = i >= 0 ? instanceName[(i + 1)..] : instanceName;
        int underscore = tail.IndexOf('_');
        string zone = underscore > 0 ? tail[..underscore] : tail;
        return $"Thermal zone {zone}";
    }
}

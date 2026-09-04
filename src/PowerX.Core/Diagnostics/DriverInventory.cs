using System.Management;
using System.Runtime.Versioning;

namespace PowerX.Core.Diagnostics;

public sealed record DriverEntry
{
    public required string Device { get; init; }
    public required string Version { get; init; }
    public DateTimeOffset? Date { get; init; }
    public string Provider { get; init; } = "";
    public string DeviceClass { get; init; } = "";
    public string InfName { get; init; } = "";
    public bool Signed { get; init; }

    public int AgeYears => Date is { } d ? (int)((DateTimeOffset.Now - d).TotalDays / 365.25) : 0;

    /// <summary>How stale the driver is, for a flag in the UI. Microsoft's own inbox drivers are
    /// dated deliberately old and are not a problem, so they are never flagged.</summary>
    public DriverAge Age =>
        Date is null ? DriverAge.Unknown
        : IsInbox ? DriverAge.Current
        : AgeYears >= 5 ? DriverAge.VeryOld
        : AgeYears >= 3 ? DriverAge.Old
        : DriverAge.Current;

    public bool IsInbox => Provider.Equals("Microsoft", StringComparison.OrdinalIgnoreCase);
}

public enum DriverAge { Unknown, Current, Old, VeryOld }

/// <summary>
/// Read-only inventory of the third-party and inbox drivers Windows has loaded, from
/// <c>Win32_PnPSignedDriver</c>. It flags drivers that are several years old so you can check the
/// vendor for a newer one. PowerX never downloads or installs a driver — same stance as the
/// updater (D-017).
/// </summary>
[SupportedOSPlatform("windows")]
public static class DriverInventory
{
    public static async Task<IReadOnlyList<DriverEntry>> ReadAsync(CancellationToken ct = default)
        => await Task.Run(Read, ct);

    public static IReadOnlyList<DriverEntry> Read()
    {
        var list = new List<DriverEntry>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion, DriverDate, DriverProviderName, DeviceClass, InfName, IsSigned "
                + "FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                string? device = o["DeviceName"]?.ToString();
                string? version = o["DriverVersion"]?.ToString();
                if (string.IsNullOrWhiteSpace(device) || string.IsNullOrWhiteSpace(version)) continue;

                DateTimeOffset? date = null;
                if (o["DriverDate"] is string dt && dt.Length >= 8)
                {
                    try { date = new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(dt)); }
                    catch { }
                }

                list.Add(new DriverEntry
                {
                    Device = device.Trim(),
                    Version = version.Trim(),
                    Date = date,
                    Provider = o["DriverProviderName"]?.ToString()?.Trim() ?? "",
                    DeviceClass = o["DeviceClass"]?.ToString()?.Trim() ?? "",
                    InfName = o["InfName"]?.ToString()?.Trim() ?? "",
                    Signed = o["IsSigned"] is bool b && b,
                });
            }
        }
        catch (Exception)
        {
            return list;   // best effort
        }

        // One physical driver package often binds to many devices; collapse to the newest per (device, provider).
        return list
            .GroupBy(d => (d.Device.ToLowerInvariant(), d.Provider.ToLowerInvariant()))
            .Select(g => g.OrderByDescending(d => d.Date ?? DateTimeOffset.MinValue).First())
            .OrderBy(d => d.DeviceClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Device, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

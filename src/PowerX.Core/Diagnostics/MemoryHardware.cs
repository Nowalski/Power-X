using System.Management;
using PowerX.Core.Interop;

namespace PowerX.Core.Diagnostics;

public sealed record MemoryModule
{
    public required string Slot { get; init; }
    public string BankLabel { get; init; } = "";
    public required ulong CapacityBytes { get; init; }
    public required int SpeedMtps { get; init; }          // rated (Speed)
    public required int ConfiguredSpeedMtps { get; init; } // actually running (ConfiguredClockSpeed)
    public required string Type { get; init; }             // DDR4 / DDR5 / …
    public required string FormFactor { get; init; }       // DIMM / SODIMM
    public required string Manufacturer { get; init; }
    public required string PartNumber { get; init; }
}

public sealed record MemoryHardware
{
    public required ulong TotalPhysicalBytes { get; init; }
    public required int SlotsTotal { get; init; }
    public int SlotsUsed => Modules.Count;
    public required ulong MaxCapacityBytes { get; init; }
    public IReadOnlyList<MemoryModule> Modules { get; init; } = [];

    /// <summary>Speed the sticks are actually running at (min of configured speeds), in MT/s.</summary>
    public int EffectiveSpeedMtps => Modules.Count == 0
        ? 0
        : Modules.Min(m => m.ConfiguredSpeedMtps > 0 ? m.ConfiguredSpeedMtps : m.SpeedMtps);

    public string DominantType => Modules
        .GroupBy(m => m.Type)
        .OrderByDescending(g => g.Count())
        .Select(g => g.Key)
        .FirstOrDefault() ?? "Unknown";

    /// <summary>WMI query — a few hundred ms. Call off the UI thread.</summary>
    public static MemoryHardware Query()
    {
        SystemInfoNative.GetPhysicallyInstalledSystemMemory(out ulong installedKb);
        ulong total = installedKb * 1024;
        int slotsTotal = 0;
        ulong maxCapacity = 0;
        var modules = new List<MemoryModule>();

        try
        {
            using (var arr = new ManagementObjectSearcher(
                "SELECT MemoryDevices, MaxCapacityEx, MaxCapacity FROM Win32_PhysicalMemoryArray"))
            {
                foreach (var o in arr.Get())
                {
                    slotsTotal += ToInt(o["MemoryDevices"]);
                    ulong maxEx = ToUlong(o["MaxCapacityEx"]);          // KB
                    ulong maxLegacy = ToUlong(o["MaxCapacity"]);        // KB
                    maxCapacity += (maxEx > 0 ? maxEx : maxLegacy) * 1024UL;
                }
            }

            using var mem = new ManagementObjectSearcher(
                "SELECT DeviceLocator, BankLabel, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, MemoryType, FormFactor, Manufacturer, PartNumber FROM Win32_PhysicalMemory");
            foreach (var o in mem.Get())
            {
                modules.Add(new MemoryModule
                {
                    Slot = (o["DeviceLocator"]?.ToString() ?? o["BankLabel"]?.ToString() ?? "?").Trim(),
                    BankLabel = (o["BankLabel"]?.ToString() ?? "").Trim(),
                    CapacityBytes = ToUlong(o["Capacity"]),
                    SpeedMtps = ToInt(o["Speed"]),
                    ConfiguredSpeedMtps = ToInt(o["ConfiguredClockSpeed"]),
                    Type = MemoryTypeName(ToInt(o["SMBIOSMemoryType"]), ToInt(o["MemoryType"])),
                    FormFactor = FormFactorName(ToInt(o["FormFactor"])),
                    Manufacturer = (o["Manufacturer"]?.ToString() ?? "").Trim(),
                    PartNumber = (o["PartNumber"]?.ToString() ?? "").Trim(),
                });
            }
        }
        catch (ManagementException)
        {
            // WMI unavailable / repository issue — return what we have (at least the total).
        }

        if (slotsTotal < modules.Count) slotsTotal = modules.Count;

        // Some boards report the same DeviceLocator (e.g. "DIMM 1") for every stick. Disambiguate
        // with the bank label when it is unique, otherwise number the slots.
        if (modules.Select(m => m.Slot).Distinct(StringComparer.OrdinalIgnoreCase).Count() < modules.Count)
        {
            var banks = modules.Select(m => m.BankLabel).ToList();
            bool useBank = banks.All(b => !string.IsNullOrWhiteSpace(b))
                           && banks.Distinct(StringComparer.OrdinalIgnoreCase).Count() == modules.Count;
            for (int i = 0; i < modules.Count; i++)
                modules[i] = modules[i] with { Slot = useBank ? banks[i] : $"Slot {i + 1}" };
        }

        return new MemoryHardware
        {
            TotalPhysicalBytes = total,
            SlotsTotal = slotsTotal,
            MaxCapacityBytes = maxCapacity,
            Modules = modules.OrderBy(m => m.Slot, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private static int ToInt(object? v) => v is null ? 0 : Convert.ToInt32(v);
    private static ulong ToUlong(object? v) => v is null ? 0 : Convert.ToUInt64(v);

    // SMBIOS memory type (preferred) then the legacy Win32 MemoryType.
    private static string MemoryTypeName(int smbios, int legacy) => smbios switch
    {
        26 => "DDR4",
        34 => "DDR5",
        35 => "DDR5",
        24 => "DDR3",
        21 => "DDR2",
        _ => legacy switch
        {
            20 => "DDR",
            21 => "DDR2",
            24 => "DDR3",
            26 => "DDR4",
            _ => smbios > 0 ? $"Type {smbios}" : "Unknown",
        },
    };

    private static string FormFactorName(int ff) => ff switch
    {
        8 => "DIMM",
        12 => "SODIMM",
        13 => "SRIMM",
        _ => "Unknown",
    };
}

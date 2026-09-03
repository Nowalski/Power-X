using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PowerX.Core.Interop;

namespace PowerX.Core.Diagnostics;

public sealed record CacheInfo(int Level, string Type, ulong TotalBytes);

/// <summary>Static CPU description. Query once (it does not change) — see <see cref="Query"/>.</summary>
public sealed record CpuInfo
{
    public required string Name { get; init; }
    public required string Vendor { get; init; }
    public required int Packages { get; init; }
    public required int PhysicalCores { get; init; }
    public required int LogicalProcessors { get; init; }
    public required bool HyperThreading { get; init; }
    public required bool IsHybrid { get; init; }
    public int PerformanceCores { get; init; }
    public int EfficiencyCores { get; init; }
    public required bool VirtualizationFirmwareEnabled { get; init; }
    public required bool SecondLevelAddressTranslation { get; init; }
    public double BaseClockMhz { get; init; }
    public double MaxClockMhz { get; init; }
    public IReadOnlyList<CacheInfo> Caches { get; init; } = [];

    public CacheInfo? L1 => Caches.FirstOrDefault(c => c.Level == 1);
    public CacheInfo? L2 => Caches.FirstOrDefault(c => c.Level == 2);
    public CacheInfo? L3 => Caches.FirstOrDefault(c => c.Level == 3);

    public static CpuInfo Query()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        string name = key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unknown CPU";
        string vendor = key?.GetValue("VendorIdentifier")?.ToString()?.Trim() ?? "";
        double regMhz = key?.GetValue("~MHz") is int mhz ? mhz : 0;

        var topo = ReadTopology();
        var (baseMhz, maxMhz) = ReadClocks(Math.Max(1, topo.logical), regMhz);

        return new CpuInfo
        {
            Name = name,
            Vendor = FriendlyVendor(vendor),
            Packages = Math.Max(1, topo.packages),
            PhysicalCores = Math.Max(topo.cores, Environment.ProcessorCount / 2),
            LogicalProcessors = topo.logical > 0 ? topo.logical : Environment.ProcessorCount,
            HyperThreading = topo.cores > 0 && topo.logical > topo.cores,
            IsHybrid = topo.pCores > 0 && topo.eCores > 0,
            PerformanceCores = topo.pCores,
            EfficiencyCores = topo.eCores,
            VirtualizationFirmwareEnabled = SystemInfoNative.IsProcessorFeaturePresent(SystemInfoNative.PF_VIRT_FIRMWARE_ENABLED),
            SecondLevelAddressTranslation = SystemInfoNative.IsProcessorFeaturePresent(SystemInfoNative.PF_SECOND_LEVEL_ADDRESS_TRANSLATION),
            BaseClockMhz = baseMhz,
            MaxClockMhz = maxMhz,
            Caches = topo.caches,
        };
    }

    private static string FriendlyVendor(string id) => id switch
    {
        "GenuineIntel" => "Intel",
        "AuthenticAMD" => "AMD",
        "" => "Unknown",
        _ => id,
    };

    private static (int packages, int cores, int logical, int pCores, int eCores, List<CacheInfo> caches) ReadTopology()
    {
        var caches = new List<CacheInfo>();
        int packages = 0, cores = 0, logical = 0, pCores = 0, eCores = 0;
        var cacheTotals = new Dictionary<(int level, int type), ulong>();

        uint len = 0;
        SystemInfoNative.GetLogicalProcessorInformationEx(SystemInfoNative.LOGICAL_PROCESSOR_RELATIONSHIP.RelationAll, 0, ref len);
        if (len == 0) return (0, 0, 0, 0, 0, caches);

        nint buffer = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!SystemInfoNative.GetLogicalProcessorInformationEx(
                    SystemInfoNative.LOGICAL_PROCESSOR_RELATIONSHIP.RelationAll, buffer, ref len))
            {
                return (0, 0, 0, 0, 0, caches);
            }

            unsafe
            {
                byte* p = (byte*)buffer;
                byte* end = p + len;
                while (p < end)
                {
                    int relationship = *(int*)p;
                    uint size = *(uint*)(p + 4);
                    if (size == 0) break;

                    switch ((SystemInfoNative.LOGICAL_PROCESSOR_RELATIONSHIP)relationship)
                    {
                        case SystemInfoNative.LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorPackage:
                            packages++;
                            break;

                        case SystemInfoNative.LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore:
                        {
                            cores++;
                            byte efficiencyClass = *(p + 9);
                            ushort groupCount = *(ushort*)(p + 30);
                            int coreLogical = 0;
                            for (int g = 0; g < groupCount; g++)
                            {
                                ulong mask = *(ulong*)(p + 32 + g * 16); // sizeof(GROUP_AFFINITY) == 16 on x64/arm64
                                coreLogical += BitOperations.PopCount(mask);
                            }
                            logical += coreLogical;
                            if (efficiencyClass == 0) eCores++; else pCores++;
                            break;
                        }

                        case SystemInfoNative.LOGICAL_PROCESSOR_RELATIONSHIP.RelationCache:
                        {
                            byte level = *(p + 8);
                            uint cacheSize = *(uint*)(p + 12);
                            int type = *(int*)(p + 16);
                            var k = (level, type);
                            cacheTotals[k] = cacheTotals.GetValueOrDefault(k) + cacheSize;
                            break;
                        }
                    }

                    p += size;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        // If not hybrid, every core is a "performance" core; zero out the split.
        if (pCores == 0 || eCores == 0) { pCores = 0; eCores = 0; }

        foreach (int level in (int[])[1, 2, 3])
        {
            ulong total = 0;
            foreach (var kv in cacheTotals)
                if (kv.Key.level == level) total += kv.Value;
            if (total > 0) caches.Add(new CacheInfo(level, "", total));
        }

        return (packages, cores, logical, pCores, eCores, caches);
    }

    private static (double baseMhz, double maxMhz) ReadClocks(int logicalCount, double registryMhz)
    {
        int size = Marshal.SizeOf<SystemInfoNative.PROCESSOR_POWER_INFORMATION>() * logicalCount;
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            uint status = SystemInfoNative.CallNtPowerInformation(
                SystemInfoNative.ProcessorInformation, 0, 0, buffer, (uint)size);
            if (status != 0)
            {
                return (registryMhz, registryMhz);
            }

            double maxMhz = 0, limit = 0;
            unsafe
            {
                var span = new ReadOnlySpan<SystemInfoNative.PROCESSOR_POWER_INFORMATION>((void*)buffer, logicalCount);
                foreach (var e in span)
                {
                    maxMhz = Math.Max(maxMhz, e.MaxMhz);
                    limit = Math.Max(limit, e.MhzLimit);
                }
            }
            double baseMhz = registryMhz > 0 ? registryMhz : maxMhz;
            return (baseMhz, Math.Max(maxMhz, limit));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

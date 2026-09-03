using System.Management;

namespace PowerX.Core.Diagnostics;

public sealed record PhysicalDiskInfo
{
    public required string Name { get; init; }
    public required string MediaType { get; init; }   // SSD / HDD / SCM / Unknown
    public required string BusType { get; init; }      // NVMe / SATA / USB / …
    public required ulong SizeBytes { get; init; }
    public required string Health { get; init; }       // Healthy / Warning / Unhealthy / Unknown
    public int? TemperatureC { get; init; }
    public int? WearPercent { get; init; }             // SSD used-endurance, if reported
}

public sealed record VolumeInfo
{
    public required string Drive { get; init; }        // "C:\"
    public required string Label { get; init; }
    public required string FileSystem { get; init; }
    public required ulong TotalBytes { get; init; }
    public required ulong FreeBytes { get; init; }
    public double UsedPercent => TotalBytes == 0 ? 0 : 100.0 * (TotalBytes - FreeBytes) / TotalBytes;
}

public static class StorageInfo
{
    public static IReadOnlyList<VolumeInfo> Volumes()
    {
        var list = new List<VolumeInfo>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;
            try
            {
                list.Add(new VolumeInfo
                {
                    Drive = d.Name,
                    Label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel,
                    FileSystem = d.DriveFormat,
                    TotalBytes = (ulong)d.TotalSize,
                    FreeBytes = (ulong)d.TotalFreeSpace,
                });
            }
            catch (IOException) { /* transient */ }
        }
        return list;
    }

    public static IReadOnlyList<PhysicalDiskInfo> PhysicalDisks()
    {
        var list = new List<PhysicalDiskInfo>();
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();
            using var s = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk"));
            var reliability = ReliabilityByName(scope);
            foreach (var o in s.Get())
            {
                string name = o["FriendlyName"]?.ToString()?.Trim() ?? "Disk";
                reliability.TryGetValue(name, out var rel);
                list.Add(new PhysicalDiskInfo
                {
                    Name = name,
                    MediaType = ToInt(o["MediaType"]) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Unknown" },
                    BusType = BusName(ToInt(o["BusType"])),
                    SizeBytes = ToULong(o["Size"]),
                    Health = ToInt(o["HealthStatus"]) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown" },
                    TemperatureC = rel.Temp > 0 ? rel.Temp : null,
                    WearPercent = rel.Wear >= 0 ? rel.Wear : null,
                });
            }
        }
        catch (ManagementException) { /* Storage namespace unavailable */ }
        catch (UnauthorizedAccessException) { }
        return list;
    }

    private static Dictionary<string, (int Temp, int Wear)> ReliabilityByName(ManagementScope scope)
    {
        var map = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var disks = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT ObjectId, FriendlyName FROM MSFT_PhysicalDisk"));
            foreach (ManagementObject disk in disks.Get())
            {
                string name = disk["FriendlyName"]?.ToString()?.Trim() ?? "";
                try
                {
                    foreach (ManagementObject rc in disk.GetRelated("MSFT_StorageReliabilityCounter"))
                    {
                        int temp = ToInt(rc["Temperature"]);
                        int wear = rc["Wear"] is not null ? ToInt(rc["Wear"]) : -1;
                        map[name] = (temp, wear);
                    }
                }
                catch (ManagementException) { }
            }
        }
        catch (ManagementException) { }
        return map;
    }

    private static string BusName(int b) => b switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "1394", 5 => "SSA", 6 => "Fibre Channel",
        7 => "USB", 8 => "RAID", 9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC",
        17 => "NVMe", _ => "Unknown",
    };

    private static int ToInt(object? v) => v is null ? 0 : Convert.ToInt32(v);
    private static ulong ToULong(object? v) => v is null ? 0 : Convert.ToUInt64(v);
}

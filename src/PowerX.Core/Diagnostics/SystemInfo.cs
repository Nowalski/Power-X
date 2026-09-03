using Microsoft.Win32;

namespace PowerX.Core.Diagnostics;

public sealed record SystemInfo
{
    public required string WindowsEdition { get; init; }
    public required string DisplayVersion { get; init; }
    public required int Build { get; init; }
    public required int UpdateBuildRevision { get; init; }
    public required string Architecture { get; init; }
    public DateTimeOffset? InstallDate { get; init; }
    public required string CpuName { get; init; }
    public required int LogicalProcessors { get; init; }
    public required ulong TotalPhysicalMemory { get; init; }
    public required string MachineName { get; init; }
    public required bool IsElevated { get; init; }

    public string BuildString => UpdateBuildRevision > 0 ? $"{Build}.{UpdateBuildRevision}" : Build.ToString();
}

public static class SystemInfoProvider
{
    public static SystemInfo Collect()
    {
        using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string Get(string name, string fallback = "") => cv?.GetValue(name)?.ToString() ?? fallback;
        int GetInt(string name) => cv?.GetValue(name) is int i ? i : int.TryParse(cv?.GetValue(name)?.ToString(), out var p) ? p : 0;

        var mem = new Interop.Kernel32.MEMORYSTATUSEX
        {
            dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Interop.Kernel32.MEMORYSTATUSEX>(),
        };
        Interop.Kernel32.GlobalMemoryStatusEx(ref mem);

        DateTimeOffset? install = cv?.GetValue("InstallDate") is int secs
            ? DateTimeOffset.FromUnixTimeSeconds(secs)
            : null;

        // ProductName still reads "Windows 10 ..." on Windows 11; the build number is authoritative.
        int build = Environment.OSVersion.Version.Build;
        string edition = Get("ProductName", "Windows");
        if (build >= 22000 && edition.Contains("Windows 10"))
        {
            edition = edition.Replace("Windows 10", "Windows 11");
        }

        return new SystemInfo
        {
            WindowsEdition = edition,
            DisplayVersion = Get("DisplayVersion", Get("ReleaseId")),
            Build = Environment.OSVersion.Version.Build,
            UpdateBuildRevision = GetInt("UBR"),
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            InstallDate = install,
            CpuName = ReadCpuName(),
            LogicalProcessors = Environment.ProcessorCount,
            TotalPhysicalMemory = mem.ullTotalPhys,
            MachineName = Environment.MachineName,
            IsElevated = PrivilegeCheck.IsElevated(),
        };
    }

    private static string ReadCpuName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unknown CPU";
    }
}

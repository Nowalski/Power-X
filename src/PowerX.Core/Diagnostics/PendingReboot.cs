using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PowerX.Core.Diagnostics;

public sealed record PendingRebootStatus(bool Pending, IReadOnlyList<string> Reasons)
{
    public static readonly PendingRebootStatus None = new(false, []);
}

/// <summary>
/// Checks the documented places Windows records that a restart is owed, and says <em>why</em>.
/// Read-only — it only reads registry keys. This is often the real answer to "an update won't
/// install", "a setting won't stick", or "Windows keeps nagging me to restart".
/// </summary>
[SupportedOSPlatform("windows")]
public static class PendingReboot
{
    public static PendingRebootStatus Check()
    {
        var reasons = new List<string>();
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);

            if (KeyExists(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
                reasons.Add("Windows servicing (CBS) has staged changes that finish on the next restart.");
            if (KeyExists(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootInProgress"))
                reasons.Add("A servicing operation is mid-way and needs a restart to complete.");
            if (KeyExists(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\PackagesPending"))
                reasons.Add("One or more Windows packages are pending and apply on restart.");

            if (KeyExists(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                reasons.Add("Windows Update installed something that needs a restart.");
            if (KeyExists(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\PostRebootReporting"))
                reasons.Add("Windows Update is waiting to report the result of an update after the next restart.");
            if (HasSubKeys(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Services\Pending"))
                reasons.Add("A Windows Update service change is pending a restart.");

            using (var sm = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
            {
                if (sm?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 } ops)
                    reasons.Add($"{CountRenames(ops)} file(s) are queued to be replaced or deleted on restart "
                              + "(usually a program that updated files still in use).");
                if (sm?.GetValue("PendingFileRenameOperations2") is string[] { Length: > 0 })
                    reasons.Add("A second batch of file replacements is queued for the next restart.");
            }

            string? active = Read(hklm, @"SYSTEM\CurrentControlSet\Control\ComputerName\ActiveComputerName", "ComputerName");
            string? pendingName = Read(hklm, @"SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName", "ComputerName");
            if (active is not null && pendingName is not null &&
                !active.Equals(pendingName, StringComparison.OrdinalIgnoreCase))
                reasons.Add($"The PC was renamed to \"{pendingName}\"; the new name takes effect on restart (currently \"{active}\").");

            if (Read(hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing", "RebootPending") is not null)
                reasons.Add("Component servicing marked a reboot pending.");
        }
        catch (Exception)
        {
            // Reading these keys can fail on a locked-down box; report what we did see.
        }

        return reasons.Count == 0 ? PendingRebootStatus.None : new PendingRebootStatus(true, reasons);
    }

    private static bool KeyExists(RegistryKey root, string path)
    {
        using var k = root.OpenSubKey(path);
        return k is not null;
    }

    private static bool HasSubKeys(RegistryKey root, string path)
    {
        using var k = root.OpenSubKey(path);
        return k is not null && k.SubKeyCount > 0;
    }

    private static string? Read(RegistryKey root, string path, string value)
    {
        using var k = root.OpenSubKey(path);
        return k?.GetValue(value) as string;
    }

    private static int CountRenames(string[] ops)
    {
        // The value is pairs of (source, target); an empty target means "delete".
        return Math.Max(1, ops.Count(s => !string.IsNullOrEmpty(s)) / 2);
    }
}

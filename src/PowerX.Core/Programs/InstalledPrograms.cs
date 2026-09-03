using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using PowerX.Core.Processes;

namespace PowerX.Core.Programs;

public sealed record InstalledProgram
{
    public required string Name { get; init; }
    public string Version { get; init; } = "";
    public string Publisher { get; init; } = "";
    public DateTimeOffset? InstalledOn { get; init; }
    public long EstimatedSizeBytes { get; init; }
    public required string UninstallCommand { get; init; }
    public string? QuietUninstallCommand { get; init; }
    public bool IsMsi { get; init; }
    public string Scope { get; init; } = "";   // "machine" / "machine (32-bit)" / "user"
}

/// <summary>
/// Classic (Win32/MSI) installed programs from the <c>Uninstall</c> registry keys —
/// the same source as Settings > Apps > Installed apps. Uninstall launches the program's
/// own uninstaller; PowerX does not delete anything itself.
/// </summary>
public static class InstalledPrograms
{
    private const string UninstallSub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallSub32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<InstalledProgram> Enumerate()
    {
        var result = new List<InstalledProgram>();
        Read(Registry.LocalMachine, UninstallSub, "machine", result);
        Read(Registry.LocalMachine, UninstallSub32, "machine (32-bit)", result);
        Read(Registry.CurrentUser, UninstallSub, "user", result);

        return result
            .GroupBy(p => (p.Name, p.Version))
            .Select(g => g.First())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static ActionResult Uninstall(InstalledProgram program, bool quiet)
    {
        string command = (quiet ? program.QuietUninstallCommand : null) ?? program.UninstallCommand;
        if (string.IsNullOrWhiteSpace(command)) return ActionResult.Fail("This program has no uninstall command.");

        try
        {
            var (file, args) = SplitCommand(command);
            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
            return ActionResult.Ok;
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }

    private static void Read(RegistryKey hive, string sub, string scope, List<InstalledProgram> into)
    {
        using var root = hive.OpenSubKey(sub);
        if (root is null) return;

        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key is null) continue;

            string? display = key.GetValue("DisplayName") as string;
            if (string.IsNullOrWhiteSpace(display)) continue;
            if (key.GetValue("SystemComponent") is 1) continue;
            if (key.GetValue("ParentKeyName") is string) continue;             // component of another product
            string releaseType = key.GetValue("ReleaseType") as string ?? "";
            if (releaseType is "Update" or "Hotfix" or "Security Update") continue;
            if (display.StartsWith("KB", StringComparison.OrdinalIgnoreCase) &&
                display.Length > 2 && char.IsDigit(display[2])) continue;
            if (display.StartsWith("Update for ", StringComparison.OrdinalIgnoreCase)) continue;

            string uninstall = key.GetValue("UninstallString") as string ?? "";
            string quiet = key.GetValue("QuietUninstallString") as string ?? "";
            if (uninstall.Length == 0 && quiet.Length == 0) continue;

            long sizeKb = key.GetValue("EstimatedSize") is int s ? s : 0;

            into.Add(new InstalledProgram
            {
                Name = display.Trim(),
                Version = (key.GetValue("DisplayVersion") as string ?? "").Trim(),
                Publisher = (key.GetValue("Publisher") as string ?? "").Trim(),
                InstalledOn = ParseInstallDate(key.GetValue("InstallDate") as string),
                EstimatedSizeBytes = sizeKb * 1024L,
                UninstallCommand = uninstall.Length > 0 ? uninstall : quiet,
                QuietUninstallCommand = quiet.Length > 0 ? quiet : null,
                IsMsi = uninstall.Contains("msiexec", StringComparison.OrdinalIgnoreCase),
                Scope = scope,
            });
        }
    }

    private static DateTimeOffset? ParseInstallDate(string? s) =>
        DateTimeOffset.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    private static readonly string[] ExeExtensions = [".exe", ".msi", ".cmd", ".bat", ".com"];

    /// <summary>Split a registry UninstallString into an executable path and its arguments.</summary>
    public static (string File, string Args) SplitCommand(string command)
    {
        command = command.Trim();

        // Quoted executable: "…path with spaces…" trailing args
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 1) return (command[1..end], command[(end + 1)..].Trim());
        }

        // Unquoted: the path itself can contain spaces (e.g. C:\Program Files\App\unins.exe /S).
        // Split at the first executable-extension boundary, not the first space.
        foreach (var ext in ExeExtensions)
        {
            if (command.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return (command, "");
            int i = command.IndexOf(ext + " ", StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                int cut = i + ext.Length;
                return (command[..cut], command[cut..].Trim());
            }
        }

        // No recognisable extension — fall back to the first space.
        int space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..]);
    }
}

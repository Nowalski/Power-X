using System.Diagnostics;
using Microsoft.Win32;
using PowerX.Core.Processes;
using PowerX.Core.Programs;

namespace PowerX.Core.Startup;

public enum StartupSource
{
    RunUser,
    RunMachine,
    RunOnceUser,
    RunOnceMachine,
    StartupFolderUser,
    StartupFolderCommon,
    ScheduledTask,
}

public sealed record StartupEntry
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required StartupSource Source { get; init; }
    public required bool Enabled { get; init; }
    public string? ExecutablePath { get; init; }
    public string? Publisher { get; init; }

    /// <summary>The entry names a specific program file (an absolute path was found in the
    /// command), and that file does not exist — almost always a leftover from an app that was
    /// uninstalled without cleaning up after itself. Safe to remove.</summary>
    public bool Broken { get; init; }

    public string SourceLabel => Source switch
    {
        StartupSource.RunUser => "Registry · this user",
        StartupSource.RunMachine => "Registry · all users",
        StartupSource.RunOnceUser => "Registry · run once (this user)",
        StartupSource.RunOnceMachine => "Registry · run once (all users)",
        StartupSource.StartupFolderUser => "Startup folder · this user",
        StartupSource.StartupFolderCommon => "Startup folder · all users",
        StartupSource.ScheduledTask => "Scheduled task · at logon or boot",
        _ => Source.ToString(),
    };

    /// <summary>The Task Scheduler path, for scheduled-task entries.</summary>
    public string? TaskPath { get; init; }

    public bool RequiresAdmin => Source is StartupSource.RunMachine or StartupSource.RunOnceMachine or StartupSource.StartupFolderCommon;
}

/// <summary>
/// Enumerates the common documented auto-start locations and toggles them the same way
/// Task Manager does — via the <c>StartupApproved</c> keys, so disabling is fully reversible
/// and never deletes the entry.
/// </summary>
public static class StartupProvider
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ApprovedRun = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedFolder = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public static IReadOnlyList<StartupEntry> Enumerate()
    {
        var list = new List<StartupEntry>();
        ReadRun(list, Registry.CurrentUser, RunKey, StartupSource.RunUser, ApprovedRun, Registry.CurrentUser);
        ReadRun(list, Registry.LocalMachine, RunKey, StartupSource.RunMachine, ApprovedRun, Registry.LocalMachine);
        ReadRun(list, Registry.CurrentUser, RunOnceKey, StartupSource.RunOnceUser, null, null);
        ReadRun(list, Registry.LocalMachine, RunOnceKey, StartupSource.RunOnceMachine, null, null);
        ReadFolder(list, UserStartupFolder, StartupSource.StartupFolderUser, Registry.CurrentUser);
        ReadFolder(list, CommonStartupFolder, StartupSource.StartupFolderCommon, Registry.LocalMachine);

        foreach (var task in ScheduledTasks.Enumerate())
        {
            list.Add(new StartupEntry
            {
                Name = task.Name,
                Command = task.Action,
                Source = StartupSource.ScheduledTask,
                Enabled = task.Enabled,
                ExecutablePath = null,
                Publisher = string.IsNullOrWhiteSpace(task.Author) ? null : task.Author,
                TaskPath = task.Path,
            });
        }

        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>True when <see cref="SetEnabled"/> can actually change this entry's state.</summary>
    public static bool CanToggle(StartupEntry entry) =>
        entry.Source is not (StartupSource.RunOnceUser or StartupSource.RunOnceMachine);

    /// <summary>
    /// True when <see cref="Remove"/> can delete this entry: a RunOnce value (which can't be
    /// disabled any other way), or a regular Run entry that is <see cref="StartupEntry.Broken"/> —
    /// it points at a program that no longer exists, so disabling it would leave dead weight
    /// behind instead of cleaning it up. Folder / task entries are left for the user to handle
    /// at their source.
    /// </summary>
    public static bool CanRemove(StartupEntry entry) =>
        entry.Source is StartupSource.RunOnceUser or StartupSource.RunOnceMachine
        || (entry.Broken && entry.Source is StartupSource.RunUser or StartupSource.RunMachine);

    private const string RemovedBackup = @"SOFTWARE\PowerX\RemovedRunOnce";

    /// <summary>
    /// Delete a Run or RunOnce value so it won't run again. The name + value + hive are stashed
    /// under HKCU\SOFTWARE\PowerX\RemovedRunOnce first so it can be put back by hand.
    /// </summary>
    public static ActionResult Remove(StartupEntry entry)
    {
        if (!CanRemove(entry))
            return ActionResult.Fail("This entry can't be removed here. Use the On/Off switch instead.");
        try
        {
            bool machine = entry.Source is StartupSource.RunOnceMachine or StartupSource.RunMachine;
            string subKey = entry.Source is StartupSource.RunOnceUser or StartupSource.RunOnceMachine ? RunOnceKey : RunKey;
            var root = machine ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = root.OpenSubKey(subKey, writable: true);
            if (key is null) return ActionResult.Ok;   // key not present, nothing to remove

            object? current = key.GetValue(entry.Name);
            if (current is null) return ActionResult.Ok;   // already gone

            // stash for manual recovery (best-effort, always in HKCU)
            try
            {
                using var bak = Registry.CurrentUser.CreateSubKey(RemovedBackup, writable: true);
                string hive = machine ? "HKLM" : "HKCU";
                bak.SetValue($"{hive}\\{subKey}\\{entry.Name}", current.ToString() ?? "", RegistryValueKind.String);
            }
            catch (Exception) { /* backup is a courtesy, not a requirement */ }

            key.DeleteValue(entry.Name, throwOnMissingValue: false);
            return ActionResult.Ok;
        }
        catch (UnauthorizedAccessException) { return ActionResult.Fail("Administrator rights required for this entry."); }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }

    public static ActionResult SetEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.Source == StartupSource.ScheduledTask)
            return entry.TaskPath is not null
                ? ScheduledTasks.SetEnabled(entry.TaskPath, enabled)
                : ActionResult.Fail("Missing task path.");

        // RunOnce is not governed by StartupApproved — Windows runs the entry once at the next
        // start regardless. Writing a "disabled" marker here would be a silent no-op, so refuse.
        if (entry.Source is StartupSource.RunOnceUser or StartupSource.RunOnceMachine)
            return ActionResult.Fail(
                "RunOnce entries run a single time at the next sign-in and cannot be disabled. " +
                "Use \"Remove entry\" from the … menu if you don't want it to run.");

        try
        {
            bool folder = entry.Source is StartupSource.StartupFolderUser or StartupSource.StartupFolderCommon;
            var root = entry.RequiresAdmin ? Registry.LocalMachine : Registry.CurrentUser;
            string approvedKey = folder ? ApprovedFolder : ApprovedRun;

            using var key = root.CreateSubKey(approvedKey, writable: true);
            // 12-byte value: first byte 0x02 (enabled) or 0x03 (disabled), rest = timestamp (left 0).
            byte[] value = new byte[12];
            value[0] = (byte)(enabled ? 0x02 : 0x03);
            key.SetValue(entry.Name, value, RegistryValueKind.Binary);
            return ActionResult.Ok;
        }
        catch (UnauthorizedAccessException) { return ActionResult.Fail("Administrator rights required for this entry."); }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }

    public static ActionResult OpenLocation(StartupEntry entry)
    {
        try
        {
            if (entry.Source is StartupSource.StartupFolderUser)
                Process.Start(new ProcessStartInfo(UserStartupFolder) { UseShellExecute = true });
            else if (entry.Source is StartupSource.StartupFolderCommon)
                Process.Start(new ProcessStartInfo(CommonStartupFolder) { UseShellExecute = true });
            else if (!string.IsNullOrEmpty(entry.ExecutablePath) && File.Exists(entry.ExecutablePath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.ExecutablePath}\"") { UseShellExecute = true });
            else
                return ActionResult.Fail("No file location available for this entry.");
            return ActionResult.Ok;
        }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }

    // ---------------------------------------------------------------- internals

    private static void ReadRun(List<StartupEntry> list, RegistryKey hive, string subKey, StartupSource source,
        string? approvedKey, RegistryKey? approvedHive)
    {
        using var key = hive.OpenSubKey(subKey);
        if (key is null) return;

        using var approved = approvedKey is not null && approvedHive is not null
            ? approvedHive.OpenSubKey(approvedKey)
            : null;

        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            string command = key.GetValue(name)?.ToString() ?? "";
            var (exe, broken) = ResolveExe(command);
            list.Add(new StartupEntry
            {
                Name = name,
                Command = command,
                Source = source,
                Enabled = IsApproved(approved, name),
                ExecutablePath = exe,
                Broken = broken,
                Publisher = exe is not null ? SafeCompany(exe) : null,
            });
        }
    }

    private static void ReadFolder(List<StartupEntry> list, string folder, StartupSource source, RegistryKey approvedHive)
    {
        if (!Directory.Exists(folder)) return;
        using var approved = approvedHive.OpenSubKey(ApprovedFolder);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (file.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            string name = Path.GetFileName(file);
            string? exe = null;
            bool broken = false;
            if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                exe = file;
            }
            else if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string? target = Interop.ShellLink.ResolveTarget(file);
                if (target is not null && File.Exists(target)) exe = target;
                else if (target is not null) broken = true;   // shortcut resolves, but the target is gone
            }
            list.Add(new StartupEntry
            {
                Name = name,
                Command = exe is not null && !file.Equals(exe, StringComparison.OrdinalIgnoreCase) ? $"{file}  →  {exe}" : file,
                Source = source,
                Enabled = IsApproved(approved, name),
                ExecutablePath = exe,
                Broken = broken,
                Publisher = exe is not null ? SafeCompany(exe) : null,
            });
        }
    }

    private static bool IsApproved(RegistryKey? approved, string name)
    {
        if (approved?.GetValue(name) is not byte[] b || b.Length == 0) return true;
        return (b[0] & 0x01) == 0; // bit 0 set => disabled
    }

    /// <summary>Resolves a Run-key command to the program file it points at. When the file cannot
    /// be found, <c>Broken</c> says whether the command actually named a specific path (so it is a
    /// leftover worth flagging) as opposed to a bare command name PowerX did not try to search
    /// PATH for.</summary>
    private static (string? Exe, bool Broken) ResolveExe(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return (null, false);

        // Handles a quoted path, and an unquoted path that itself contains spaces
        // (e.g. C:\Program Files\App\app.exe --flag), by splitting at the .exe boundary.
        string path = InstalledPrograms.SplitCommand(command).File;

        try
        {
            path = Environment.ExpandEnvironmentVariables(path);
            if (File.Exists(path)) return (Path.GetFullPath(path), false);
            bool looksLikeASpecificPath = path.Length > 2 && (path[1] == ':' || path.StartsWith(@"\\", StringComparison.Ordinal));
            return (null, looksLikeASpecificPath);
        }
        catch { return (null, false); }
    }

    private static string? SafeCompany(string exe)
    {
        try { return FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim() is { Length: > 0 } c ? c : null; }
        catch { return null; }
    }

    private static string UserStartupFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Windows\Start Menu\Programs\Startup");

    private static string CommonStartupFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        @"Microsoft\Windows\Start Menu\Programs\Startup");
}

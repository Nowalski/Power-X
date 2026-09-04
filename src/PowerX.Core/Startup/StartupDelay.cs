using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PowerX.Core.Processes;

namespace PowerX.Core.Startup;

/// <summary>
/// Delays a registry <c>Run</c> startup entry: PowerX creates a scheduled task under
/// <c>\PowerX\</c> that launches the same program a chosen number of seconds after sign-in, and
/// disables the original entry the reversible way (<c>StartupApproved</c>). Undo deletes the task
/// and re-enables the entry. Useful for a High-impact entry from the boot-performance data:
/// the program still starts, just after the desktop is usable.
/// </summary>
[SupportedOSPlatform("windows")]
public static class StartupDelay
{
    private const string TaskFolder = "PowerX";
    private const int TASK_TRIGGER_LOGON = 9;
    private const int TASK_ACTION_EXEC = 0;
    private const int TASK_CREATE_OR_UPDATE = 6;
    private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;
    private const int TASK_RUNLEVEL_LUA = 0;

    public static bool CanDelay(StartupEntry e) =>
        e.Source is StartupSource.RunUser or StartupSource.RunMachine
        && !string.IsNullOrEmpty(e.ExecutablePath)
        && File.Exists(e.ExecutablePath);

    public static string TaskPathFor(StartupEntry e) => $@"\{TaskFolder}\Delayed - {Sanitize(e.Name)}";

    public static bool IsDelayed(StartupEntry e)
        => ScheduledTasks.GetEnabled(TaskPathFor(e)) is not null;

    public static ActionResult Delay(StartupEntry entry, int seconds)
    {
        if (!CanDelay(entry))
            return ActionResult.Fail("Only registry Run entries with a known program file can be delayed here.");
        if (seconds is < 5 or > 600)
            return ActionResult.Fail("Pick a delay between 5 and 600 seconds.");

        string exe = entry.ExecutablePath!;
        string args = ArgumentsFrom(entry.Command, exe);

        dynamic? svc = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null) return ActionResult.Fail("Task Scheduler is not available.");
            svc = Activator.CreateInstance(type);
            svc!.Connect();

            dynamic root = svc.GetFolder("\\");
            dynamic folder;
            try { folder = root.GetFolder(TaskFolder); }
            catch { folder = root.CreateFolder(TaskFolder); }

            dynamic def = svc.NewTask(0);
            def.RegistrationInfo.Author = "PowerX";
            def.RegistrationInfo.Description =
                $"Runs \"{entry.Name}\" {seconds}s after sign-in. Created by PowerX to reduce boot-time impact. "
                + "Delete this task (or use PowerX) to restore the original startup entry.";

            dynamic trigger = def.Triggers.Create(TASK_TRIGGER_LOGON);
            trigger.Delay = $"PT{seconds}S";

            dynamic action = def.Actions.Create(TASK_ACTION_EXEC);
            action.Path = exe;
            if (!string.IsNullOrWhiteSpace(args)) action.Arguments = args;
            try { action.WorkingDirectory = Path.GetDirectoryName(exe); } catch { }

            def.Settings.DisallowStartIfOnBatteries = false;
            def.Settings.StopIfGoingOnBatteries = false;
            def.Settings.StartWhenAvailable = true;
            def.Settings.ExecutionTimeLimit = "PT0S";   // no time limit
            def.Principal.RunLevel = TASK_RUNLEVEL_LUA;

            folder.RegisterTaskDefinition($"Delayed - {Sanitize(entry.Name)}", def,
                TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN);

            // Disable the original entry (reversible).
            var disabled = StartupProvider.SetEnabled(entry, false);
            if (!disabled.Success)
            {
                // roll the task back so we don't leave a double-launch
                try { folder.DeleteTask($"Delayed - {Sanitize(entry.Name)}", 0); } catch { }
                return ActionResult.Fail("Created the delay task but could not disable the original entry: " + disabled.Message);
            }

            return ActionResult.Ok;
        }
        catch (UnauthorizedAccessException) { return ActionResult.Fail("Administrator rights are required for this entry."); }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
        finally { Release(svc); }
    }

    public static ActionResult Undelay(StartupEntry entry)
    {
        dynamic? svc = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null) return ActionResult.Fail("Task Scheduler is not available.");
            svc = Activator.CreateInstance(type);
            svc!.Connect();

            try
            {
                dynamic folder = svc.GetFolder($"\\{TaskFolder}");
                folder.DeleteTask($"Delayed - {Sanitize(entry.Name)}", 0);
                // tidy the folder if it is now empty
                try { if (folder.GetTasks(1).Count == 0) svc.GetFolder("\\").DeleteFolder(TaskFolder, 0); } catch { }
            }
            catch (Exception) { /* task already gone */ }

            var enabled = StartupProvider.SetEnabled(entry, true);
            return enabled.Success ? ActionResult.Ok
                : ActionResult.Fail("Removed the delay task but could not re-enable the original entry: " + enabled.Message);
        }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
        finally { Release(svc); }
    }

    private static string ArgumentsFrom(string command, string exe)
    {
        command = command.Trim();
        // Strip the leading (possibly quoted) executable, return the rest.
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 0 ? command[(end + 1)..].Trim() : "";
        }
        string name = Path.GetFileName(exe);
        int i = command.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        return i >= 0 ? command[(i + name.Length)..].Trim() : "";
    }

    private static string Sanitize(string name)
    {
        Span<char> buf = stackalloc char[name.Length];
        int n = 0;
        foreach (char c in name)
            buf[n++] = char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' ? c : '_';
        return new string(buf[..n]).Trim();
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            try { Marshal.FinalReleaseComObject(com); } catch (Exception) { }
        }
    }
}

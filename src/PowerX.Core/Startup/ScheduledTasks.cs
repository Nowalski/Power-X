using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PowerX.Core.Processes;

namespace PowerX.Core.Startup;

public sealed record ScheduledStartupTask(string Path, string Name, bool Enabled, string Action, string Author);

/// <summary>
/// Logon- and boot-triggered scheduled tasks, via the Task Scheduler 2.0 COM API
/// (<c>Schedule.Service</c>). These are a common autostart mechanism Task Manager's
/// Startup tab does not show.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ScheduledTasks
{
    private const int TASK_TRIGGER_BOOT = 8;
    private const int TASK_TRIGGER_LOGON = 9;

    public static IReadOnlyList<ScheduledStartupTask> Enumerate()
    {
        var result = new List<ScheduledStartupTask>();
        dynamic? svc = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null) return result;
            svc = Activator.CreateInstance(type);
            svc!.Connect();
            Walk(svc.GetFolder("\\"), result);
        }
        catch (Exception)
        {
            // Task Scheduler unavailable / access denied — return what we have.
        }
        finally
        {
            Release(svc);
        }
        return result.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Current enabled state of one task, or null if it does not exist / cannot be read.</summary>
    public static bool? GetEnabled(string taskPath)
        => WithTask(taskPath, task => (bool?)(bool)task.Enabled, onError: () => null);

    public static ActionResult SetEnabled(string taskPath, bool enabled)
        => WithTask(taskPath,
            task => { task.Enabled = enabled; return ActionResult.Ok; },
            onError: static ex => ex is UnauthorizedAccessException
                ? ActionResult.Fail("Administrator rights required for this task.")
                : ActionResult.Fail(ex.Message));

    /// <summary>
    /// Toggle several tasks over a single Task Scheduler connection. Returns the first failure,
    /// or Ok if every task (that exists) was set.
    /// </summary>
    public static ActionResult SetEnabledMany(IEnumerable<string> taskPaths, bool enabled)
    {
        dynamic? svc = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null) return ActionResult.Fail("Task Scheduler is not available.");
            svc = Activator.CreateInstance(type);
            svc!.Connect();

            foreach (var path in taskPaths)
            {
                try
                {
                    int slash = path.LastIndexOf('\\');
                    dynamic folder = svc.GetFolder(slash <= 0 ? "\\" : path[..slash]);
                    dynamic task = folder.GetTask(path[(slash + 1)..]);
                    task.Enabled = enabled;
                    Release(task); Release(folder);
                }
                catch (Exception) { /* task gone / not permitted — skip, keep going */ }
            }
            return ActionResult.Ok;
        }
        catch (UnauthorizedAccessException) { return ActionResult.Fail("Administrator rights required."); }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
        finally { Release(svc); }
    }

    private static T WithTask<T>(string taskPath, Func<dynamic, T> body, Func<T> onError)
        => WithTask(taskPath, body, _ => onError());

    private static T WithTask<T>(string taskPath, Func<dynamic, T> body, Func<Exception, T> onError)
    {
        dynamic? svc = null, folder = null, task = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null) return onError(new InvalidOperationException("Task Scheduler is not available."));
            svc = Activator.CreateInstance(type);
            svc!.Connect();

            int slash = taskPath.LastIndexOf('\\');
            folder = svc.GetFolder(slash <= 0 ? "\\" : taskPath[..slash]);
            task = folder.GetTask(taskPath[(slash + 1)..]);
            return body(task);
        }
        catch (Exception ex)
        {
            return onError(ex);
        }
        finally
        {
            Release(task); Release(folder); Release(svc);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            try { Marshal.FinalReleaseComObject(com); } catch (Exception) { /* already released */ }
        }
    }

    private static void Walk(dynamic folder, List<ScheduledStartupTask> into)
    {
        try
        {
            foreach (dynamic task in folder.GetTasks(1)) // 1 = TASK_ENUM_HIDDEN
            {
                try
                {
                    dynamic def = task.Definition;
                    bool startup = false;
                    foreach (dynamic trig in def.Triggers)
                    {
                        int t = (int)trig.Type;
                        if (t is TASK_TRIGGER_LOGON or TASK_TRIGGER_BOOT) { startup = true; break; }
                    }
                    if (startup)
                    {
                        string action = "";
                        foreach (dynamic act in def.Actions)
                        {
                            try { action = $"{act.Path} {act.Arguments}".Trim(); } catch { }
                            break;
                        }

                        into.Add(new ScheduledStartupTask(
                            Path: (string)task.Path,
                            Name: (string)task.Name,
                            Enabled: (bool)task.Enabled,
                            Action: action,
                            Author: SafeAuthor(def)));
                    }
                    Release(def);
                }
                catch (Exception) { /* skip this task */ }
                finally { Release(task); }
            }

            foreach (dynamic sub in folder.GetFolders(0))
            {
                Walk(sub, into);
                Release(sub);
            }
        }
        catch (Exception) { /* folder not accessible */ }
    }

    private static string SafeAuthor(dynamic def)
    {
        try { return (string)def.RegistrationInfo.Author ?? ""; }
        catch { return ""; }
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PowerX.Core.Processes;

namespace PowerX.Core.Startup;

public sealed record ScheduledTaskInfo
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string Folder { get; init; }
    public required bool Enabled { get; init; }
    public bool Hidden { get; init; }
    public string Action { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string Triggers { get; init; } = "";
    public DateTimeOffset? LastRun { get; init; }
    public DateTimeOffset? NextRun { get; init; }
    public int LastResult { get; init; }

    /// <summary>Curated stance, filled in from <see cref="ScheduledTaskCatalog"/>.</summary>
    public TaskStance Stance { get; init; } = TaskStance.Unreviewed;
    public string? StanceNote { get; init; }
}

public enum TaskStance
{
    /// <summary>Not in the catalog — shown, read-only, "disable if you know what it is".</summary>
    Unreviewed,
    /// <summary>Windows needs this. PowerX will not offer to disable it.</summary>
    KeepSystem,
    /// <summary>Telemetry / advertising / upsell. Safe to disable.</summary>
    Telemetry,
    /// <summary>A third-party updater or helper. Disabling means updating that app yourself.</summary>
    Optional,
}

/// <summary>
/// A fuller read of the Task Scheduler than <see cref="ScheduledTasks"/> (which is only the
/// logon/boot subset for the Startup page): every task, with its schedule, last result and a
/// curated stance. Toggling reuses <see cref="ScheduledTasks.SetEnabled"/> so it stays reversible.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TaskInventory
{
    public static IReadOnlyList<ScheduledTaskInfo> Enumerate()
    {
        var result = new List<ScheduledTaskInfo>();
        dynamic? svc = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null) return result;
            svc = Activator.CreateInstance(type);
            svc!.Connect();
            Walk(svc.GetFolder("\\"), "\\", result);
        }
        catch (Exception) { }
        finally { Release(svc); }

        return result
            .Select(t => t with
            {
                Stance = ScheduledTaskCatalog.StanceFor(t.Path, out var note),
                StanceNote = note,
            })
            .OrderBy(t => t.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Walk(dynamic folder, string folderPath, List<ScheduledTaskInfo> into)
    {
        try
        {
            foreach (dynamic task in folder.GetTasks(1))   // 1 = include hidden
            {
                try { into.Add(Read(task, folderPath)); }
                catch (Exception) { }
                finally { Release(task); }
            }
            foreach (dynamic sub in folder.GetFolders(0))
            {
                try { Walk(sub, (string)sub.Path, into); }
                finally { Release(sub); }
            }
        }
        catch (Exception) { }
    }

    private static ScheduledTaskInfo Read(dynamic task, string folderPath)
    {
        dynamic def = task.Definition;
        try
        {
            string action = "";
            try
            {
                foreach (dynamic act in def.Actions)
                {
                    try { action = $"{act.Path} {act.Arguments}".Trim(); } catch { }
                    Release(act);
                    break;
                }
            }
            catch { }

            string triggers = "";
            try
            {
                var kinds = new List<string>();
                foreach (dynamic trig in def.Triggers)
                {
                    kinds.Add(TriggerName((int)trig.Type));
                    Release(trig);
                }
                triggers = string.Join(", ", kinds.Distinct());
            }
            catch { }

            bool hidden = false;
            string desc = "", author = "";
            try { hidden = (bool)def.Settings.Hidden; } catch { }
            try { desc = (string)def.RegistrationInfo.Description ?? ""; } catch { }
            try { author = (string)def.RegistrationInfo.Author ?? ""; } catch { }

            DateTimeOffset? Dt(object? v)
            {
                try { return v is DateTime d && d.Year > 1900 ? new DateTimeOffset(d) : null; }
                catch { return null; }
            }

            return new ScheduledTaskInfo
            {
                Path = (string)task.Path,
                Name = (string)task.Name,
                Folder = folderPath,
                Enabled = (bool)task.Enabled,
                Hidden = hidden,
                Action = action,
                Author = author,
                Description = desc,
                Triggers = triggers,
                LastRun = Dt(SafeGet(() => task.LastRunTime)),
                NextRun = Dt(SafeGet(() => task.NextRunTime)),
                LastResult = (int)(SafeGet(() => task.LastTaskResult) ?? 0),
            };
        }
        finally { Release(def); }
    }

    private static object? SafeGet(Func<object?> f) { try { return f(); } catch { return null; } }

    private static string TriggerName(int t) => t switch
    {
        0 => "on an event",
        1 => "once",
        2 => "daily",
        3 => "weekly",
        4 => "monthly",
        5 => "monthly (day of week)",
        6 => "on idle",
        7 => "at registration",
        8 => "at boot",
        9 => "at logon",
        11 => "on session change",
        _ => "scheduled",
    };

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            try { Marshal.FinalReleaseComObject(com); } catch (Exception) { }
        }
    }
}

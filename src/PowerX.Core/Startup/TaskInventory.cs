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
        dynamic? root = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null) return result;
            svc = Activator.CreateInstance(type);
            svc!.Connect();
            root = svc.GetFolder("\\");
            Walk(root, "\\", result);
        }
        catch (Exception) { }
        finally { Release(root); Release(svc); }

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
            dynamic tasks = folder.GetTasks(1);   // 1 = include hidden
            try
            {
                foreach (dynamic task in tasks)
                {
                    try { into.Add(Read(task, folderPath)); }
                    catch (Exception) { }
                    finally { Release(task); }
                }
            }
            finally { Release(tasks); }

            dynamic subFolders = folder.GetFolders(0);
            try
            {
                foreach (dynamic sub in subFolders)
                {
                    try { Walk(sub, (string)sub.Path, into); }
                    finally { Release(sub); }
                }
            }
            finally { Release(subFolders); }
        }
        catch (Exception) { }
    }

    private static ScheduledTaskInfo Read(dynamic task, string folderPath)
    {
        // The definition (actions, triggers, author, description, hidden) comes back in a single
        // COM call as XML rather than a dozen late-bound property reads each round-tripping to the
        // scheduler service - see TaskXml. Everything below that is *runtime* state, which is not
        // in the definition XML at all and still has to be read off the registered task itself.
        var parsed = TaskXml.Parse((string)task.Xml);

        // A task that has never run reports a sentinel timestamp rather than nothing (30/11/1999
        // on this build of Windows, 30/12/1899 on others), which the old "year > 1900" guard let
        // through, so the page showed "last ran 30.11.1999" beside result 0x41303, which is
        // SCHED_S_TASK_HAS_NOT_RUN. Task Scheduler 2.0 did not exist before Vista, so no genuine
        // run time can predate 2000 and anything earlier is the sentinel.
        DateTimeOffset? Dt(object? v)
        {
            try { return v is DateTime d && d.Year >= 2000 ? new DateTimeOffset(d) : null; }
            catch { return null; }
        }

        return new ScheduledTaskInfo
        {
            Path = (string)task.Path,
            Name = (string)task.Name,
            Folder = folderPath,
            Enabled = (bool)task.Enabled,
            Hidden = parsed?.Hidden ?? false,
            Action = parsed?.Action ?? "",
            Author = parsed?.Author ?? "",
            Description = parsed?.Description ?? "",
            Triggers = parsed?.Triggers ?? "",
            LastRun = Dt(SafeGet(() => task.LastRunTime)),
            NextRun = Dt(SafeGet(() => task.NextRunTime)),
            LastResult = (int)(SafeGet(() => task.LastTaskResult) ?? 0),
        };
    }

    private static object? SafeGet(Func<object?> f) { try { return f(); } catch { return null; } }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            try { Marshal.FinalReleaseComObject(com); } catch (Exception) { }
        }
    }
}

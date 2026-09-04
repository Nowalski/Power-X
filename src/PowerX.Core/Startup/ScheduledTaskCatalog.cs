namespace PowerX.Core.Startup;

/// <summary>
/// A small curated stance list for well-known scheduled tasks, matched by a case-insensitive
/// substring of the task's full path. Everything not listed stays <see cref="TaskStance.Unreviewed"/>
/// — visible, toggleable, but PowerX does not take a position. Nothing is ever deleted, only
/// disabled (reversible).
/// </summary>
public static class ScheduledTaskCatalog
{
    private sealed record Rule(string PathContains, TaskStance Stance, string Note);

    private static readonly Rule[] Rules =
    [
        // ---- Microsoft telemetry / advertising / upsell (safe to disable) ----
        new(@"\Microsoft\Windows\Customer Experience Improvement Program", TaskStance.Telemetry,
            "The Customer Experience Improvement Program (CEIP) uploads usage data to Microsoft."),
        new(@"\Microsoft\Windows\Application Experience", TaskStance.Telemetry,
            "Application compatibility telemetry and app-usage inventory sent to Microsoft."),
        new(@"\Microsoft\Windows\Autochk\Proxy", TaskStance.Telemetry,
            "Collects and uploads SQM (telemetry) data."),
        new(@"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector", TaskStance.Telemetry,
            "Uploads disk SMART data to Microsoft. The user-facing disk check is a separate task."),
        new(@"\Microsoft\Windows\Feedback\Siuf", TaskStance.Telemetry,
            "The 'Windows Feedback' prompts and their upload."),
        new(@"\Microsoft\Windows\Windows Error Reporting\QueueReporting", TaskStance.Telemetry,
            "Uploads queued error reports to Microsoft. Local crash analysis still works without it."),
        new(@"\Microsoft\Windows\CloudExperienceHost\CreateObjectTask", TaskStance.Optional,
            "Part of the out-of-box and account setup experience; harmless to leave enabled."),
        new(@"\Microsoft\Windows\Clip\License Validation", TaskStance.KeepSystem,
            "Validates Store app licenses."),
        new(@"\Microsoft\Windows\Maps\MapsUpdateTask", TaskStance.Optional,
            "Downloads offline map updates. Disable if you do not use offline maps."),
        new(@"\Microsoft\Windows\Retail Demo", TaskStance.Optional,
            "Store retail-demo mode. Irrelevant on a personal PC."),

        // ---- Windows components that must stay (KeepSystem) ----
        new(@"\Microsoft\Windows\UpdateOrchestrator", TaskStance.KeepSystem,
            "Drives Windows Update. Disabling here does not stop updates cleanly — use the Windows Update card instead."),
        new(@"\Microsoft\Windows\WindowsUpdate", TaskStance.KeepSystem, "Windows Update scheduling."),
        new(@"\Microsoft\Windows\SystemRestore\SR", TaskStance.KeepSystem, "Creates automatic restore points."),
        new(@"\Microsoft\Windows\Defrag\ScheduledDefrag", TaskStance.KeepSystem,
            "Storage optimization (TRIM on an SSD, defrag on an HDD). Leave it on."),
        new(@"\Microsoft\Windows\Windows Defender", TaskStance.KeepSystem, "Microsoft Defender scans and updates."),
        new(@"\Microsoft\Windows\Servicing", TaskStance.KeepSystem, "Component servicing."),
        new(@"\Microsoft\Windows\Task Manager", TaskStance.KeepSystem, "Task Manager support."),
        new(@"\Microsoft\Windows\Time Synchronization", TaskStance.KeepSystem, "Keeps the clock accurate."),
        new(@"\Microsoft\Windows\Time Zone", TaskStance.KeepSystem, "Automatic time-zone updates."),
        new(@"\Microsoft\Windows\Chkdsk\ProactiveScan", TaskStance.KeepSystem, "Background file-system health check."),
        new(@"\Microsoft\Windows\DiskCleanup", TaskStance.KeepSystem, "Automatic low-disk cleanup."),
        new(@"\Microsoft\Windows\.NET Framework\.NET Framework NGEN", TaskStance.KeepSystem,
            "Pre-compiles .NET assemblies during idle time. Leave it on."),
        new(@"\Microsoft\Windows\MUI\LPRemove", TaskStance.KeepSystem, "Removes unused language packs."),
        new(@"\Microsoft\Windows\Shell\CreateObjectTask", TaskStance.KeepSystem, "Explorer/shell support."),
        new(@"\Microsoft\Windows\WOF\WIM-Hash", TaskStance.KeepSystem, "Compressed-file support."),

        // ---- Common third-party updaters (Optional) ----
        new(@"\GoogleUpdateTask", TaskStance.Optional,
            "Google's silent updater (Chrome, Drive). Disable only if you will update Google apps yourself."),
        new(@"\GoogleSystem\GoogleUpdater", TaskStance.Optional, "Google's newer updater service task."),
        new(@"\MicrosoftEdgeUpdateTask", TaskStance.Optional,
            "Microsoft Edge's updater. Edge also updates through Windows Update."),
        new(@"\Adobe Acrobat Update Task", TaskStance.Optional, "Adobe Acrobat/Reader updater."),
        new(@"\Adobe GC Invoker Utility", TaskStance.Telemetry, "Adobe Genuine Software integrity/telemetry check."),
        new(@"\CCleaner", TaskStance.Optional, "CCleaner's scheduled run / update check."),
        new(@"\OneDrive", TaskStance.Optional, "OneDrive's standalone updater task."),
        new(@"\Overwolf", TaskStance.Optional, "Overwolf updater."),
        new(@"\EpicGamesLauncher", TaskStance.Optional, "Epic Games Launcher helper."),
        new(@"\NvTmRep", TaskStance.Telemetry, "NVIDIA telemetry reporting."),
        new(@"\NvProfileUpdater", TaskStance.Optional, "NVIDIA driver profile updater."),
        new(@"\NVIDIA GeForce Experience", TaskStance.Optional, "GeForce Experience background tasks."),
        new(@"\Intel\Intel Delivery Optimization", TaskStance.Optional, "Intel driver delivery task."),
        new(@"\AMDLinkUpdate", TaskStance.Optional, "AMD Link updater."),
        new(@"\klcp_update", TaskStance.Optional, "K-Lite Codec Pack update check."),
        new(@"\Razer", TaskStance.Optional, "Razer Synapse helper/updater."),
        new(@"\Dropbox Update", TaskStance.Optional, "Dropbox updater."),
        new(@"\brave", TaskStance.Optional, "Brave browser updater."),
        new(@"\MegaSync", TaskStance.Optional, "MEGA sync helper."),
    ];

    public static TaskStance StanceFor(string taskPath, out string? note)
    {
        foreach (var r in Rules)
        {
            if (taskPath.Contains(r.PathContains, StringComparison.OrdinalIgnoreCase))
            {
                note = r.Note;
                return r.Stance;
            }
        }
        note = null;
        return TaskStance.Unreviewed;
    }
}

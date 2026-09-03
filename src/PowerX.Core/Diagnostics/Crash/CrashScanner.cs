using System.Runtime.Versioning;

namespace PowerX.Core.Diagnostics.Crash;

/// <summary>
/// Merges what Windows already recorded about recent crashes / hangs / bugchecks into a list of
/// <see cref="CrashInsight"/>. It reads WER report folders, the Application and System event
/// logs, and — only when asked and only if a dump is present — the metadata streams of a
/// user-mode minidump. It never downloads symbols, never executes a dump, never uploads
/// anything, and never states a cause it cannot support.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CrashScanner
{
    // System DLLs that are almost always just the messenger, not the culprit.
    private static readonly HashSet<string> SystemMessengers = new(StringComparer.OrdinalIgnoreCase)
    {
        "ntdll.dll", "kernel32.dll", "kernelbase.dll", "combase.dll", "ole32.dll", "rpcrt4.dll",
        "user32.dll", "gdi32.dll", "gdi32full.dll", "win32u.dll", "msvcrt.dll", "ucrtbase.dll",
        "clr.dll", "coreclr.dll", "clrjit.dll", "mscordbi.dll", "mscoreei.dll",
        "Microsoft.UI.Xaml.dll", "Windows.UI.Xaml.dll", "wpfgfx_cor3.dll", "PresentationCore.dll",
        "shcore.dll", "shell32.dll", "windows.storage.dll", "twinapi.appcore.dll",
    };

    private static readonly HashSet<string> GraphicsDrivers = new(StringComparer.OrdinalIgnoreCase)
    {
        "nvwgf2umx.dll", "nvwgf2um.dll", "nvd3dumx.dll", "nvoglv64.dll", "nvlddmkm.sys",
        "atidxx64.dll", "aticfx64.dll", "amdxc64.dll", "amdkmdag.sys", "atikmdag.sys",
        "igd10iumd64.dll", "igdumdim64.dll", "igdkmd64.sys", "igc64.dll", "igdext64.dll",
    };

    public sealed record ScanOptions
    {
        public TimeSpan Window { get; init; } = TimeSpan.FromDays(30);
        public bool IncludeMachineStore { get; init; }   // ProgramData WER — needs elevation
        public bool ReadDumps { get; init; }             // parse minidump metadata when present
        public int Max { get; init; } = 100;
    }

    public static IReadOnlyList<CrashInsight> Scan(ScanOptions? options = null)
    {
        var opt = options ?? new ScanOptions();
        var since = DateTimeOffset.UtcNow - opt.Window;

        IReadOnlyList<WerReport> wer;
        IReadOnlyList<EventCrashRecord> events;
        try { wer = WerReportReader.Read(since, opt.IncludeMachineStore, opt.Max * 2); }
        catch (Exception) { wer = []; }
        try { events = EventLogCrashReader.Read(since, opt.Max * 3); }
        catch (Exception) { events = []; }

        var insights = new List<CrashInsight>();

        // 1) Bugchecks + unexpected shutdowns from the System log.
        foreach (var e in events.Where(e => e.Kind is EventCrashKind.Bugcheck or EventCrashKind.UnexpectedShutdown))
            insights.Add(FromKernelEvent(e, opt));

        // 2) App crashes / hangs: prefer a WER folder, corroborate with events.
        var dotNet = events.Where(e => e.Kind == EventCrashKind.DotNetRuntime).ToList();
        var appErrors = events.Where(e => e.Kind is EventCrashKind.AppError or EventCrashKind.AppHang).ToList();

        foreach (var w in wer.Where(w => IsAppEvent(w.EventType)))
        {
            var managed = dotNet.FirstOrDefault(d => Near(d.When, w.When) && SameApp(d.App, w.AppName));
            insights.Add(FromWer(w, managed, opt));
        }

        // 3) App-error / .NET events that had no WER folder (WER kept nothing).
        foreach (var e in appErrors)
        {
            if (wer.Any(w => Near(w.When, e.When) && SameApp(w.AppName, e.App))) continue;
            insights.Add(FromAppEvent(e, dotNet.FirstOrDefault(d => Near(d.When, e.When) && SameApp(d.App, e.App))));
        }
        foreach (var d in dotNet)
        {
            if (insights.Any(i => Near(i.When, d.When) && SameApp(i.Subject, d.App))) continue;
            insights.Add(FromDotNetOnly(d));
        }

        return insights
            .GroupBy(i => (i.Kind, Round(i.When), i.Subject))
            .Select(g => g.OrderByDescending(x => x.Confidence).First())
            .OrderByDescending(i => i.When)
            .Take(opt.Max)
            .ToList();
    }

    // ---------------------------------------------------------------- builders

    private static CrashInsight FromKernelEvent(EventCrashRecord e, ScanOptions opt)
    {
        if (e.Kind == EventCrashKind.UnexpectedShutdown)
            return new CrashInsight
            {
                When = e.When, Kind = CrashKind.Bugcheck, Subject = "Unexpected shutdown",
                Source = $"Event 6008 (#{e.RecordId})",
                Facts = [$"Windows recorded that the shutdown at about {e.When.LocalDateTime:g} was unexpected."],
                LikelyCauses = ["A power loss, a hard reset, a thermal shutdown, or a crash that happened before Windows could write a bugcheck."],
                Confidence = CrashConfidence.Low,
                Remediation = ["If it recurs, check power (cable, PSU, battery), temperatures, and look for a matching bugcheck around the same time."],
                Missing = ["a bugcheck record or dump. None was written for this event."],
            };

        var facts = new List<string>();
        var causes = new List<string>();
        var remedy = new List<string>();
        var missing = new List<string>();
        string subject = e.BugcheckCode is { } c ? BugcheckCatalog.Describe(c) : "Kernel stop error";
        string? culprit = null;
        var conf = CrashConfidence.Low;

        if (e.BugcheckCode is { } code)
        {
            facts.Add($"Stop code 0x{code:X}" + (e.BugcheckParams is { Length: > 0 } p ? $" ({p})" : "") + ".");
            if (BugcheckCatalog.TryGet(code, out var info))
            {
                facts.Add($"{info.Name}: {info.Meaning}");
                causes.Add(info.CommonCauses);
                conf = CrashConfidence.Moderate;

                if (code == 0x124)
                {
                    remedy.Add("This is a hardware-reported error, not a software bug. Test your RAM (Repair > Memory test), check CPU/GPU temperatures, and remove any overclock or XMP/EXPO profile to see if it stops.");
                    conf = CrashConfidence.Moderate;
                }
                else if (code is 0xEF or 0xF4)
                {
                    remedy.Add("Run 'Scan & repair system files (SFC)' then 'Repair Windows image (DISM RestoreHealth)' on the Repair page. If you have used an aggressive debloat tool, some removals can trigger this.");
                }
                else if (code is 0x116 or 0x117 or 0x119)
                {
                    culprit = "the display driver";
                    remedy.Add("Update the GPU driver; a clean reinstall (uninstall in Safe Mode / DDU, then the latest from NVIDIA / AMD / Intel) often fixes it. Check GPU temperatures and remove any GPU overclock.");
                }
                else
                {
                    remedy.Add("Update your device drivers (chipset, storage, network, GPU) from the vendor, and install pending Windows updates.");
                }
            }
            else
            {
                facts.Add("This stop code is not in PowerX's catalogue.");
                missing.Add($"a description of stop code 0x{code:X}");
            }
        }
        else
        {
            missing.Add("the stop code. It could not be read from the event.");
        }

        if (e.DumpPath is { } dump && File.Exists(dump))
        {
            facts.Add($"A dump was saved at {dump}.");
            missing.Add("a kernel-dump analysis. PowerX does not parse kernel dumps on purpose (that needs a debugger engine and symbols). Run WinDbg and `!analyze -v` to get the driver name.");
        }
        else
        {
            missing.Add("the memory dump. Nothing was found where Windows said it saved one.");
        }

        return new CrashInsight
        {
            When = e.When, Kind = CrashKind.Bugcheck, Subject = subject, Culprit = culprit,
            Facts = facts, LikelyCauses = causes, Confidence = conf,
            Remediation = remedy.Count > 0 ? remedy : ["If this was a one-off, no action is needed. If it recurs, update drivers and run SFC/DISM."],
            Missing = missing,
            ArtifactPath = e.DumpPath, Source = $"Event 1001 · System (#{e.RecordId})",
        };
    }

    private static CrashInsight FromWer(WerReport w, EventCrashRecord? managed, ScanOptions opt)
    {
        var facts = new List<string>();
        var causes = new List<string>();
        var remedy = new List<string>();
        var missing = new List<string>();

        string app = w.AppName ?? Path.GetFileName(w.AppPath ?? "") ?? "an application";
        string subject = w.AppVersion is { Length: > 0 } v ? $"{app} {v}" : app;
        bool hang = w.EventType.StartsWith("AppHang", StringComparison.OrdinalIgnoreCase);
        string? culprit = null;
        var conf = CrashConfidence.Low;

        facts.Add(hang
            ? $"{app} stopped responding (WER event type {w.EventType})."
            : $"{app} crashed (WER event type {w.EventType}).");

        if (w.ExceptionCode is { } ec)
            facts.Add($"Exception code 0x{ec}: {DescribeException(ec)}.");

        if (w.FaultModule is { } fm)
        {
            facts.Add($"The fault was in {fm}" + (w.FaultModuleVersion is { Length: > 0 } fmv ? $" (version {fmv})." : "."));
            if (GraphicsDrivers.Contains(fm))
            {
                culprit = fm;
                causes.Add("A display-driver fault. The application is often not at fault.");
                remedy.Add("Update your GPU driver (a clean reinstall with DDU often helps). Check GPU temperatures and remove any GPU overclock.");
                conf = CrashConfidence.High;
            }
            else if (SystemMessengers.Contains(fm))
            {
                causes.Add($"{fm} is a Windows component that is usually just where the crash surfaced, not the cause. The real fault is in {app} itself or a plug-in it loaded.");
                conf = CrashConfidence.Moderate;
            }
            else if (fm.Equals(Path.GetFileName(w.AppPath ?? ""), StringComparison.OrdinalIgnoreCase))
            {
                causes.Add($"The fault is in {app}'s own code.");
                conf = CrashConfidence.Moderate;
            }
            else
            {
                culprit = fm;
                causes.Add($"A third-party component, {fm}, was executing when the crash happened. If it belongs to a driver, overlay or security product, that is the thing to update or remove.");
                conf = CrashConfidence.Moderate;
            }
        }
        else if (!hang)
        {
            missing.Add("the faulting module. WER did not record one.");
        }

        if (managed is { ManagedExceptionType: { Length: > 0 } met })
        {
            facts.Add($"Unhandled .NET exception: {met}.");
            foreach (var f in managed.ManagedStackTop) facts.Add($"  {f}");
            conf = CrashConfidence.High;
            remedy.Add($"This is a bug in {app} (or a library it uses). Check for an update; report it with the exception type and stack above.");
        }

        MinidumpSummary? dump = null;
        if (opt.ReadDumps && w.MinidumpPath is { } mp && File.Exists(mp))
        {
            dump = MinidumpReader.Read(mp);
            if (dump.Ok)
            {
                if (dump.FaultingModule is { } fmod)
                {
                    facts.Add($"The minidump puts the faulting instruction in {fmod.Name}" +
                              (fmod.Version is { } mv ? $" (version {mv})." : "."));
                    if (culprit is null && !SystemMessengers.Contains(fmod.Name)) culprit = fmod.Name;
                    if (GraphicsDrivers.Contains(fmod.Name)) { conf = CrashConfidence.High; culprit = fmod.Name; }
                }
                missing.Add($"symbols for {culprit ?? "the faulting module"}. Without them PowerX can name the module but not the function or line.");
            }
            else
            {
                missing.Add($"a readable minidump ({dump.Error})");
            }
        }
        else if (w.MinidumpPath is null)
        {
            missing.Add("a minidump. WER did not keep one for this crash.");
        }

        if (hang && remedy.Count == 0)
            remedy.Add($"If {app} hangs repeatedly, update it, disable its add-ons/overlays one at a time, and check for a driver (especially storage or GPU) update.");
        if (remedy.Count == 0)
            remedy.Add($"If {app} keeps crashing: update it, disable overlays / injectors (RivaTuner, Discord, anti-cheat), and run SFC/DISM. If it started after a Windows or driver update, roll that back.");

        return new CrashInsight
        {
            When = w.When,
            Kind = hang ? CrashKind.AppHang : (managed is not null ? CrashKind.ManagedException : CrashKind.AppCrash),
            Subject = subject, Culprit = culprit,
            Facts = facts, LikelyCauses = causes, Confidence = conf, Remediation = remedy, Missing = missing,
            ArtifactPath = w.ReportFolder,
            Source = $"WER · {Path.GetFileName(w.ReportFolder)}",
        };
    }

    private static CrashInsight FromAppEvent(EventCrashRecord e, EventCrashRecord? managed)
    {
        var facts = new List<string> { $"{e.App ?? "An application"} " + (e.Kind == EventCrashKind.AppHang ? "stopped responding" : "crashed") + $" at about {e.When.LocalDateTime:g}." };
        var missing = new List<string> { "the WER report or dump. Windows logged the event but kept no report folder." };
        var conf = CrashConfidence.Low;
        string? culprit = e.FaultModule;

        if (e.ExceptionCode is { } ec) facts.Add($"Exception code {ec}: {DescribeException(ec.TrimStart('0','x'))}.");

        var causes = new List<string>();
        if (e.FaultModule is { } fm)
        {
            facts.Add($"Faulting module: {fm}.");
            if (GraphicsDrivers.Contains(fm))
            {
                causes.Add($"A display-driver fault ({fm}). The app is often not to blame.");
                conf = CrashConfidence.Moderate;
            }
            else if (fm is "Microsoft.UI.Xaml.dll" or "Windows.UI.Xaml.dll")
            {
                causes.Add($"{fm} is where a WinUI / UWP app surfaces XAML and rendering failures. The cause is usually the app itself, a missing runtime dependency (e.g. the Visual C++ runtime), or a corrupt install. Not this DLL itself.");
                conf = CrashConfidence.Moderate;
            }
            else if (SystemMessengers.Contains(fm))
            {
                causes.Add($"{fm} is a Windows component that is usually just where the crash surfaced. The real fault is in {e.App ?? "the app"} or something it loaded.");
                conf = CrashConfidence.Moderate;
            }
            else
            {
                causes.Add($"{fm} was executing when it crashed. If it belongs to a driver, overlay or security product, that is what to update or remove.");
                conf = CrashConfidence.Moderate;
            }
        }
        if (managed is { ManagedExceptionType: { Length: > 0 } met })
        {
            facts.Add($"Unhandled .NET exception: {met}.");
            foreach (var f in managed.ManagedStackTop) facts.Add($"  {f}");
            conf = CrashConfidence.High;
        }

        if (causes.Count == 0) causes.Add("Not enough was recorded to point at a cause.");

        return new CrashInsight
        {
            When = e.When, Kind = e.Kind == EventCrashKind.AppHang ? CrashKind.AppHang : CrashKind.AppCrash,
            Subject = e.App is { Length: > 0 } a ? (e.AppVersion is { Length: > 0 } v ? $"{a} {v}" : a) : "Application crash",
            Culprit = culprit, Facts = facts, Confidence = conf, Missing = missing,
            LikelyCauses = causes,
            Remediation = ["Update the app and your drivers; disable overlays and injectors; run SFC/DISM if it recurs."],
            Source = $"Event {(e.Kind == EventCrashKind.AppHang ? 1002 : 1000)} (#{e.RecordId})",
        };
    }

    private static CrashInsight FromDotNetOnly(EventCrashRecord d)
    {
        var facts = new List<string> { $"{d.App ?? "A .NET application"} ended with an unhandled exception at about {d.When.LocalDateTime:g}." };
        if (d.ManagedExceptionType is { Length: > 0 } met) facts.Add($"Exception: {met}.");
        foreach (var f in d.ManagedStackTop) facts.Add($"  {f}");

        return new CrashInsight
        {
            When = d.When, Kind = CrashKind.ManagedException,
            Subject = d.App is { Length: > 0 } a ? a : ".NET application crash",
            Facts = facts,
            LikelyCauses = ["A bug in the app or a library it loaded. The exception type and stack above point at it."],
            Confidence = d.ManagedStackTop.Count > 0 ? CrashConfidence.High : CrashConfidence.Moderate,
            Remediation = ["Check for an application update. If you build it, the stack names the throwing method."],
            Missing = d.ManagedStackTop.Count > 0 ? [] : ["the exception stack. The event recorded only the type."],
            Source = $"Event 1026 (#{d.RecordId})",
        };
    }

    // ---------------------------------------------------------------- helpers

    private static bool IsAppEvent(string t) =>
        t.StartsWith("APPCRASH", StringComparison.OrdinalIgnoreCase) ||
        t.StartsWith("AppHang", StringComparison.OrdinalIgnoreCase) ||
        t.StartsWith("BEX", StringComparison.OrdinalIgnoreCase) ||
        t.StartsWith("CLR20", StringComparison.OrdinalIgnoreCase) ||
        t.StartsWith("MoAppCrash", StringComparison.OrdinalIgnoreCase);

    private static bool Near(DateTimeOffset a, DateTimeOffset b) => Math.Abs((a - b).TotalMinutes) <= 3;

    private static bool SameApp(string? a, string? b)
    {
        if (a is null || b is null) return false;
        a = Path.GetFileName(a); b = Path.GetFileName(b);
        return a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset Round(DateTimeOffset t) =>
        new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Offset);

    internal static string DescribeException(string code) => code.TrimStart('0', 'x', 'X').ToLowerInvariant() switch
    {
        "c0000005" => "access violation (read or write to memory it doesn't own)",
        "c0000374" => "heap corruption",
        "c0000409" => "stack buffer overrun / fast-fail security check",
        "c00000fd" => "stack overflow",
        "c000027b" => "an unhandled WinRT / C++ exception (a 'stowed' exception)",
        "e0434352" => "an unhandled .NET exception",
        "e06d7363" => "an unhandled C++ exception",
        "80000003" => "a breakpoint (often a debug assertion)",
        "c0000094" => "integer divide by zero",
        "c0000096" => "a privileged instruction",
        "c0000420" => "an assertion failure",
        _ => "see Microsoft's NTSTATUS reference for this code",
    };
}

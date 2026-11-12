using System.Diagnostics;
using PowerX.Core.Processes;

namespace PowerX.Core.Diagnostics;

/// <summary>
/// Small, safe, common maintenance actions surfaced on Home. Each is a single well-understood
/// operation with a clear effect — nothing here is destructive or hard to reverse.
/// </summary>
public static partial class QuickActions
{
    public static ActionResult RestartExplorer()
    {
        try
        {
            // Only this session's shell. PowerX runs elevated, so GetProcessesByName also returns
            // the Explorer of every other signed-in user, and killing those is not ours to do.
            int session;
            using (var self = Process.GetCurrentProcess()) session = self.SessionId;
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                using (p)
                {
                    try
                    {
                        if (p.SessionId == session) p.Kill();
                    }
                    catch (Exception) { /* already gone */ }
                }
            }
            // Windows restarts the shell on its own (Winlogon's AutoRestartShell), usually within
            // a second or two. Only start one ourselves if it has not come back: PowerX is
            // elevated, so an Explorer it starts the plain way would be an elevated desktop, and
            // everything opened from the taskbar afterwards would run as administrator too.
            for (int waited = 0; waited < 6000 && !ShellRunningIn(session); waited += 250)
                Thread.Sleep(250);
            if (!ShellRunningIn(session))
            {
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                if (!ShellLauncher.StartUnelevated(explorer, "").Success)
                    Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = true });   // a desktop beats none
            }
            return ActionResult.Ok;
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }

    private static bool ShellRunningIn(int session)
    {
        bool found = false;
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            using (p)
            {
                try { found |= p.SessionId == session; } catch (Exception) { /* exited */ }
            }
        }
        return found;
    }

    public static ActionResult FlushDns() => RunHidden("ipconfig.exe", "/flushdns");

    public static ActionResult EmptyRecycleBin()
    {
        try
        {
            int hr = SHEmptyRecycleBinW(0, null, 0x1 | 0x2 | 0x4); // no confirm, no progress, no sound
            return hr is 0 or unchecked((int)0x8000FFFF) ? ActionResult.Ok : ActionResult.Fail($"HRESULT 0x{hr:X8}");
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }

    public static ActionResult OpenSettings(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return ActionResult.Ok;
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }

    private static ActionResult RunHidden(string file, string args)
    {
        var r = ProcessRunner.Run(file, args, 10_000);
        if (r.Ok) return ActionResult.Ok;
        string detail = string.IsNullOrWhiteSpace(r.Output)
            ? (r.Exited ? $"exited {r.ExitCode}" : "did not complete")
            : r.Output.Trim();
        return ActionResult.Fail($"{Path.GetFileNameWithoutExtension(file)} {detail}");
    }

    [System.Runtime.InteropServices.LibraryImport("shell32.dll", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinW(nint hwnd, string? rootPath, uint flags);
}

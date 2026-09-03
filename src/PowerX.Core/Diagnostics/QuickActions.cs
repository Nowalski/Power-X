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
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch (Exception) { /* already gone */ }
            }
            // Windows normally auto-restarts the shell; force it if it did not within a moment.
            Thread.Sleep(400);
            if (Process.GetProcessesByName("explorer").Length == 0)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            return ActionResult.Ok;
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
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

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PowerX.Core.Interop;

namespace PowerX.Core.Processes;

public enum ProcessPriority
{
    Idle,
    BelowNormal,
    Normal,
    AboveNormal,
    High,
    Realtime,
}

public sealed record ActionResult(bool Success, string? Message = null)
{
    public static readonly ActionResult Ok = new(true);
    public static ActionResult Fail(string m) => new(false, m);
}

/// <summary>
/// Structured, whitelisted process operations. Every call opens the minimum handle it needs,
/// acts, and closes it — nothing is cached. Denials surface as <see cref="ActionResult"/>,
/// never exceptions across the boundary.
/// </summary>
public static class ProcessActions
{
    private static readonly Dictionary<ProcessPriority, uint> PriorityClass = new()
    {
        [ProcessPriority.Idle] = 0x00000040,
        [ProcessPriority.BelowNormal] = 0x00004000,
        [ProcessPriority.Normal] = 0x00000020,
        [ProcessPriority.AboveNormal] = 0x00008000,
        [ProcessPriority.High] = 0x00000080,
        [ProcessPriority.Realtime] = 0x00000100,
    };

    public static ActionResult EndTask(int pid) => WithHandle(pid, ProcessNative.PROCESS_TERMINATE, h =>
        ProcessNative.TerminateProcess(h, 0)
            ? ActionResult.Ok
            : ActionResult.Fail(Win32("End task")));

    /// <summary>Terminate the process and every descendant, leaves-first.</summary>
    public static ActionResult EndTaskTree(int pid, ProcessSnapshot snapshot)
    {
        var order = new List<int>();
        Collect(pid);
        order.Reverse();
        var failures = 0;
        foreach (var p in order)
            if (!EndTask(p).Success) failures++;
        return failures == 0 ? ActionResult.Ok : ActionResult.Fail($"{failures} process(es) could not be ended");

        void Collect(int parent)
        {
            order.Add(parent);
            foreach (var child in snapshot.Processes.Where(x => x.ParentPid == parent && x.Pid != parent))
                Collect(child.Pid);
        }
    }

    public static ActionResult Suspend(int pid) => WithHandle(pid, ProcessNative.PROCESS_SUSPEND_RESUME, h =>
        ProcessNative.NtSuspendProcess(h) == 0 ? ActionResult.Ok : ActionResult.Fail("Suspend failed"));

    public static ActionResult Resume(int pid) => WithHandle(pid, ProcessNative.PROCESS_SUSPEND_RESUME, h =>
        ProcessNative.NtResumeProcess(h) == 0 ? ActionResult.Ok : ActionResult.Fail("Resume failed"));

    public static ActionResult SetPriority(int pid, ProcessPriority priority) =>
        WithHandle(pid, ProcessNative.PROCESS_SET_INFORMATION, h =>
            ProcessNative.SetPriorityClass(h, PriorityClass[priority])
                ? ActionResult.Ok
                : ActionResult.Fail(Win32("Set priority")));

    // Efficiency mode forces Idle priority (like Task Manager). Remember the class the process
    // was at when it was turned on, so turning it off restores the user's real choice instead of
    // flattening every process to Normal.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, uint> PriorityBeforeEco = new();

    /// <summary>Toggle EcoQoS (Task Manager's "Efficiency mode").</summary>
    public static ActionResult SetEfficiencyMode(int pid, bool enabled) =>
        WithHandle(pid, ProcessNative.PROCESS_SET_INFORMATION | ProcessNative.PROCESS_QUERY_LIMITED_INFORMATION, h =>
        {
            uint priorBefore = enabled ? ProcessNative.GetPriorityClass(h) : 0;

            var state = new ProcessNative.PROCESS_POWER_THROTTLING_STATE
            {
                Version = ProcessNative.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = ProcessNative.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = enabled ? ProcessNative.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0,
            };
            bool ok = ProcessNative.SetProcessInformation(
                h, ProcessNative.ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf(state));
            if (!ok) return ActionResult.Fail(Win32("Efficiency mode"));

            uint idle = PriorityClass[ProcessPriority.Idle];
            if (enabled)
            {
                if (priorBefore != 0 && priorBefore != idle) PriorityBeforeEco[pid] = priorBefore;
                ProcessNative.SetPriorityClass(h, idle);
            }
            else
            {
                // restore what the user had; fall back to Normal only if we never recorded it
                uint restore = PriorityBeforeEco.TryRemove(pid, out var saved) && saved != 0
                    ? saved
                    : PriorityClass[ProcessPriority.Normal];
                ProcessNative.SetPriorityClass(h, restore);
            }
            return ActionResult.Ok;
        });

    public static ActionResult OpenFileLocation(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            return ActionResult.Fail("Executable path is not available.");
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{imagePath}\"") { UseShellExecute = true });
            return ActionResult.Ok;
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }

    public static ActionResult SearchOnline(string name)
    {
        try
        {
            Process.Start(new ProcessStartInfo($"https://www.google.com/search?q={Uri.EscapeDataString(name + " process")}")
            { UseShellExecute = true });
            return ActionResult.Ok;
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }

    private static ActionResult WithHandle(int pid, uint access, Func<nint, ActionResult> body)
    {
        if (pid <= 4) return ActionResult.Fail("This is a protected system process.");
        nint h = ProcessNative.OpenProcess(access, false, (uint)pid);
        if (h == 0)
        {
            int err = Marshal.GetLastPInvokeError();
            return ActionResult.Fail(err == 5
                ? "Windows denied access (the process may be protected or require elevation)."
                : new Win32Exception(err).Message);
        }
        try
        {
            return body(h);
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
        finally
        {
            ProcessNative.CloseHandle(h);
        }
    }

    private static string Win32(string what) => $"{what} failed: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}";
}

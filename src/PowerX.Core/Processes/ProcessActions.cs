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

    /// <summary>
    /// Whether Windows marks the process as critical. Ending a critical process stops the whole
    /// PC with a CRITICAL_PROCESS_DIED bugcheck, and PowerX runs elevated, so for some of them
    /// TerminateProcess would actually succeed. This only reports it; the caller decides how
    /// loudly to warn. PIDs 0 (Idle) and 4 (System) count as critical. A process that cannot be
    /// opened, or a query that fails, reports false: query-limited is the weakest access there is,
    /// so a process PowerX cannot open for that is in practice one it cannot terminate either.
    /// </summary>
    public static bool IsCritical(int pid)
    {
        if (pid <= 4) return true;
        nint h = ProcessNative.OpenProcess(ProcessNative.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == 0) return false;
        try
        {
            return ProcessNative.IsProcessCritical(h, out bool critical) && critical;
        }
        finally
        {
            ProcessNative.CloseHandle(h);
        }
    }

    /// <summary>
    /// The process and every descendant in <paramref name="snapshot"/>, leaves first: every child
    /// comes before its parent, which is the order <see cref="EndTaskTree"/> ends them in.
    ///
    /// PowerX itself is never in the list, and neither is anything below it. End process tree on
    /// the Explorer that started PowerX would otherwise end PowerX halfway through the list and
    /// leave the rest of the tree running. Anything below PowerX is something PowerX started and
    /// may still be waiting on (a repair job, for one), so it is left alone as well.
    /// </summary>
    public static IReadOnlyList<int> TreePids(int pid, ProcessSnapshot snapshot)
    {
        int self = Environment.ProcessId;
        var order = new List<int>();
        var seen = new HashSet<int>();
        Collect(pid);
        order.Reverse();
        return order;

        void Collect(int parent)
        {
            if (parent == self) return;
            // PIDs are reused, so a stale parent PID can make the "tree" loop back on itself.
            if (!seen.Add(parent)) return;
            order.Add(parent);
            foreach (var child in snapshot.Processes.Where(x => x.ParentPid == parent && x.Pid != parent))
                Collect(child.Pid);
        }
    }

    /// <summary>
    /// Whether any process <see cref="EndTaskTree"/> would end is critical (see
    /// <see cref="IsCritical"/>), so the caller can warn before the whole PC goes down with it.
    /// </summary>
    public static bool TreeHasCritical(int pid, ProcessSnapshot snapshot) =>
        TreePids(pid, snapshot).Any(IsCritical);

    /// <summary>Terminate the process and every descendant, leaves-first (see <see cref="TreePids"/>).</summary>
    public static ActionResult EndTaskTree(int pid, ProcessSnapshot snapshot)
    {
        var order = TreePids(pid, snapshot);
        if (order.Count == 0)
            return ActionResult.Fail("This is PowerX itself. Close PowerX instead of ending its process tree.");

        var failures = 0;
        foreach (var p in order)
            if (!EndTask(p).Success) failures++;
        return failures == 0 ? ActionResult.Ok : ActionResult.Fail(failures == 1 ? "1 process could not be ended" : $"{failures} processes could not be ended");
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

    /// <summary>Search the web for the process name, in an unelevated browser.</summary>
    public static ActionResult SearchOnline(string name) =>
        ShellLauncher.Open($"https://www.google.com/search?q={Uri.EscapeDataString(name + " process")}");

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

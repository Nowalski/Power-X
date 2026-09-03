using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PowerX.Core.Interop;

namespace PowerX.Core.Processes;

/// <summary>
/// Enumerates every process in a single <c>NtQuerySystemInformation(SystemProcessInformation)</c>
/// call and derives per-process CPU% / I/O rate from deltas against the previous pass.
/// Stateful: keep one instance and call <see cref="Enumerate"/> on a cadence.
/// </summary>
public sealed class ProcessProvider
{
    private readonly ILogger _log;
    private readonly int _logicalCount;
    private Dictionary<int, (long Cpu100Ns, long IoBytes, DateTimeOffset At)> _prev = new();

    public ProcessProvider(ILogger<ProcessProvider>? log = null)
    {
        _log = log ?? NullLogger<ProcessProvider>.Instance;
        _logicalCount = Environment.ProcessorCount;
    }

    ~ProcessProvider()
    {
        if (_buffer != 0) Marshal.FreeHGlobal(_buffer);
    }

    // Reused across calls to avoid a fresh 400-entry allocation every second.
    private nint _buffer;
    private int _bufferSize;

    public unsafe ProcessSnapshot Enumerate()
    {
        var now = DateTimeOffset.UtcNow;
        if (_buffer == 0) { _bufferSize = 512 * 1024; _buffer = Marshal.AllocHGlobal(_bufferSize); }
        nint buffer = _buffer;
        uint size = (uint)_bufferSize;
        try
        {
            int status;
            while ((status = NtDll.NtQuerySystemInformation(
                       NtDll.SystemProcessInformation, buffer, size, out uint needed)) == unchecked((int)0xC0000004))
            {
                Marshal.FreeHGlobal(buffer);
                size = needed + (64 * 1024);
                buffer = _buffer = Marshal.AllocHGlobal((int)size);
                _bufferSize = (int)size;
            }

            if (status != 0)
            {
                _log.LogWarning("NtQuerySystemInformation returned 0x{Status:X8}", status);
                return new ProcessSnapshot([], now, 0, 0);
            }

            var list = new List<ProcessInfo>(400);
            var next = new Dictionary<int, (long, long, DateTimeOffset)>(400);
            byte* p = (byte*)buffer;
            int totalThreads = 0;

            while (true)
            {
                ref readonly var e = ref *(NtDll.SYSTEM_PROCESS_INFORMATION*)p;
                int pid = (int)e.UniqueProcessId;

                // A pid can, very rarely, appear twice in one walk (exit + reuse mid-snapshot).
                // Keep the first; skip the rest so downstream pid-keyed maps stay valid.
                if (next.ContainsKey(pid))
                {
                    if (e.NextEntryOffset == 0) break;
                    p += e.NextEntryOffset;
                    continue;
                }

                long cpu100Ns = e.KernelTime + e.UserTime;
                long ioBytes = e.ReadTransferCount + e.WriteTransferCount + e.OtherTransferCount;

                double cpuPercent = 0;
                double ioRate = 0;
                if (_prev.TryGetValue(pid, out var prev))
                {
                    double secs = (now - prev.At).TotalSeconds;
                    if (secs > 0)
                    {
                        double cpuSecs = (cpu100Ns - prev.Cpu100Ns) / 1e7;
                        cpuPercent = Math.Clamp(100.0 * cpuSecs / (secs * _logicalCount), 0, 100);
                        ioRate = Math.Max(0, (ioBytes - prev.IoBytes) / secs);
                    }
                }
                next[pid] = (cpu100Ns, ioBytes, now);

                string name = e.ImageNameLength > 0 && e.ImageNameBuffer != 0
                    ? new string((char*)e.ImageNameBuffer, 0, e.ImageNameLength / 2)
                    : pid == 0 ? "System Idle Process" : "System";

                totalThreads += (int)e.NumberOfThreads;

                list.Add(new ProcessInfo
                {
                    Pid = pid,
                    ParentPid = (int)e.InheritedFromUniqueProcessId,
                    Name = name,
                    SessionId = (int)e.SessionId,
                    ThreadCount = (int)e.NumberOfThreads,
                    HandleCount = (int)e.HandleCount,
                    BasePriority = e.BasePriority,
                    CpuPercent = cpuPercent,
                    WorkingSetBytes = e.WorkingSetSize,
                    PrivateBytes = e.PrivatePageCount,
                    IoBytesPerSec = ioRate,
                    HardFaultDelta = e.HardFaultCount,
                    StartTime = e.CreateTime > 0
                        ? DateTimeOffset.FromFileTime(e.CreateTime)
                        : null,
                    TotalProcessorTime = TimeSpan.FromTicks(cpu100Ns),
                });

                if (e.NextEntryOffset == 0) break;
                p += e.NextEntryOffset;
            }

            _prev = next;
            return new ProcessSnapshot(list, now, list.Count, totalThreads);
        }
        catch
        {
            // a growth realloc may have thrown — drop the buffer so the next call starts clean
            if (_buffer != 0) { Marshal.FreeHGlobal(_buffer); _buffer = 0; _bufferSize = 0; }
            throw;
        }
    }

    /// <summary>Build a parent→children map for tree views. Roots are entries whose parent is absent.</summary>
    public static IReadOnlyDictionary<int, List<ProcessInfo>> BuildTree(ProcessSnapshot snapshot)
    {
        var byParent = new Dictionary<int, List<ProcessInfo>>();
        var known = snapshot.Processes.Select(x => x.Pid).ToHashSet();
        foreach (var proc in snapshot.Processes)
        {
            int parent = known.Contains(proc.ParentPid) && proc.ParentPid != proc.Pid ? proc.ParentPid : -1;
            (byParent.TryGetValue(parent, out var kids) ? kids : byParent[parent] = []).Add(proc);
        }
        return byParent;
    }
}

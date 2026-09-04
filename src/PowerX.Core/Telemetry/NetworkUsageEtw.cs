using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace PowerX.Core.Telemetry;

/// <summary>Cumulative network bytes attributed to one process since the trace started.</summary>
public sealed record ProcessNetTotals(int Pid, long BytesSent, long BytesReceived);

/// <summary>
/// Per-process network byte counts from a private ETW real-time session on
/// <c>Microsoft-Windows-Kernel-Network</c>. Attributing throughput to a process needs ETW; this
/// needs administrator rights. If the session cannot start, <see cref="Running"/> stays false and
/// callers fall back to the connection list with no rates. Read-only: it subscribes to counters,
/// it never touches the network.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NetworkUsageEtw : IDisposable
{
    private const string SessionName = "PowerX-KernelNetwork";
    private static readonly Guid KernelNetwork = new("7dd42a49-5329-4832-8dfd-43d979153a88");

    // TCP v4 send/recv, TCP v6 send/recv, UDP v4 send/recv, UDP v6 send/recv
    private static readonly HashSet<int> SendIds = [10, 26, 42, 58];
    private static readonly HashSet<int> RecvIds = [11, 27, 43, 59];

    private readonly ConcurrentDictionary<int, long[]> _totals = new();   // pid -> [sent, recv]
    private TraceEventSession? _session;
    private Thread? _pump;

    public bool Running { get; private set; }

    public bool Start()
    {
        if (Running) return true;
        try
        {
            // Clear a session a previous crash may have left behind.
            try { TraceEventSession.GetActiveSession(SessionName)?.Dispose(); } catch { }

            _session = new TraceEventSession(SessionName, TraceEventSessionOptions.Create)
            {
                StopOnDispose = true,
                BufferSizeMB = 16,
            };
            _session.EnableProvider(KernelNetwork, TraceEventLevel.Informational);

            _session.Source.AllEvents += OnEvent;

            _pump = new Thread(() => { try { _session.Source.Process(); } catch (Exception) { } })
            {
                IsBackground = true,
                Name = "PowerX-ETW",
            };
            _pump.Start();
            Running = true;
            return true;
        }
        catch (Exception)
        {
            Cleanup();
            return false;
        }
    }

    private void OnEvent(TraceEvent data)
    {
        try
        {
            int id = (int)data.ID;
            bool send = SendIds.Contains(id);
            if (!send && !RecvIds.Contains(id)) return;

            // Microsoft-Windows-Kernel-Network's TCP/UDP send/recv events are logged in the kernel's
            // own context, so TraceEvent's own ProcessID/PayloadByName do not resolve to the owning
            // process (ProcessID here is usually 4 = System, and the manifest fields do not surface
            // through the generic parser). The owning PID and the byte count are, in every build
            // observed, the first two UInt32 fields of the raw payload (PID, then size) — this is
            // the same layout WPA's own network-usage view reads. Read them directly.
            var raw = data.EventData();
            if (raw.Length < 8) return;
            int pid = BitConverter.ToInt32(raw, 0);
            long size = BitConverter.ToUInt32(raw, 4);
            if (pid <= 0 || size <= 0) return;

            var slot = _totals.GetOrAdd(pid, static _ => new long[2]);
            Interlocked.Add(ref slot[send ? 0 : 1], size);
        }
        catch (Exception) { }
    }

    /// <summary>Cumulative totals per process since the trace started.</summary>
    public IReadOnlyList<ProcessNetTotals> Totals() =>
        _totals.Select(kv => new ProcessNetTotals(kv.Key,
                Interlocked.Read(ref kv.Value[0]), Interlocked.Read(ref kv.Value[1])))
            .ToList();

    public void Stop() => Cleanup();

    private void Cleanup()
    {
        Running = false;
        try { _session?.Dispose(); } catch { }
        _session = null;
        _pump = null;
        _totals.Clear();
    }

    public void Dispose() => Cleanup();
}

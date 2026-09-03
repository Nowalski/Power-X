using Microsoft.UI.Dispatching;
using PowerX.Core.Diagnostics;
using PowerX.Core.Processes;
using PowerX.Core.Telemetry;

namespace PowerX.App.Services;

/// <summary>
/// One process-wide sampler. Runs for the lifetime of the app so history stays warm — a page
/// that navigates in gets a full graph immediately. The expensive Win32 / PDH / WMI calls run on
/// a background loop; only the cheap commit (store results, push history, raise <see cref="Updated"/>)
/// is marshalled to the UI thread, so sampling never stutters the UI. Default cadence is
/// <see cref="Interval"/> (1 s), backing off to <see cref="BackgroundInterval"/> when the window
/// is hidden. Nothing queries Windows faster than this.
/// </summary>
public sealed class TelemetryHub
{
    public const int HistoryCapacity = 300;

    public static TelemetryHub Instance { get; } = new();

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan BackgroundInterval { get; set; } = TimeSpan.FromSeconds(5);

    private readonly CpuMetricsProvider _cpu = new();
    private readonly MemoryMetricsProvider _mem = new();
    private readonly ProcessProvider _proc = new();
    private readonly GpuMetricsProvider _gpu = new();
    private readonly NetworkMetricsProvider _net = new();

    private DispatcherQueue? _ui;
    private CancellationTokenSource? _loop;
    private volatile bool _active = true;

    public ProviderResult<CpuMetrics>? LastCpu { get; private set; }
    public ProviderResult<MemoryMetrics>? LastMemory { get; private set; }
    public ProcessSnapshot? LastProcesses { get; private set; }
    public ProviderResult<GpuMetrics>? LastGpu { get; private set; }
    public ProviderResult<NetworkMetrics>? LastNetwork { get; private set; }

    // Warm history — filled every tick, read by charts on navigate so they open full.
    public MetricRing CpuHistory { get; } = new(HistoryCapacity);
    public MetricRing MemHistory { get; } = new(HistoryCapacity);
    public MetricRing GpuHistory { get; } = new(HistoryCapacity);
    public MetricRing NetDownHistory { get; } = new(HistoryCapacity);
    public MetricRing NetUpHistory { get; } = new(HistoryCapacity);

    private IReadOnlyList<GpuAdapter>? _gpuAdapters;
    public IReadOnlyList<GpuAdapter> GpuAdapters => _gpuAdapters ??= GpuMetricsProvider.QueryAdapters();

    public CpuInfo CpuInfo { get; } = SafeCpuInfo();

    private Task<MemoryHardware>? _memHardware;
    public Task<MemoryHardware> GetMemoryHardwareAsync() => _memHardware ??= Task.Run(MemoryHardware.Query);

    private static CpuInfo SafeCpuInfo()
    {
        try { return CpuInfo.Query(); }
        catch { return null!; }
    }

    public event EventHandler? Updated;

    public bool Active
    {
        get => _active;
        set => _active = value;
    }

    /// <summary>Start the sampler. Called once from the main window; safe to call again.</summary>
    public void Start()
    {
        if (_loop is not null) return;
        _ui = DispatcherQueue.GetForCurrentThread();
        _loop = new CancellationTokenSource();
        _ = Task.Run(() => RunAsync(_loop.Token));
    }

    /// <summary>Stop sampling and release the GPU PDH query. Called when the main window closes.</summary>
    public void Shutdown()
    {
        _loop?.Cancel();
        try { _gpu.Dispose(); } catch { /* exiting */ }
    }

    /// <summary>Subscribe to per-tick updates; the returned token unsubscribes. Fires once immediately.</summary>
    public IDisposable Subscribe(EventHandler handler)
    {
        Updated += handler;
        if (LastCpu is not null) handler(this, EventArgs.Empty);
        return new Unsub(this, handler);
    }

    private int _tick;

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            _cpu.Sample();       // prime the CPU delta
            _proc.Enumerate();   // prime per-process CPU/IO deltas

            // When the window is hidden the loop still wakes each second (a bool check + a
            // delay — negligible) but only samples every Nth wake, so a background cadence of
            // ~BackgroundInterval is kept while an alt-tab back resumes fast sampling within 1 s.
            int idleWakes = 0;

            while (!ct.IsCancellationRequested)
            {
                bool active = _active;
                int idleEvery = Math.Max(1, (int)Math.Round(BackgroundInterval / Interval));
                if (active || ++idleWakes >= idleEvery)
                {
                    SampleOnce();
                    idleWakes = 0;
                }
                try { await Task.Delay(active ? Interval : TimeSpan.FromSeconds(1), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
        catch (Exception ex)
        {
            PowerX.App.App.Log("TelemetryHub", ex);
        }
    }

    /// <summary>Runs on the background loop thread. Does the costly sampling, then hands the
    /// results to the UI thread to commit.</summary>
    private void SampleOnce()
    {
        _tick++;
        bool sampleNet = _tick % 2 == 0;   // network enumeration is the priciest part of a tick

        var cpu = _cpu.Sample();
        var mem = _mem.Sample();
        var proc = _proc.Enumerate();
        var gpu = _gpu.Sample();
        var net = sampleNet ? _net.Sample() : null;

        _ui?.TryEnqueue(() => Commit(cpu, mem, proc, gpu, net, sampleNet));
    }

    /// <summary>Runs on the UI thread. Publishes the snapshot and raises <see cref="Updated"/>.</summary>
    private void Commit(
        ProviderResult<CpuMetrics> cpu,
        ProviderResult<MemoryMetrics> mem,
        ProcessSnapshot proc,
        ProviderResult<GpuMetrics> gpu,
        ProviderResult<NetworkMetrics>? net,
        bool sampledNet)
    {
        LastCpu = cpu;
        LastMemory = mem;
        LastProcesses = proc;
        LastGpu = gpu;

        if (sampledNet)
        {
            LastNetwork = net;
            if (net?.Value is { } n)
            {
                NetDownHistory.Seed(n.TotalReceiveBytesPerSec); NetDownHistory.Add(n.TotalReceiveBytesPerSec);
                NetUpHistory.Seed(n.TotalSendBytesPerSec); NetUpHistory.Add(n.TotalSendBytesPerSec);
            }
        }
        else if (LastNetwork?.Value is { } prev)
        {
            NetDownHistory.Add(prev.TotalReceiveBytesPerSec);
            NetUpHistory.Add(prev.TotalSendBytesPerSec);
        }

        // Seed on the first sample so every chart opens already full instead of crawling in.
        if (cpu.Value is { } c) { CpuHistory.Seed(c.TotalUsagePercent); CpuHistory.Add(c.TotalUsagePercent); }
        if (mem.Value is { } m) { MemHistory.Seed(m.UsedPercent); MemHistory.Add(m.UsedPercent); }
        if (gpu.Value is { } g) { GpuHistory.Seed(g.UtilizationPercent); GpuHistory.Add(g.UtilizationPercent); }

        Updated?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Unsub(TelemetryHub hub, EventHandler handler) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            hub.Updated -= handler;
        }
    }
}

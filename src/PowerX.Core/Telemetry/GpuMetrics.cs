namespace PowerX.Core.Telemetry;

public sealed record GpuAdapter
{
    public required string Name { get; init; }
    public string DriverVersion { get; init; } = "";
    public ulong DedicatedMemoryTotal { get; init; }
    public string VideoProcessor { get; init; } = "";
    public (int W, int H) CurrentResolution { get; init; }
    public int RefreshHz { get; init; }
    /// <summary>The LUID DXGI and the <c>\GPU Engine</c> / <c>\GPU Adapter Memory</c> performance
    /// counters use to identify this exact adapter — how live usage in <see cref="GpuMetrics.Adapters"/>
    /// is matched back to this one on a multi-GPU machine. 0 if this entry could not be resolved
    /// via DXGI (falls back to a single blended reading only).</summary>
    public long Luid { get; init; }
}

public sealed record GpuEngineLoad(string Engine, double Percent);

/// <summary>Live usage for one adapter, keyed to the matching entry in <see cref="TelemetryHub.GpuAdapters"/>
/// (or <see cref="GpuMetricsProvider.QueryAdapters"/>) by <see cref="Luid"/>.</summary>
public sealed record GpuAdapterUsage
{
    public required long Luid { get; init; }
    public required string Name { get; init; }
    public required double UtilizationPercent { get; init; }
    public required IReadOnlyList<GpuEngineLoad> Engines { get; init; }
    public required ulong DedicatedMemoryUsed { get; init; }
    public ulong DedicatedMemoryTotal { get; init; }
    public required ulong SharedMemoryUsed { get; init; }
}

public sealed record GpuMetrics
{
    /// <summary>Overall GPU utilisation 0..100 — the busiest engine type across every adapter,
    /// Task-Manager style. On a multi-GPU machine this is a blend; see <see cref="Adapters"/> for
    /// the real per-card breakdown.</summary>
    public required double UtilizationPercent { get; init; }
    public required IReadOnlyList<GpuEngineLoad> Engines { get; init; }
    public required ulong DedicatedMemoryUsed { get; init; }
    public required ulong SharedMemoryUsed { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    /// <summary>Per-adapter breakdown — one entry per real GPU DXGI reports (software/remote
    /// adapters and the Basic Render Driver are excluded). Empty when only one adapter was found
    /// or its LUID could not be correlated, in which case the totals above already cover it.</summary>
    public IReadOnlyList<GpuAdapterUsage> Adapters { get; init; } = [];
}

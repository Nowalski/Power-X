namespace PowerX.Core.Telemetry;

public sealed record GpuAdapter
{
    public required string Name { get; init; }
    public string DriverVersion { get; init; } = "";
    public ulong DedicatedMemoryTotal { get; init; }
    public string VideoProcessor { get; init; } = "";
    public (int W, int H) CurrentResolution { get; init; }
    public int RefreshHz { get; init; }
}

public sealed record GpuEngineLoad(string Engine, double Percent);

public sealed record GpuMetrics
{
    /// <summary>Overall GPU utilisation 0..100 — the busiest engine type, Task-Manager style.</summary>
    public required double UtilizationPercent { get; init; }
    public required IReadOnlyList<GpuEngineLoad> Engines { get; init; }
    public required ulong DedicatedMemoryUsed { get; init; }
    public required ulong SharedMemoryUsed { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

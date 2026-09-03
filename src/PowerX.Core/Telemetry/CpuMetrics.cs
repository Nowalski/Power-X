namespace PowerX.Core.Telemetry;

/// <summary>A single CPU sample. All utilization values are 0..100 percent.</summary>
public sealed record CpuMetrics
{
    public required double TotalUsagePercent { get; init; }

    /// <summary>Kernel-mode share of the total (0..100, subset of <see cref="TotalUsagePercent"/>).</summary>
    public required double KernelUsagePercent { get; init; }

    /// <summary>Per-logical-processor utilization, index = logical processor number.</summary>
    public required IReadOnlyList<double> PerLogicalProcessor { get; init; }

    public required int ProcessCount { get; init; }
    public required int ThreadCount { get; init; }
    public required int HandleCount { get; init; }
    public required TimeSpan Uptime { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>Static CPU topology — queried once, does not change at runtime.</summary>
public sealed record CpuTopology
{
    public required string Name { get; init; }
    public required int PhysicalCores { get; init; }
    public required int LogicalProcessors { get; init; }
    public required int Sockets { get; init; }
    public required bool VirtualizationEnabled { get; init; }
    public required bool IsHybrid { get; init; }
    public int PerformanceCores { get; init; }
    public int EfficiencyCores { get; init; }
    public double MaxClockMhz { get; init; }
}

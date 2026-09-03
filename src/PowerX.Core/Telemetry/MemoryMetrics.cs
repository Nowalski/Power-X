namespace PowerX.Core.Telemetry;

/// <summary>Windows memory accounting. All byte values unless noted.</summary>
public sealed record MemoryMetrics
{
    public required ulong TotalPhysical { get; init; }
    public required ulong AvailablePhysical { get; init; }
    public ulong InUsePhysical => TotalPhysical - AvailablePhysical;
    public required double UsedPercent { get; init; }

    /// <summary>Standby + modified pages backing the file cache.</summary>
    public required ulong CachedApprox { get; init; }

    public required ulong CommitTotal { get; init; }
    public required ulong CommitLimit { get; init; }
    public double CommitPercent => CommitLimit == 0 ? 0 : 100.0 * CommitTotal / CommitLimit;

    public required ulong PagedPool { get; init; }
    public required ulong NonPagedPool { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}

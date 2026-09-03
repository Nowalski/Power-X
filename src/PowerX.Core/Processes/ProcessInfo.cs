namespace PowerX.Core.Processes;

/// <summary>Immutable per-process snapshot row.</summary>
public sealed record ProcessInfo
{
    public required int Pid { get; init; }
    public required int ParentPid { get; init; }
    public required string Name { get; init; }
    public required int SessionId { get; init; }
    public required int ThreadCount { get; init; }
    public required int HandleCount { get; init; }
    public required int BasePriority { get; init; }

    /// <summary>CPU usage 0..100 across all logical processors, since the previous snapshot.</summary>
    public required double CpuPercent { get; init; }

    /// <summary>Private working set-ish figure (WorkingSetSize).</summary>
    public required ulong WorkingSetBytes { get; init; }

    /// <summary>Private committed bytes (PrivatePageCount).</summary>
    public required ulong PrivateBytes { get; init; }

    /// <summary>Bytes/sec of disk+other I/O transfer since the previous snapshot.</summary>
    public required double IoBytesPerSec { get; init; }

    public required long HardFaultDelta { get; init; }
    public required DateTimeOffset? StartTime { get; init; }
    public TimeSpan TotalProcessorTime { get; init; }

    // Enriched lazily (needs a per-process handle) — null until resolved.
    public string? ImagePath { get; init; }
    public string? UserName { get; init; }
    public SignatureStatus Signature { get; init; } = SignatureStatus.Unknown;
}

public enum SignatureStatus
{
    Unknown,
    Unsigned,
    Signed,
    TrustedPublisher,
    MicrosoftSigned,
    VerificationUnavailable,
}

/// <summary>Full result of one enumeration pass.</summary>
public sealed record ProcessSnapshot(
    IReadOnlyList<ProcessInfo> Processes,
    DateTimeOffset Timestamp,
    int TotalProcesses,
    int TotalThreads);

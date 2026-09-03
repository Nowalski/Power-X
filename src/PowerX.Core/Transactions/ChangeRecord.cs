namespace PowerX.Core.Transactions;

public enum ChangeAction { Apply, Revert }

/// <summary>An immutable audit-log entry for one tweak operation. Persisted as JSON lines.</summary>
public sealed record ChangeRecord
{
    public required string TweakId { get; init; }
    public required string TweakName { get; init; }
    public required ChangeAction Action { get; init; }
    public required string PreviousState { get; init; }
    public required string ResultingState { get; init; }
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? SessionId { get; init; }
    public int WindowsBuild { get; init; }
}

/// <summary>Aggregate result of applying a set of tweaks as one transaction.</summary>
public sealed record TransactionResult(
    IReadOnlyList<ChangeRecord> Records,
    RestartScopeSummary Restart)
{
    public int Succeeded => Records.Count(r => r.Success && r.ResultingState != r.PreviousState);
    public int AlreadyConfigured => Records.Count(r => r.Success && r.ResultingState == r.PreviousState);
    public int Failed => Records.Count(r => !r.Success);
}

public sealed record RestartScopeSummary(bool Application, bool Explorer, bool SignOut, bool Reboot)
{
    public bool Any => Application || Explorer || SignOut || Reboot;
}

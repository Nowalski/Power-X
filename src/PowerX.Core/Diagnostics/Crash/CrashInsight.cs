namespace PowerX.Core.Diagnostics.Crash;

public enum CrashKind
{
    AppCrash,          // a user-mode process terminated abnormally
    AppHang,           // a process stopped responding
    ManagedException,  // an unhandled .NET exception (Event 1026) — usually the fullest signal
    Bugcheck,          // a kernel stop error (BSOD)
    LiveKernelReport,  // a kernel component was reset without a full stop (GPU/NDIS watchdog, …)
}

/// <summary>How much PowerX can actually stand behind the interpretation.</summary>
public enum CrashConfidence
{
    /// <summary>Not enough evidence to say anything useful. PowerX does not guess.</summary>
    Insufficient,
    Low,
    Moderate,
    High,
}

/// <summary>
/// One crash / hang / bugcheck, described honestly. <see cref="Facts"/> is only what a source
/// literally recorded; <see cref="LikelyCauses"/> is interpretation, qualified by
/// <see cref="Confidence"/>; <see cref="Missing"/> lists what would raise the confidence.
/// PowerX never turns "we don't know" into a diagnosis.
/// </summary>
public sealed record CrashInsight
{
    public required DateTimeOffset When { get; init; }
    public required CrashKind Kind { get; init; }

    /// <summary>Short headline: "PowerX.App.exe 0.1.0" or "DPC_WATCHDOG_VIOLATION (0x133)".</summary>
    public required string Subject { get; init; }

    /// <summary>Faulting module / driver when a source identified one (may be null).</summary>
    public string? Culprit { get; init; }

    public IReadOnlyList<string> Facts { get; init; } = [];
    public IReadOnlyList<string> LikelyCauses { get; init; } = [];
    public CrashConfidence Confidence { get; init; } = CrashConfidence.Insufficient;
    public IReadOnlyList<string> Remediation { get; init; } = [];
    public IReadOnlyList<string> Missing { get; init; } = [];

    /// <summary>A folder or file the user can open for the raw report. Never read further automatically.</summary>
    public string? ArtifactPath { get; init; }

    /// <summary>Where this came from, for the record: "WER · ReportArchive\AppCrash_…", "Event 1000".</summary>
    public string Source { get; init; } = "";

    /// <summary>Stable-ish id for `crashes show &lt;id&gt;` — first 8 of a hash of source+time+subject.</summary>
    public string Id => Convert.ToHexString(
        System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{Source}|{When:o}|{Subject}")))[..8].ToLowerInvariant();
}

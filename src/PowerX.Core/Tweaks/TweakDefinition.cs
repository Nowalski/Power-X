namespace PowerX.Core.Tweaks;

/// <summary>
/// Declarative description of one reversible Windows change. The UI, CLI, profiles,
/// config import/export and tests are all driven from this model — never from ad-hoc
/// registry writes in event handlers. See docs/ARCHITECTURE.md §Tweak engine.
/// </summary>
public sealed record TweakDefinition
{
    /// <summary>Stable, namespaced identifier, e.g. <c>explorer.show-file-extensions</c>. Never changes.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
    public required string Category { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    // --- The four questions every tweak must answer (docs/PRODUCT_SPEC.md §40) ---
    public required string WhatItDoes { get; init; }
    public required string WhyYouMightWant { get; init; }
    public required string Downside { get; init; }

    public required TweakRisk Risk { get; init; }
    public RestartScope Restart { get; init; } = RestartScope.None;
    public PrivilegeLevel Privilege { get; init; } = PrivilegeLevel.User;

    /// <summary>True only for conservative, broadly safe quality-of-life changes. Security trade-offs are never recommended.</summary>
    public bool Recommended { get; init; }

    /// <summary>Inclusive Windows build range this tweak is valid for. 0 = unbounded.</summary>
    public int MinBuild { get; init; }
    public int MaxBuild { get; init; }

    public IReadOnlyList<Evidence> Sources { get; init; } = [];
    public IReadOnlyList<string> Incompatibilities { get; init; } = [];
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>The operation that actually reads/writes state.</summary>
    public required ITweakOperation Operation { get; init; }

    public bool SupportsBuild(int build) =>
        (MinBuild == 0 || build >= MinBuild) && (MaxBuild == 0 || build <= MaxBuild);
}

/// <summary>Detect / apply / revert / verify for a single tweak. Implementations must be idempotent.</summary>
public interface ITweakOperation
{
    TweakState Detect(TweakContext context);
    TweakOutcome Apply(TweakContext context);
    TweakOutcome Revert(TweakContext context);
    bool Verify(TweakContext context);
}

/// <summary>Ambient information passed to every operation.</summary>
public sealed record TweakContext(int WindowsBuild, bool IsElevated, bool DryRun)
{
    public static TweakContext Detect(bool dryRun = false) => new(
        WindowsBuild: Environment.OSVersion.Version.Build,
        IsElevated: Diagnostics.PrivilegeCheck.IsElevated(),
        DryRun: dryRun);
}

public sealed record TweakOutcome(bool Success, TweakState ResultingState, string? Message = null)
{
    public static TweakOutcome Ok(TweakState state, string? msg = null) => new(true, state, msg);
    public static TweakOutcome Fail(string msg) => new(false, TweakState.Unknown, msg);
    public static TweakOutcome NoChange(TweakState state) => new(true, state, "Already in desired state");
}

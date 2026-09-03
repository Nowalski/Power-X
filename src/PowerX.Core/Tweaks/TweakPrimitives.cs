namespace PowerX.Core.Tweaks;

/// <summary>How hard a tweak is to reason about. Drives colour, confirmation and preselection.</summary>
public enum TweakRisk
{
    /// <summary>Cosmetic / trivially reversible.</summary>
    Low,
    /// <summary>Changes Windows behaviour; low breakage risk.</summary>
    Moderate,
    /// <summary>May affect software compatibility or system behaviour.</summary>
    Advanced,
    /// <summary>Reduces a Windows security protection. Never "Recommended".</summary>
    SecurityTradeoff,
    /// <summary>Removal or action that may be difficult to reverse.</summary>
    Destructive,
}

[Flags]
public enum RestartScope
{
    None = 0,
    Application = 1,
    Explorer = 2,
    SignOut = 4,
    Reboot = 8,
}

/// <summary>Current state of a tweak on this machine.</summary>
public enum TweakState
{
    /// <summary>Matches the Windows default.</summary>
    Default,
    /// <summary>Matches this tweak's desired (applied) value.</summary>
    Applied,
    /// <summary>Present but set to a value neither default nor our desired one.</summary>
    Custom,
    /// <summary>Not applicable to this Windows build / edition / hardware.</summary>
    NotApplicable,
    Unknown,
}

public enum PrivilegeLevel
{
    User,
    Administrator,
}

/// <summary>A citation backing a tweak — required for every performance/behaviour tweak.</summary>
public sealed record Evidence(string Summary, string? Url = null);

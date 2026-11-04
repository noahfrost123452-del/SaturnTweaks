using System.Collections.Generic;

namespace SaturnTweaks.Core.Models;

/// <summary>
/// Risk classification for a tweak. Drives UI color coding and preset inclusion rules.
/// </summary>
public enum TweakRisk
{
    /// <summary>Well-understood, reversible, broadly applicable.</summary>
    Safe,
    /// <summary>Beneficial for most systems; minor trade-offs possible.</summary>
    Low,
    /// <summary>System-dependent; user should understand the trade-off.</summary>
    Moderate,
    /// <summary>May disrupt functionality on some configurations.</summary>
    High,
    /// <summary>Potentially destructive; requires explicit user acknowledgment.</summary>
    Critical
}

public enum TweakCategory
{
    Performance,
    Graphics,
    Network,
    Input,
    Memory,
    Storage,
    Power,
    GameProfile,
    Advanced,
    System
}

public enum TweakResult
{
    /// <summary>Change applied and verified.</summary>
    AppliedAndVerified,
    /// <summary>Setting was already at the desired state.</summary>
    AlreadyConfigured,
    /// <summary>Hardware or OS does not support this tweak.</summary>
    NotApplicable,
    /// <summary>Windows version does not support this setting.</summary>
    Unsupported,
    /// <summary>Operation requires elevation that was not available.</summary>
    RequiresAdminPrivileges,
    /// <summary>Applied but restart is required before the change takes effect.</summary>
    RequiresRestart,
    /// <summary>Apply operation failed; original state preserved.</summary>
    Failed,
    /// <summary>Rolled back to original state.</summary>
    RolledBack,
    /// <summary>Change applied but verification returned an inconclusive result.</summary>
    VerificationInconclusive,
    /// <summary>Dry-run mode; no change was made.</summary>
    DryRun,
    /// <summary>Skipped due to unmet prerequisites.</summary>
    PrerequisiteNotMet
}

/// <summary>
/// Immutable definition for a single optimization tweak.
/// Implementations live in SaturnTweaks.Engine.Tweaks.
/// </summary>
public sealed record TweakDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required TweakCategory Category { get; init; }
    public required string ShortDescription { get; init; }
    public required string DetailedExplanation { get; init; }
    public required string ExpectedBenefit { get; init; }
    public required string PotentialDisadvantages { get; init; }
    public required TweakRisk RiskLevel { get; init; }

    /// <summary>Minimum Windows build numbers, keyed by edition family ("10", "11").</summary>
    public Dictionary<string, int> SupportedWindowsBuilds { get; init; } = new();

    public List<string> HardwareRequirements { get; init; } = new();
    public List<string> Prerequisites { get; init; } = new();

    public bool RequiresAdminPrivileges { get; init; }
    public bool RequiresRestart { get; init; }
    public bool RequiresSignOut { get; init; }

    /// <summary>Included in the one-click Recommended preset.</summary>
    public bool IncludedInRecommendedPreset { get; init; }

    /// <summary>Schema version for future migration handling.</summary>
    public int DefinitionVersion { get; init; } = 1;
}

/// <summary>
/// Mutable runtime state for a tweak in a given session.
/// </summary>
public sealed class TweakState
{
    public required string TweakId { get; init; }
    public TweakResult? LastResult { get; set; }
    public string? OriginalValueSnapshot { get; set; }
    public string? AppliedValueSnapshot { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsApplied => LastResult == TweakResult.AppliedAndVerified || LastResult == TweakResult.RequiresRestart;
    public bool CanRollBack => IsApplied && OriginalValueSnapshot is not null;
}

/// <summary>
/// Result record written to the change log after each tweak operation.
/// </summary>
public sealed record TweakOperationRecord
{
    public required string TweakId { get; init; }
    public required string DisplayName { get; init; }
    public required TweakResult Result { get; init; }
    public string? OriginalValue { get; init; }
    public string? NewValue { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? ErrorMessage { get; init; }
    public string? SessionId { get; init; }
}

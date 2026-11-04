using SaturnTweaks.Core.Models;

namespace SaturnTweaks.Core.Interfaces;

/// <summary>
/// Implemented by every concrete tweak in the Engine layer.
/// The lifecycle: Detect → CheckApplicability → Inspect → Backup → Apply → Verify → Record.
/// </summary>
public interface ITweakExecutor
{
    TweakDefinition Definition { get; }

    /// <summary>
    /// Read the current system state for this tweak.
    /// Must not modify anything.
    /// Returns the current value as a serializable string.
    /// </summary>
    Task<string?> InspectCurrentStateAsync(SystemSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Determine whether this tweak is applicable given the detected hardware and OS.
    /// Returns false if the hardware, driver, or OS version does not support this setting.
    /// </summary>
    Task<bool> IsApplicableAsync(SystemSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Determine whether the desired state is already active.
    /// Returns true if no change is needed.
    /// </summary>
    Task<bool> IsAlreadyConfiguredAsync(SystemSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Apply the optimization. Must capture OriginalValue in the returned state before modifying.
    /// In dry-run mode, must return DryRun without touching the system.
    /// </summary>
    Task<TweakOperationRecord> ApplyAsync(
        SystemSnapshot snapshot,
        bool dryRun = false,
        CancellationToken ct = default);

    /// <summary>
    /// Verify that the change was actually applied.
    /// Reads back the live value and compares against expected.
    /// Must not assume apply succeeded just because it didn't throw.
    /// </summary>
    Task<bool> VerifyAsync(SystemSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Restore the original value captured in the supplied record.
    /// Must not apply a generic default — must restore the specific recorded original.
    /// </summary>
    Task<TweakOperationRecord> RollbackAsync(
        TweakOperationRecord originalRecord,
        CancellationToken ct = default);
}

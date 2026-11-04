using SaturnTweaks.Core.Models;

namespace SaturnTweaks.Core.Interfaces;

/// <summary>
/// Discovers hardware, OS, drivers, and configuration for a Windows system.
/// All reads are non-destructive. Unavailable values are returned as null, not invented.
/// </summary>
public interface ISystemDetector
{
    Task<SystemSnapshot> CaptureSnapshotAsync(CancellationToken ct = default);
    Task RefreshLiveMetricsAsync(SystemSnapshot snapshot, CancellationToken ct = default);
}

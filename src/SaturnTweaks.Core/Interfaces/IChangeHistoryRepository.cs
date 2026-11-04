using SaturnTweaks.Core.Models;

namespace SaturnTweaks.Core.Interfaces;

/// <summary>
/// Persists and retrieves tweak operation records for rollback and audit.
/// </summary>
public interface IChangeHistoryRepository
{
    Task RecordAsync(TweakOperationRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<TweakOperationRecord>> GetSessionRecordsAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<TweakOperationRecord>> GetAllRecordsAsync(CancellationToken ct = default);
    Task<TweakOperationRecord?> GetLatestForTweakAsync(string tweakId, CancellationToken ct = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken ct = default);
    Task ExportAsync(string filePath, CancellationToken ct = default);
    Task<IReadOnlyList<TweakOperationRecord>> ImportAsync(string filePath, CancellationToken ct = default);
}

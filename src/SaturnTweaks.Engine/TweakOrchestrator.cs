using Microsoft.Extensions.Logging;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;

namespace SaturnTweaks.Engine;

/// <summary>
/// Runs optimization sessions: detect applicability, confirm, apply, verify, record.
/// Guarantees the lifecycle: Inspect → IsApplicable → IsAlreadyConfigured → Apply → Verify → Record.
/// Never reports success without verification.
/// </summary>
public sealed class TweakOrchestrator
{
    private readonly ILogger<TweakOrchestrator> _logger;
    private readonly IChangeHistoryRepository _history;
    private readonly IReadOnlyList<ITweakExecutor> _registeredTweaks;

    public TweakOrchestrator(
        ILogger<TweakOrchestrator> logger,
        IChangeHistoryRepository history,
        IEnumerable<ITweakExecutor> tweaks)
    {
        _logger = logger;
        _history = history;
        _registeredTweaks = tweaks.ToList().AsReadOnly();
    }

    public IReadOnlyList<ITweakExecutor> AllTweaks => _registeredTweaks;

    /// <summary>
    /// Run a complete optimization session for the selected tweak IDs.
    /// Progress is reported via the supplied callback after each tweak completes.
    /// </summary>
    public async Task<OptimizationSession> RunSessionAsync(
        SystemSnapshot snapshot,
        IEnumerable<string> tweakIds,
        bool dryRun,
        IProgress<TweakOperationRecord>? progress = null,
        CancellationToken ct = default)
    {
        var session = new OptimizationSession
        {
            SystemSnapshot = snapshot,
            DryRun = dryRun,
            Status = SessionStatus.Running
        };
        session.SelectedTweakIds.AddRange(tweakIds);

        var selectedExecutors = _registeredTweaks
            .Where(t => session.SelectedTweakIds.Contains(t.Definition.Id))
            .ToList();

        _logger.LogInformation("Session {SessionId} starting. {Count} tweaks, dryRun={DryRun}",
            session.SessionId, selectedExecutors.Count, dryRun);

        foreach (var executor in selectedExecutors)
        {
            if (ct.IsCancellationRequested)
            {
                session.Status = SessionStatus.Cancelled;
                break;
            }

            TweakOperationRecord result;
            try
            {
                result = await RunSingleTweakAsync(executor, snapshot, dryRun, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancellation is a normal session outcome, not a failed tweak.
                session.Status = SessionStatus.Cancelled;
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception running tweak {TweakId}.", executor.Definition.Id);
                result = new TweakOperationRecord
                {
                    TweakId = executor.Definition.Id,
                    DisplayName = executor.Definition.DisplayName,
                    Result = TweakResult.Failed,
                    ErrorMessage = $"Unexpected error: {ex.Message}",
                    SessionId = session.SessionId
                };
            }

            result = result with { SessionId = session.SessionId };
            session.Results.Add(result);

            if (!dryRun)
            {
                try
                {
                    await _history.RecordAsync(result, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Preserve the result already produced, but stop the session.
                    session.Status = SessionStatus.Cancelled;
                    break;
                }
            }

            progress?.Report(result);
        }

        session.CompletedAt = DateTimeOffset.UtcNow;
        // Do not overwrite a cancellation recorded by the loop with Completed.
        if (session.Status != SessionStatus.Cancelled)
        {
            session.Status = session.FailedCount > 0
                ? SessionStatus.CompletedWithErrors
                : SessionStatus.Completed;
        }

        _logger.LogInformation(
            "Session {SessionId} complete. Applied={A} AlreadyOk={K} Failed={F} Skipped={S}",
            session.SessionId, session.AppliedCount, session.AlreadyConfiguredCount,
            session.FailedCount, session.SkippedCount);

        return session;
    }

    private async Task<TweakOperationRecord> RunSingleTweakAsync(
        ITweakExecutor executor,
        SystemSnapshot snapshot,
        bool dryRun,
        CancellationToken ct)
    {
        var def = executor.Definition;

        // Step 1: Applicability check
        bool applicable = await executor.IsApplicableAsync(snapshot, ct);
        if (!applicable)
        {
            _logger.LogDebug("[{Id}] Not applicable for this system.", def.Id);
            return new TweakOperationRecord
            {
                TweakId = def.Id, DisplayName = def.DisplayName,
                Result = TweakResult.NotApplicable
            };
        }

        // Step 2: Already configured?
        bool alreadyOk = await executor.IsAlreadyConfiguredAsync(snapshot, ct);
        if (alreadyOk)
        {
            _logger.LogDebug("[{Id}] Already configured — skipping.", def.Id);
            return new TweakOperationRecord
            {
                TweakId = def.Id, DisplayName = def.DisplayName,
                Result = TweakResult.AlreadyConfigured,
                OriginalValue = await executor.InspectCurrentStateAsync(snapshot, ct)
            };
        }

        // Step 3: Apply (captures original internally before modifying)
        var applyRecord = await executor.ApplyAsync(snapshot, dryRun, ct);
        if (applyRecord.Result is TweakResult.Failed
            or TweakResult.RequiresAdminPrivileges
            or TweakResult.DryRun)
            return applyRecord;

        // Step 4: Verify — only when not dry-run and not requires-restart
        if (!dryRun && applyRecord.Result != TweakResult.RequiresRestart)
        {
            bool verified = await executor.VerifyAsync(snapshot, ct);
            if (!verified)
            {
                _logger.LogWarning("[{Id}] Verification inconclusive after apply.", def.Id);
                return applyRecord with { Result = TweakResult.VerificationInconclusive };
            }
        }

        return applyRecord;
    }

    /// <summary>
    /// Roll back a specific session to the state captured before it ran.
    /// </summary>
    public async Task<List<TweakOperationRecord>> RollbackSessionAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        var sessionRecords = await _history.GetSessionRecordsAsync(sessionId, ct);
        var rollbackResults = new List<TweakOperationRecord>();

        // Roll back in reverse application order
        foreach (var record in sessionRecords.Reverse())
        {
            if (record.Result != TweakResult.AppliedAndVerified
                && record.Result != TweakResult.RequiresRestart)
                continue;

            var executor = _registeredTweaks.FirstOrDefault(t => t.Definition.Id == record.TweakId);
            if (executor is null)
            {
                rollbackResults.Add(record with
                {
                    Result = TweakResult.Failed,
                    ErrorMessage = "Tweak executor not found — cannot roll back automatically."
                });
                continue;
            }

            try
            {
                var rollbackRecord = await executor.RollbackAsync(record, ct);
                await _history.RecordAsync(rollbackRecord, ct);
                rollbackResults.Add(rollbackRecord);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rollback failed for tweak {TweakId}.", record.TweakId);
                rollbackResults.Add(record with
                {
                    Result = TweakResult.Failed,
                    ErrorMessage = ex.Message
                });
            }
        }

        return rollbackResults;
    }

    /// <summary>
    /// Returns the tweaks that are applicable for the given snapshot and not already configured.
    /// Used to populate the recommended preset before the user confirms.
    /// </summary>
    public async Task<List<ITweakExecutor>> GetApplicableRecommendedTweaksAsync(
        SystemSnapshot snapshot,
        CancellationToken ct = default)
    {
        var result = new List<ITweakExecutor>();
        foreach (var exec in _registeredTweaks.Where(t => t.Definition.IncludedInRecommendedPreset))
        {
            if (!await exec.IsApplicableAsync(snapshot, ct)) continue;
            if (await exec.IsAlreadyConfiguredAsync(snapshot, ct)) continue;
            result.Add(exec);
        }
        return result;
    }
}

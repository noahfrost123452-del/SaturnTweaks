namespace SaturnTweaks.Core.Models;

public enum SessionStatus
{
    Pending,
    Running,
    Completed,
    CompletedWithErrors,
    Cancelled,
    RolledBack
}

public sealed class OptimizationSession
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Pending;
    public bool DryRun { get; init; }
    public SystemSnapshot? SystemSnapshot { get; set; }
    public List<string> SelectedTweakIds { get; init; } = new();
    public List<TweakOperationRecord> Results { get; set; } = new();

    public int AppliedCount => Results.Count(r => r.Result == TweakResult.AppliedAndVerified);
    public int AlreadyConfiguredCount => Results.Count(r => r.Result == TweakResult.AlreadyConfigured);
    public int FailedCount => Results.Count(r => r.Result == TweakResult.Failed);
    public int SkippedCount => Results.Count(r =>
        r.Result == TweakResult.NotApplicable ||
        r.Result == TweakResult.Unsupported ||
        r.Result == TweakResult.PrerequisiteNotMet);
    public bool RequiresRestart => Results.Any(r => r.Result == TweakResult.RequiresRestart);
}

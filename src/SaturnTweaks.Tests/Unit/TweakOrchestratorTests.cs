using FluentAssertions;
using Moq;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SaturnTweaks.Tests.Unit;

public sealed class TweakOrchestratorTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static SystemSnapshot MakeSnapshot(bool isAdmin = true) => new()
    {
        IsRunningAsAdministrator = isAdmin,
        OsBuildNumber = 22621,
        Gpus = new List<GpuInfo> { new() { Name = "NVIDIA Test GPU", Vendor = "NVIDIA", IsIntegrated = false } }
    };

    private static TweakOrchestrator MakeOrchestrator(
        IChangeHistoryRepository repo,
        params ITweakExecutor[] tweaks)
        => new(NullLogger<TweakOrchestrator>.Instance, repo, tweaks);

    private static Mock<ITweakExecutor> MakeTweak(
        string id,
        bool applicable = true,
        bool alreadyConfigured = false,
        TweakResult applyResult = TweakResult.AppliedAndVerified,
        bool verified = true)
    {
        var mock = new Mock<ITweakExecutor>();
        mock.Setup(t => t.Definition).Returns(new TweakDefinition
        {
            Id = id,
            DisplayName = id,
            Category = TweakCategory.Performance,
            ShortDescription = "Test",
            DetailedExplanation = "Test",
            ExpectedBenefit = "Test",
            PotentialDisadvantages = "Test",
            RiskLevel = TweakRisk.Safe,
            IncludedInRecommendedPreset = true
        });
        mock.Setup(t => t.IsApplicableAsync(It.IsAny<SystemSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(applicable);
        mock.Setup(t => t.IsAlreadyConfiguredAsync(It.IsAny<SystemSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(alreadyConfigured);
        mock.Setup(t => t.InspectCurrentStateAsync(It.IsAny<SystemSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0");
        mock.Setup(t => t.ApplyAsync(It.IsAny<SystemSnapshot>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TweakOperationRecord
            {
                TweakId = id, DisplayName = id, Result = applyResult,
                OriginalValue = "0", NewValue = "1"
            });
        mock.Setup(t => t.VerifyAsync(It.IsAny<SystemSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(verified);
        return mock;
    }

    private static Mock<IChangeHistoryRepository> MakeRepo()
    {
        var repo = new Mock<IChangeHistoryRepository>();
        repo.Setup(r => r.RecordAsync(It.IsAny<TweakOperationRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetSessionRecordsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TweakOperationRecord>());
        return repo;
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunSession_ApplicableNotConfigured_AppliesAndVerifies()
    {
        var tweak = MakeTweak("t1");
        var repo = MakeRepo();
        var orchestrator = MakeOrchestrator(repo.Object, tweak.Object);
        var snapshot = MakeSnapshot();

        var session = await orchestrator.RunSessionAsync(snapshot, ["t1"], dryRun: false);

        session.AppliedCount.Should().Be(1);
        session.FailedCount.Should().Be(0);
        session.Results[0].Result.Should().Be(TweakResult.AppliedAndVerified);
    }

    [Fact]
    public async Task RunSession_AlreadyConfigured_ReportsAlreadyConfigured()
    {
        var tweak = MakeTweak("t2", alreadyConfigured: true);
        var repo = MakeRepo();
        var orchestrator = MakeOrchestrator(repo.Object, tweak.Object);

        var session = await orchestrator.RunSessionAsync(MakeSnapshot(), ["t2"], dryRun: false);

        session.AlreadyConfiguredCount.Should().Be(1);
        session.AppliedCount.Should().Be(0);
        session.Results[0].Result.Should().Be(TweakResult.AlreadyConfigured);
    }

    [Fact]
    public async Task RunSession_NotApplicable_ReportsNotApplicable()
    {
        var tweak = MakeTweak("t3", applicable: false);
        var repo = MakeRepo();
        var orchestrator = MakeOrchestrator(repo.Object, tweak.Object);

        var session = await orchestrator.RunSessionAsync(MakeSnapshot(), ["t3"], dryRun: false);

        session.Results[0].Result.Should().Be(TweakResult.NotApplicable);
        session.SkippedCount.Should().Be(1);
    }

    [Fact]
    public async Task RunSession_DryRun_DoesNotWriteToRepository()
    {
        var tweak = MakeTweak("t4", applyResult: TweakResult.DryRun);
        var repo = MakeRepo();
        var orchestrator = MakeOrchestrator(repo.Object, tweak.Object);

        await orchestrator.RunSessionAsync(MakeSnapshot(), ["t4"], dryRun: true);

        // Dry-run: Apply is called with dryRun=true, but repo.RecordAsync should NOT be called
        // (dry-run results are not persisted)
        repo.Verify(r => r.RecordAsync(
            It.Is<TweakOperationRecord>(rec => rec.Result == TweakResult.DryRun),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunSession_ApplySucceedsVerificationFails_ReportsVerificationInconclusive()
    {
        var tweak = MakeTweak("t5", verified: false);
        var repo = MakeRepo();
        var orchestrator = MakeOrchestrator(repo.Object, tweak.Object);

        var session = await orchestrator.RunSessionAsync(MakeSnapshot(), ["t5"], dryRun: false);

        session.Results[0].Result.Should().Be(TweakResult.VerificationInconclusive);
        // Must not report success on failed verification
        session.AppliedCount.Should().Be(0);
    }

    [Fact]
    public async Task RunSession_ApplyFails_ReportsFailed()
    {
        var tweak = MakeTweak("t6", applyResult: TweakResult.Failed);
        var repo = MakeRepo();
        var orchestrator = MakeOrchestrator(repo.Object, tweak.Object);

        var session = await orchestrator.RunSessionAsync(MakeSnapshot(), ["t6"], dryRun: false);

        session.FailedCount.Should().Be(1);
        session.Results[0].Result.Should().Be(TweakResult.Failed);
    }

    [Fact]
    public async Task RunSession_Cancelled_ReturnsEarlyWithCancelledStatus()
    {
        var tweak = MakeTweak("t7");
        var repo = MakeRepo();
        var orchestrator = MakeOrchestrator(repo.Object, tweak.Object);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var session = await orchestrator.RunSessionAsync(MakeSnapshot(), ["t7"], dryRun: false, ct: cts.Token);

        session.Status.Should().Be(SessionStatus.Cancelled);
        session.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task RunSession_MultipleSelected_AllProcessed()
    {
        var t1 = MakeTweak("m1");
        var t2 = MakeTweak("m2");
        var t3 = MakeTweak("m3", applicable: false);
        var repo = MakeRepo();
        var orchestrator = MakeOrchestrator(repo.Object, t1.Object, t2.Object, t3.Object);

        var session = await orchestrator.RunSessionAsync(
            MakeSnapshot(), ["m1", "m2", "m3"], dryRun: false);

        session.Results.Should().HaveCount(3);
        session.AppliedCount.Should().Be(2);
        session.SkippedCount.Should().Be(1);
    }

    [Fact]
    public async Task RunSession_OneFailsOthersStillRun()
    {
        var t1 = MakeTweak("f1", applyResult: TweakResult.Failed);
        var t2 = MakeTweak("f2");
        var repo = MakeRepo();
        var orchestrator = MakeOrchestrator(repo.Object, t1.Object, t2.Object);

        var session = await orchestrator.RunSessionAsync(
            MakeSnapshot(), ["f1", "f2"], dryRun: false);

        // Failure of f1 must not prevent f2 from running
        session.Results.Should().HaveCount(2);
        session.FailedCount.Should().Be(1);
        session.AppliedCount.Should().Be(1);
        session.Status.Should().Be(SessionStatus.CompletedWithErrors);
    }

    [Fact]
    public async Task GetApplicableRecommended_FiltersCorrectly()
    {
        var t1 = MakeTweak("r1"); // applicable, not configured, recommended
        var t2 = MakeTweak("r2", alreadyConfigured: true); // already ok
        var t3 = MakeTweak("r3", applicable: false); // not applicable
        var repo = MakeRepo();
        var orchestrator = MakeOrchestrator(repo.Object, t1.Object, t2.Object, t3.Object);

        var result = await orchestrator.GetApplicableRecommendedTweaksAsync(MakeSnapshot());

        result.Should().HaveCount(1);
        result[0].Definition.Id.Should().Be("r1");
    }

    [Fact]
    public async Task RollbackSession_CallsRollbackOnAppliedTweaks()
    {
        var appliedRecord = new TweakOperationRecord
        {
            TweakId = "rb1", DisplayName = "RB1",
            Result = TweakResult.AppliedAndVerified,
            OriginalValue = "0", NewValue = "1",
            SessionId = "session_abc"
        };

        var tweak = MakeTweak("rb1");
        tweak.Setup(t => t.RollbackAsync(It.IsAny<TweakOperationRecord>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new TweakOperationRecord
             {
                 TweakId = "rb1", DisplayName = "RB1",
                 Result = TweakResult.RolledBack
             });

        var repo = new Mock<IChangeHistoryRepository>();
        repo.Setup(r => r.GetSessionRecordsAsync("session_abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TweakOperationRecord> { appliedRecord });
        repo.Setup(r => r.RecordAsync(It.IsAny<TweakOperationRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = MakeOrchestrator(repo.Object, tweak.Object);
        var results = await orchestrator.RollbackSessionAsync("session_abc");

        results.Should().HaveCount(1);
        results[0].Result.Should().Be(TweakResult.RolledBack);
        tweak.Verify(t => t.RollbackAsync(
            It.Is<TweakOperationRecord>(r => r.TweakId == "rb1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RollbackSession_SkipsNonAppliedRecords()
    {
        var skippedRecord = new TweakOperationRecord
        {
            TweakId = "sk1", DisplayName = "SK1",
            Result = TweakResult.AlreadyConfigured,
            SessionId = "session_skip"
        };

        var tweak = MakeTweak("sk1");
        var repo = new Mock<IChangeHistoryRepository>();
        repo.Setup(r => r.GetSessionRecordsAsync("session_skip", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TweakOperationRecord> { skippedRecord });

        var orchestrator = MakeOrchestrator(repo.Object, tweak.Object);
        var results = await orchestrator.RollbackSessionAsync("session_skip");

        results.Should().BeEmpty();
        tweak.Verify(t => t.RollbackAsync(It.IsAny<TweakOperationRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

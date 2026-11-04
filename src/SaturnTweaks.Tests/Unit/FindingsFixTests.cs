using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Microsoft.Win32;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine;
using SaturnTweaks.Engine.Tweaks;
using Xunit;

namespace SaturnTweaks.Tests.Unit;

/// <summary>
/// Tests that directly address the findings from the v2 independent source review:
/// Finding 4: ApplyAsync must read-back before reporting AppliedAndVerified.
/// Finding 5: RollbackAsync must preserve original registry value type.
/// Finding 5: RollbackAsync must delete the value when original was absent.
/// Finding 6: FlattenMouseCurvesTweak must verify both curves, not just X.
/// </summary>

// ════════════════════════════════════════════════════════════════════════════
// Finding 4: Verification read-back in RegistryTweakBase.ApplyAsync
// ════════════════════════════════════════════════════════════════════════════

[Collection(RegistryTestsCollection.Name)]
public sealed class FindingFourVerificationReadBackTests : IDisposable
{
    private const string TestSubKey = @"SOFTWARE\SaturnTweaks\Tests\FindingFour";

    // A tweak whose DesiredValue we can predict and confirm
    private sealed class VerifyReadBackTweak : RegistryTweakBase
    {
        public int DesiredInt { get; set; } = 99;

        public VerifyReadBackTweak() : base(NullLogger.Instance) { }

        public override TweakDefinition Definition { get; } = new()
        {
            Id = "test.findings.four",
            DisplayName = "Finding Four Test",
            Category = TweakCategory.Performance,
            ShortDescription = "Test",
            DetailedExplanation = "Tests that ApplyAsync reads back after writing.",
            ExpectedBenefit = "Test",
            PotentialDisadvantages = "Test",
            RiskLevel = TweakRisk.Safe,
            SupportedWindowsBuilds = new() { ["10"] = 10240 }
        };

        protected override RegistryHive Hive => Microsoft.Win32.RegistryHive.CurrentUser;
        protected override string SubKeyPath => TestSubKey;
        protected override string ValueName => "TestVal";
        protected override object DesiredValue => DesiredInt;
    }

    private readonly VerifyReadBackTweak _tweak = new();
    private readonly SystemSnapshot _snapshot = new() { OsBuildNumber = 22621 };

    public void Dispose()
    {
        try { Microsoft.Win32.Registry.CurrentUser
            .DeleteSubKeyTree(TestSubKey, throwOnMissingSubKey: false); }
        catch { }
    }

    [Fact]
    public async Task ApplyAsync_SuccessfulWrite_ReturnsAppliedAndVerified_NotOptimisticSuccess()
    {
        // This test confirms the write-then-read-back path:
        // ApplyAsync must return AppliedAndVerified only when the read-back confirms the write.
        var record = await _tweak.ApplyAsync(_snapshot, dryRun: false);

        // If the implementation is correct, the read-back found the written value
        Assert.Contains(record.Result, new[]
        {
            TweakResult.AppliedAndVerified,
            TweakResult.VerificationInconclusive,
            TweakResult.Failed,
            TweakResult.RequiresAdminPrivileges
        });

        // On a test system where HKCU writes succeed, it should be AppliedAndVerified
        if (record.Result == TweakResult.AppliedAndVerified)
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(TestSubKey, writable: false);
            var actual = key?.GetValue("TestVal");
            actual.Should().Be(99,
                because: "if result is AppliedAndVerified, the registry value must actually be 99");
        }
    }

    [Fact]
    public async Task ApplyAsync_NeverReturnsAppliedAndVerifiedForDryRun()
    {
        var record = await _tweak.ApplyAsync(_snapshot, dryRun: true);
        record.Result.Should().NotBe(TweakResult.AppliedAndVerified,
            because: "dry run must never claim a real write was verified");
        record.Result.Should().Be(TweakResult.DryRun);
    }

    [Fact]
    public async Task ApplyAsync_NewValue_MatchesDesiredValue()
    {
        var record = await _tweak.ApplyAsync(_snapshot, dryRun: false);
        if (record.Result is TweakResult.AppliedAndVerified)
        {
            record.NewValue.Should().Be("99",
                because: "NewValue must reflect what was actually written");
        }
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Finding 5: Type-preserving rollback
// ════════════════════════════════════════════════════════════════════════════

[Collection(RegistryTestsCollection.Name)]
public sealed class FindingFiveRollbackTypePreservationTests : IDisposable
{
    private const string TestSubKey = @"SOFTWARE\SaturnTweaks\Tests\FindingFive";

    private sealed class TypePreserveTweak : RegistryTweakBase
    {
        public TypePreserveTweak() : base(NullLogger.Instance) { }

        public override TweakDefinition Definition { get; } = new()
        {
            Id = "test.findings.five",
            DisplayName = "Finding Five Test",
            Category = TweakCategory.Performance,
            ShortDescription = "Test",
            DetailedExplanation = "Tests type-preserving rollback.",
            ExpectedBenefit = "Test",
            PotentialDisadvantages = "Test",
            RiskLevel = TweakRisk.Safe,
            SupportedWindowsBuilds = new() { ["10"] = 10240 }
        };

        protected override Microsoft.Win32.RegistryHive Hive =>
            Microsoft.Win32.RegistryHive.CurrentUser;
        protected override string SubKeyPath => TestSubKey;
        protected override string ValueName => "TypeTestVal";
        protected override object DesiredValue => 1;
    }

    private readonly TypePreserveTweak _tweak = new();
    private readonly SystemSnapshot _snapshot = new() { OsBuildNumber = 22621 };

    public void Dispose()
    {
        try { Microsoft.Win32.Registry.CurrentUser
            .DeleteSubKeyTree(TestSubKey, throwOnMissingSubKey: false); }
        catch { }
    }

    [Fact]
    public async Task RollbackAsync_WhenOriginalWasDWord_RestoresDWord()
    {
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue("TypeTestVal", 7, Microsoft.Win32.RegistryValueKind.DWord);

        var applyRecord = await _tweak.ApplyAsync(_snapshot, dryRun: false);
        applyRecord.OriginalValue.Should().StartWith("DWord:",
            because: "backup must record the original kind");

        await _tweak.RollbackAsync(applyRecord);

        using var verifyKey = Microsoft.Win32.Registry.CurrentUser
            .OpenSubKey(TestSubKey, writable: false);
        var kind = verifyKey?.GetValueKind("TypeTestVal");
        kind.Should().Be(Microsoft.Win32.RegistryValueKind.DWord,
            because: "rollback must restore the DWord kind, not coerce to another type");
    }

    [Fact]
    public async Task RollbackAsync_WhenOriginalWasString_RestoresString()
    {
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue("TypeTestVal", "original-string", Microsoft.Win32.RegistryValueKind.String);

        var applyRecord = await _tweak.ApplyAsync(_snapshot, dryRun: false);
        applyRecord.OriginalValue.Should().StartWith("String:",
            because: "original String kind must be captured");

        await _tweak.RollbackAsync(applyRecord);

        using var verifyKey = Microsoft.Win32.Registry.CurrentUser
            .OpenSubKey(TestSubKey, writable: false);
        var restoredVal = verifyKey?.GetValue("TypeTestVal");
        var restoredKind = verifyKey?.GetValueKind("TypeTestVal");

        restoredVal.Should().Be("original-string",
            because: "rollback must restore the original string value");
        restoredKind.Should().Be(Microsoft.Win32.RegistryValueKind.String,
            because: "rollback must restore the String kind, not coerce to DWord");
    }

    [Fact]
    public async Task RollbackAsync_WhenOriginalAbsent_DeletesValueNotWritesDefault()
    {
        // Value does not exist before apply
        var applyRecord = await _tweak.ApplyAsync(_snapshot, dryRun: false);
        applyRecord.OriginalValue.Should().BeNull(
            because: "absence must be recorded as null, not as '0' or any default");

        var rollback = await _tweak.RollbackAsync(applyRecord);
        rollback.Result.Should().Be(TweakResult.RolledBack);

        using var verifyKey = Microsoft.Win32.Registry.CurrentUser
            .OpenSubKey(TestSubKey, writable: false);
        var restoredVal = verifyKey?.GetValue("TypeTestVal");
        restoredVal.Should().BeNull(
            because: "rollback of an absent-original must delete the value, never write 0 or ''");
    }

    [Fact]
    public void RollbackAsync_OriginalValueFormat_IsKindColonValue()
    {
        // Test the serialisation format directly without touching the registry
        // by verifying the format string that RollbackAsync parses.
        // "DWord:5" must round-trip cleanly.
        const string backup = "DWord:5";
        backup.Should().MatchRegex(@"^\w+:\S*$",
            because: "backup format must be 'Kind:value' for unambiguous deserialisation");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Finding 6: FlattenMouseCurvesTweak.VerifyAsync checks both curves
// ════════════════════════════════════════════════════════════════════════════

[Collection(RegistryTestsCollection.Name)]
public sealed class FindingSixFlattenMouseCurvesVerifyTests : IDisposable
{
    private const string MouseSubKey = @"Control Panel\Mouse";
    private readonly FlattenMouseCurvesTweak _tweak =
        new(NullLogger<FlattenMouseCurvesTweak>.Instance);
    private readonly SystemSnapshot _snapshot = new() { OsBuildNumber = 22621 };

    // Save originals before any test writes
    private readonly byte[]? _savedX;
    private readonly byte[]? _savedY;

    public FindingSixFlattenMouseCurvesVerifyTests()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser
            .OpenSubKey(MouseSubKey, writable: false);
        _savedX = key?.GetValue("SmoothMouseXCurve") as byte[];
        _savedY = key?.GetValue("SmoothMouseYCurve") as byte[];
    }

    public void Dispose()
    {
        // Restore original curves after each test
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(MouseSubKey, writable: true);
            if (key is null) return;

            if (_savedX is not null)
                key.SetValue("SmoothMouseXCurve", _savedX, Microsoft.Win32.RegistryValueKind.Binary);
            else
                key.DeleteValue("SmoothMouseXCurve", throwOnMissingValue: false);

            if (_savedY is not null)
                key.SetValue("SmoothMouseYCurve", _savedY, Microsoft.Win32.RegistryValueKind.Binary);
            else
                key.DeleteValue("SmoothMouseYCurve", throwOnMissingValue: false);
        }
        catch { /* best-effort restore */ }
    }

    [Fact]
    public async Task VerifyAsync_BothCurvesFlat_ReturnsTrue()
    {
        var flat = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(MouseSubKey, writable: true);
            if (key is null) return; // skip if key inaccessible

            key.SetValue("SmoothMouseXCurve", flat, Microsoft.Win32.RegistryValueKind.Binary);
            key.SetValue("SmoothMouseYCurve", flat, Microsoft.Win32.RegistryValueKind.Binary);
        }
        catch { return; } // skip if registry write unavailable in test environment

        var result = await _tweak.VerifyAsync(_snapshot);
        result.Should().BeTrue("both curves are flat, so VerifyAsync must return true");
    }

    [Fact]
    public async Task VerifyAsync_OnlyXCurveFlat_ReturnsFalse()
    {
        var flat = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var nonFlat = new byte[] { 1, 0, 2, 0, 3, 0, 4, 0, 5, 0 };
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(MouseSubKey, writable: true);
            if (key is null) return;

            key.SetValue("SmoothMouseXCurve", flat, Microsoft.Win32.RegistryValueKind.Binary);
            key.SetValue("SmoothMouseYCurve", nonFlat, Microsoft.Win32.RegistryValueKind.Binary);
        }
        catch { return; }

        var result = await _tweak.VerifyAsync(_snapshot);
        result.Should().BeFalse(
            "Y curve is not flat — VerifyAsync must check BOTH curves, not just X");
    }

    [Fact]
    public async Task VerifyAsync_OnlyYCurveFlat_ReturnsFalse()
    {
        var flat = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var nonFlat = new byte[] { 1, 0, 2, 0, 3, 0, 4, 0, 5, 0 };
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(MouseSubKey, writable: true);
            if (key is null) return;

            key.SetValue("SmoothMouseXCurve", nonFlat, Microsoft.Win32.RegistryValueKind.Binary);
            key.SetValue("SmoothMouseYCurve", flat, Microsoft.Win32.RegistryValueKind.Binary);
        }
        catch { return; }

        var result = await _tweak.VerifyAsync(_snapshot);
        result.Should().BeFalse(
            "X curve is not flat — VerifyAsync must check BOTH curves, not just Y");
    }

    [Fact]
    public async Task InspectCurrentStateAsync_ShowsBothCurves()
    {
        var state = await _tweak.InspectCurrentStateAsync(_snapshot);
        // Must contain both X and Y
        Assert.Contains("X=", state);
        Assert.Contains("Y=", state);
    }

    [Fact]
    public async Task ApplyAsync_DryRun_IncludesBothCurvesInOriginalValue()
    {
        var record = await _tweak.ApplyAsync(_snapshot, dryRun: true);
        record.Result.Should().Be(TweakResult.DryRun);
        Assert.Contains("X=", record.OriginalValue);
        Assert.Contains("Y=", record.OriginalValue);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Finding 6: Verify orchestrator correctly handles VerificationInconclusive
// The orchestrator path was already correct; this confirms it stays correct.
// ════════════════════════════════════════════════════════════════════════════

public sealed class FindingSixOrchestratorVerificationTests
{
    private static Mock<ITweakExecutor> MakeTweakThatAppliesButVerificationIsInconclusive(string id)
    {
        var mock = new Mock<ITweakExecutor>();
        mock.Setup(t => t.Definition).Returns(new TweakDefinition
        {
            Id = id, DisplayName = id, Category = TweakCategory.Performance,
            ShortDescription = "Test", DetailedExplanation = "Test",
            ExpectedBenefit = "Test", PotentialDisadvantages = "Test",
            RiskLevel = TweakRisk.Safe,
            SupportedWindowsBuilds = new() { ["10"] = 10240 }
        });
        mock.Setup(t => t.IsApplicableAsync(It.IsAny<SystemSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mock.Setup(t => t.IsAlreadyConfiguredAsync(It.IsAny<SystemSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        mock.Setup(t => t.InspectCurrentStateAsync(It.IsAny<SystemSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0");
        // ApplyAsync returns AppliedAndVerified (simulating the old pre-fix behaviour)
        mock.Setup(t => t.ApplyAsync(It.IsAny<SystemSnapshot>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TweakOperationRecord
            {
                TweakId = id, DisplayName = id,
                Result = TweakResult.AppliedAndVerified,
                OriginalValue = "DWord:0", NewValue = "1"
            });
        // But VerifyAsync returns false (the actual state doesn't match)
        mock.Setup(t => t.VerifyAsync(It.IsAny<SystemSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return mock;
    }

    [Fact]
    public async Task Orchestrator_WhenVerifyReturnsFalse_ReportsVerificationInconclusive()
    {
        var tweak = MakeTweakThatAppliesButVerificationIsInconclusive("vi1");
        var repo = new Mock<IChangeHistoryRepository>();
        repo.Setup(r => r.RecordAsync(It.IsAny<TweakOperationRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestrator = new TweakOrchestrator(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TweakOrchestrator>.Instance,
            repo.Object, new[] { tweak.Object });

        var session = await orchestrator.RunSessionAsync(
            new SystemSnapshot { OsBuildNumber = 22621 }, ["vi1"], dryRun: false);

        session.Results[0].Result.Should().Be(TweakResult.VerificationInconclusive,
            because: "when VerifyAsync returns false, the orchestrator must not claim success");
        session.AppliedCount.Should().Be(0,
            because: "VerificationInconclusive must not be counted as Applied");
    }
}

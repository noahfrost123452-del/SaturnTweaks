using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine.Tweaks;
using Xunit;

namespace SaturnTweaks.Tests.Unit;

/// <summary>
/// Tests for RegistryTweakBase using a concrete subclass targeting a safe test key.
/// IMPORTANT: These tests require write access to HKCU and create/delete a test subkey.
/// They do NOT touch production registry paths.
///
/// The test subkey is deleted in Dispose() regardless of outcome.
/// </summary>
[Collection(RegistryTestsCollection.Name)]
public sealed class RegistryTweakBaseTests : IDisposable
{
    private const string TestSubKey = @"SOFTWARE\SaturnTweaks\Tests\TweakBaseTest";
    private const string TestValueName = "TestValue";

    // Concrete subclass targeting our safe test key
    private sealed class TestRegistryTweak : RegistryTweakBase
    {
        public TestRegistryTweak() : base(NullLogger.Instance) { }

        public override TweakDefinition Definition { get; } = new()
        {
            Id = "test.registry.tweak",
            DisplayName = "Test Registry Tweak",
            Category = TweakCategory.Performance,
            ShortDescription = "Test",
            DetailedExplanation = "Test tweak used only in unit tests against a safe HKCU key.",
            ExpectedBenefit = "Test",
            PotentialDisadvantages = "Test",
            RiskLevel = TweakRisk.Safe,
            SupportedWindowsBuilds = new() { ["10"] = 10240 }
        };

        protected override RegistryHive Hive => RegistryHive.CurrentUser;
        protected override string SubKeyPath => TestSubKey;
        protected override string ValueName => TestValueName;
        protected override object DesiredValue => 42;
    }

    private readonly TestRegistryTweak _tweak;
    private readonly SystemSnapshot _snapshot;

    public RegistryTweakBaseTests()
    {
        _tweak = new TestRegistryTweak();
        _snapshot = new SystemSnapshot { OsBuildNumber = 22621 };
        CleanTestKey();
    }

    public void Dispose() => CleanTestKey();

    private static void CleanTestKey()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                TestSubKey, throwOnMissingSubKey: false);
        }
        catch { /* best-effort */ }
    }

    // ─── Apply tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_WhenKeyDoesNotExist_CreatesKeyAndSetsValue()
    {
        var record = await _tweak.ApplyAsync(_snapshot, dryRun: false);

        record.Result.Should().Be(TweakResult.AppliedAndVerified,
            because: "a clean write to a new key should be verified successfully");
        record.NewValue.Should().Be("42");
        // OriginalValue should be null (key was absent), encoded as null in new format
        record.OriginalValue.Should().BeNull(
            because: "value did not exist before; absence is represented as null");
    }

    [Fact]
    public async Task Apply_DryRun_DoesNotWriteRegistry()
    {
        var record = await _tweak.ApplyAsync(_snapshot, dryRun: true);

        record.Result.Should().Be(TweakResult.DryRun);

        using var key = Registry.CurrentUser.OpenSubKey(TestSubKey);
        key.Should().BeNull("dry run must not create the registry key");
    }

    [Fact]
    public async Task Apply_CapturesOriginalValueAndKindBeforeOverwrite()
    {
        // Pre-set a value with a specific kind
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue(TestValueName, 99, RegistryValueKind.DWord);

        var record = await _tweak.ApplyAsync(_snapshot, dryRun: false);

        record.Result.Should().Be(TweakResult.AppliedAndVerified);
        // New format: "DWord:99"
        record.OriginalValue.Should().Be("DWord:99",
            because: "backup must encode both the kind and the value for type-preserving rollback");
        record.NewValue.Should().Be("42");
    }

    [Fact]
    public async Task Apply_VerifiesWriteReadBack_ReturnsAppliedAndVerified()
    {
        // Normal apply — the read-back should confirm the write
        var record = await _tweak.ApplyAsync(_snapshot, dryRun: false);
        record.Result.Should().Be(TweakResult.AppliedAndVerified);

        // Confirm the registry actually has the value
        using var key = Registry.CurrentUser.OpenSubKey(TestSubKey);
        var actual = key?.GetValue(TestValueName);
        actual.Should().Be(42, because: "the registry must contain the desired value after apply");
    }

    // ─── Verify tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_ReturnsTrueWhenValueMatches()
    {
        await _tweak.ApplyAsync(_snapshot, dryRun: false);
        var verified = await _tweak.VerifyAsync(_snapshot);
        verified.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_ReturnsFalseWhenValueDoesNotMatch()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue(TestValueName, 0, RegistryValueKind.DWord);

        var verified = await _tweak.VerifyAsync(_snapshot);
        verified.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_ReturnsFalseWhenKeyAbsent()
    {
        // Key was never created
        var verified = await _tweak.VerifyAsync(_snapshot);
        verified.Should().BeFalse();
    }

    // ─── IsAlreadyConfigured tests ────────────────────────────────────────

    [Fact]
    public async Task IsAlreadyConfigured_TrueWhenValueIsDesired()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue(TestValueName, 42, RegistryValueKind.DWord);

        var alreadyOk = await _tweak.IsAlreadyConfiguredAsync(_snapshot);
        alreadyOk.Should().BeTrue();
    }

    [Fact]
    public async Task IsAlreadyConfigured_FalseWhenValueDiffers()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue(TestValueName, 0, RegistryValueKind.DWord);

        var alreadyOk = await _tweak.IsAlreadyConfiguredAsync(_snapshot);
        alreadyOk.Should().BeFalse();
    }

    [Fact]
    public async Task IsAlreadyConfigured_FalseWhenKeyAbsent()
    {
        var alreadyOk = await _tweak.IsAlreadyConfiguredAsync(_snapshot);
        alreadyOk.Should().BeFalse();
    }

    // ─── Rollback tests ───────────────────────────────────────────────────

    [Fact]
    public async Task Rollback_RestoresOriginalDWordValue()
    {
        // Original state: value = 5 as DWord
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue(TestValueName, 5, RegistryValueKind.DWord);

        var applyRecord = await _tweak.ApplyAsync(_snapshot, dryRun: false);
        applyRecord.OriginalValue.Should().Be("DWord:5",
            because: "backup must capture both the value and its registry kind");

        var rollbackRecord = await _tweak.RollbackAsync(applyRecord);
        rollbackRecord.Result.Should().Be(TweakResult.RolledBack);

        // Verify the registry has the restored value
        using var verifyKey = Registry.CurrentUser.OpenSubKey(TestSubKey);
        var current = verifyKey?.GetValue(TestValueName);
        current.Should().Be(5, because: "rollback must restore the exact original integer value");
    }

    [Fact]
    public async Task Rollback_WhenOriginalValueWasAbsent_DeletesValue()
    {
        // Value did not exist before apply
        var applyRecord = await _tweak.ApplyAsync(_snapshot, dryRun: false);
        applyRecord.OriginalValue.Should().BeNull(
            because: "absence must be recorded as null, not as a default value");

        var rollbackRecord = await _tweak.RollbackAsync(applyRecord);
        rollbackRecord.Result.Should().Be(TweakResult.RolledBack);

        // Value should be deleted
        using var verifyKey = Registry.CurrentUser.OpenSubKey(TestSubKey);
        var current = verifyKey?.GetValue(TestValueName);
        current.Should().BeNull("rollback of an absent-original must delete the value, not write a default");
    }

    [Fact]
    public async Task Rollback_PreservesOriginalRegistryValueKind()
    {
        // Write original as a String kind (unusual for DWORD tweaks, but must be preserved)
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue(TestValueName, "5", RegistryValueKind.String);

        var applyRecord = await _tweak.ApplyAsync(_snapshot, dryRun: false);
        // Backup should record the kind as String, not DWord
        applyRecord.OriginalValue.Should().StartWith("String:",
            because: "the original registry kind must be captured, not assumed");

        var rollbackRecord = await _tweak.RollbackAsync(applyRecord);
        rollbackRecord.Result.Should().Be(TweakResult.RolledBack);

        // Kind should be restored as String
        using var verifyKey = Registry.CurrentUser.OpenSubKey(TestSubKey);
        if (verifyKey is not null)
        {
            var restoredKind = verifyKey.GetValueKind(TestValueName);
            restoredKind.Should().Be(RegistryValueKind.String,
                because: "rollback must restore the original registry value kind, not coerce to DWord");
        }
    }

    // ─── InspectCurrentState tests ────────────────────────────────────────

    [Fact]
    public async Task InspectCurrentState_ReturnsCurrentValue()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue(TestValueName, 7, RegistryValueKind.DWord);

        var value = await _tweak.InspectCurrentStateAsync(_snapshot);
        value.Should().Be("7");
    }

    [Fact]
    public async Task InspectCurrentState_ReturnsNullWhenKeyAbsent()
    {
        var value = await _tweak.InspectCurrentStateAsync(_snapshot);
        value.Should().BeNull();
    }
}

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine.Tweaks;
using Xunit;

namespace SaturnTweaks.Tests.Unit;

// ════════════════════════════════════════════════════════════════════════════
// GRAPHICS TWEAKS
// ════════════════════════════════════════════════════════════════════════════

public sealed class DisableFullscreenOptimizationsTweakTests
{
    private readonly DisableFullscreenOptimizationsTweak _tweak =
        new(NullLogger<DisableFullscreenOptimizationsTweak>.Instance);

    [Fact]
    public void Definition_HasCorrectId() =>
        _tweak.Definition.Id.Should().Be("graphics.fullscreen.disableoptimizations");

    [Fact]
    public void Definition_DoesNotRequireAdmin() =>
        _tweak.Definition.RequiresAdminPrivileges.Should().BeFalse();

    [Fact]
    public void Definition_RiskIsLow() =>
        _tweak.Definition.RiskLevel.Should().Be(TweakRisk.Low);

    [Fact]
    public void Definition_NotInRecommendedPreset() =>
        _tweak.Definition.IncludedInRecommendedPreset.Should().BeFalse();

    [Fact]
    public void Definition_ExplanationMentionsFSO() =>
        _tweak.Definition.DetailedExplanation.Should()
            .ContainAny("fullscreen", "fullscreen optimization", "borderless");

    [Fact]
    public async Task IsApplicable_AnySystem_ReturnsTrue()
    {
        var result = await _tweak.IsApplicableAsync(new SystemSnapshot { OsBuildNumber = 19041 });
        result.Should().BeTrue();
    }
}

public sealed class DisableVrrTearingTweakTests
{
    private readonly DisableVrrTearingTweak _tweak =
        new(NullLogger<DisableVrrTearingTweak>.Instance);

    [Fact]
    public void Definition_HasCorrectId() =>
        _tweak.Definition.Id.Should().Be("graphics.vrr.disabletearing");

    [Fact]
    public async Task IsApplicable_OlderBuild_ReturnsFalse()
    {
        var snap = new SystemSnapshot { OsBuildNumber = 18363 };
        (await _tweak.IsApplicableAsync(snap)).Should().BeFalse();
    }

    [Fact]
    public async Task IsApplicable_Build19041_ReturnsTrue()
    {
        var snap = new SystemSnapshot { OsBuildNumber = 19041 };
        (await _tweak.IsApplicableAsync(snap)).Should().BeTrue();
    }

    [Fact]
    public void Definition_DisadvantagesExplainVrrEffect() =>
        _tweak.Definition.PotentialDisadvantages.Should()
            .ContainAny("G-Sync", "FreeSync", "VRR");
}

public sealed class PreferDiscreteGpuTweakTests
{
    private readonly PreferDiscreteGpuForAppsTweak _tweak =
        new(NullLogger<PreferDiscreteGpuForAppsTweak>.Instance);

    [Fact]
    public async Task IsApplicable_NoDiscreteGpu_ReturnsFalse()
    {
        var snap = new SystemSnapshot
        {
            OsBuildNumber = 19041,
            Gpus = new List<GpuInfo>
            {
                new() { Name = "Intel UHD", Vendor = "Intel", IsIntegrated = true }
            }
        };
        (await _tweak.IsApplicableAsync(snap)).Should().BeFalse();
    }

    [Fact]
    public async Task IsApplicable_DiscreteGpuPresent_ReturnsTrue()
    {
        var snap = new SystemSnapshot
        {
            OsBuildNumber = 19041,
            Gpus = new List<GpuInfo>
            {
                new() { Name = "RTX 4070", Vendor = "NVIDIA", IsIntegrated = false }
            }
        };
        (await _tweak.IsApplicableAsync(snap)).Should().BeTrue();
    }
}

// ════════════════════════════════════════════════════════════════════════════
// INPUT TWEAKS
// ════════════════════════════════════════════════════════════════════════════

public sealed class NeutralPointerSpeedTweakTests
{
    private readonly NeutralPointerSpeedTweak _tweak =
        new(NullLogger<NeutralPointerSpeedTweak>.Instance);

    [Fact]
    public void Definition_HasCorrectId() =>
        _tweak.Definition.Id.Should().Be("input.mouse.neutralspeed");

    [Fact]
    public void Definition_DesiredValueIs10() =>
        // Neutral Windows pointer speed slider midpoint
        _tweak.Definition.DetailedExplanation.Should().Contain("10");

    [Fact]
    public void Definition_RiskIsSafe() =>
        _tweak.Definition.RiskLevel.Should().Be(TweakRisk.Safe);

    [Fact]
    public void Definition_ExplainsRawInputNotAffected() =>
        _tweak.Definition.DetailedExplanation.Should()
            .ContainAny("raw input", "Raw Input", "WM_MOUSEMOVE");
}

public sealed class DisableUsbSelectiveSuspendTweakTests
{
    private readonly DisableUsbSelectiveSuspendTweak _tweak =
        new(NullLogger<DisableUsbSelectiveSuspendTweak>.Instance);

    [Fact]
    public void Definition_HasCorrectId() =>
        _tweak.Definition.Id.Should().Be("input.usb.disableselectivesuspend");

    [Fact]
    public void Definition_RequiresAdmin() =>
        _tweak.Definition.RequiresAdminPrivileges.Should().BeTrue();

    [Fact]
    public async Task IsApplicable_StandardUser_ReturnsFalse()
    {
        var snap = new SystemSnapshot { IsRunningAsAdministrator = false };
        (await _tweak.IsApplicableAsync(snap)).Should().BeFalse();
    }

    [Fact]
    public async Task IsApplicable_Admin_ReturnsTrue()
    {
        var snap = new SystemSnapshot { IsRunningAsAdministrator = true };
        (await _tweak.IsApplicableAsync(snap)).Should().BeTrue();
    }
}

public sealed class FlattenMouseCurvesTweakTests
{
    private readonly FlattenMouseCurvesTweak _tweak =
        new(NullLogger<FlattenMouseCurvesTweak>.Instance);

    [Fact]
    public void Definition_HasCorrectId() =>
        _tweak.Definition.Id.Should().Be("input.mouse.flatcurves");

    [Fact]
    public void Definition_RiskIsSafe() =>
        _tweak.Definition.RiskLevel.Should().Be(TweakRisk.Safe);

    [Fact]
    public void Definition_DoesNotRequireAdmin() =>
        _tweak.Definition.RequiresAdminPrivileges.Should().BeFalse();

    [Fact]
    public async Task Apply_DryRun_ReturnsDryRunResult()
    {
        var record = await _tweak.ApplyAsync(new SystemSnapshot(), dryRun: true);
        record.Result.Should().Be(TweakResult.DryRun);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// MEMORY TWEAKS
// ════════════════════════════════════════════════════════════════════════════

public sealed class DisableLargeSystemCacheTweakTests
{
    private readonly DisableLargeSystemCacheTweak _tweak =
        new(NullLogger<DisableLargeSystemCacheTweak>.Instance);

    [Fact]
    public void Definition_HasCorrectId() =>
        _tweak.Definition.Id.Should().Be("memory.cache.apppriority");

    [Fact]
    public void Definition_RequiresRestart() =>
        _tweak.Definition.RequiresRestart.Should().BeTrue();

    [Fact]
    public void Definition_ExplainsThatZeroIsCorrectDefault() =>
        _tweak.Definition.DetailedExplanation.Should()
            .ContainAny("workstation", "default", "0");
}

public sealed class DisableClearPageFileTweakTests
{
    private readonly DisableClearPageFileTweak _tweak =
        new(NullLogger<DisableClearPageFileTweak>.Instance);

    [Fact]
    public void Definition_HasCorrectId() =>
        _tweak.Definition.Id.Should().Be("memory.pagefile.cleardisable");

    [Fact]
    public void Definition_RiskIsSafe() =>
        _tweak.Definition.RiskLevel.Should().Be(TweakRisk.Safe);
}

public sealed class DisableSysMainTweakTests
{
    private readonly DisableSysMainTweak _tweak =
        new(NullLogger<DisableSysMainTweak>.Instance);

    [Fact]
    public void Definition_HasCorrectId() =>
        _tweak.Definition.Id.Should().Be("advanced.sysmain.disable");

    [Fact]
    public void Definition_IsModerateRisk() =>
        _tweak.Definition.RiskLevel.Should().Be(TweakRisk.Moderate);

    [Fact]
    public async Task IsApplicable_HddWithLowRam_ReturnsFalse()
    {
        // SysMain is beneficial on HDD — must NOT be offered
        var snap = new SystemSnapshot
        {
            IsRunningAsAdministrator = true,
            TotalPhysicalMemoryBytes = 8L * 1024 * 1024 * 1024, // 8 GB
            Drives = new List<DriveInfo2>
            {
                new() { DriveType = "HDD", TotalBytes = 1_000_000_000, FreeBytes = 500_000_000 }
            }
        };
        (await _tweak.IsApplicableAsync(snap)).Should().BeFalse();
    }

    [Fact]
    public async Task IsApplicable_SsdWithHighRam_ReturnsTrue()
    {
        var snap = new SystemSnapshot
        {
            IsRunningAsAdministrator = true,
            TotalPhysicalMemoryBytes = 32L * 1024 * 1024 * 1024, // 32 GB
            Drives = new List<DriveInfo2>
            {
                new() { DriveType = "SSD", TotalBytes = 1_000_000_000, FreeBytes = 500_000_000 }
            }
        };
        (await _tweak.IsApplicableAsync(snap)).Should().BeTrue();
    }

    [Fact]
    public async Task IsApplicable_StandardUser_ReturnsFalse()
    {
        var snap = new SystemSnapshot
        {
            IsRunningAsAdministrator = false,
            TotalPhysicalMemoryBytes = 32L * 1024 * 1024 * 1024,
            Drives = new List<DriveInfo2>
            {
                new() { DriveType = "SSD", TotalBytes = 1_000_000_000, FreeBytes = 500_000_000 }
            }
        };
        (await _tweak.IsApplicableAsync(snap)).Should().BeFalse();
    }

    [Fact]
    public void Definition_ExplanationWarnsAboutHdd() =>
        _tweak.Definition.DetailedExplanation.Should()
            .ContainAny("HDD", "hard disk", "rotational");
}

// ════════════════════════════════════════════════════════════════════════════
// ADVANCED TWEAKS
// ════════════════════════════════════════════════════════════════════════════

public sealed class Win32PrioritySeparationTweakTests
{
    private readonly Win32PrioritySeparationTweak _tweak =
        new(NullLogger<Win32PrioritySeparationTweak>.Instance);

    [Fact]
    public void Definition_HasCorrectId() =>
        _tweak.Definition.Id.Should().Be("advanced.scheduling.win32priority");

    [Fact]
    public void Definition_IsModerateRisk() =>
        _tweak.Definition.RiskLevel.Should().Be(TweakRisk.Moderate);

    [Fact]
    public void Definition_RequiresRestart() =>
        _tweak.Definition.RequiresRestart.Should().BeTrue();

    [Fact]
    public void Definition_RequiresAdmin() =>
        _tweak.Definition.RequiresAdminPrivileges.Should().BeTrue();

    [Fact]
    public void Definition_NotInRecommendedPreset() =>
        _tweak.Definition.IncludedInRecommendedPreset.Should().BeFalse();

    [Fact]
    public void Definition_ExplainsGpuBoundScenarios() =>
        _tweak.Definition.PotentialDisadvantages.Should()
            .ContainAny("background", "context-switch", "GPU-bound");
}

public sealed class Irq8PriorityTweakTests
{
    private readonly Irq8PriorityTweak _tweak =
        new(NullLogger<Irq8PriorityTweak>.Instance);

    [Fact]
    public void Definition_HasCorrectId() =>
        _tweak.Definition.Id.Should().Be("advanced.irq8.highpriority");

    [Fact]
    public void Definition_IsLowRisk() =>
        _tweak.Definition.RiskLevel.Should().Be(TweakRisk.Low);

    [Fact]
    public void Definition_RequiresRestart() =>
        _tweak.Definition.RequiresRestart.Should().BeTrue();
}

// ════════════════════════════════════════════════════════════════════════════
// TWEAK DEFINITION AUDIT — verify all tweaks meet content requirements
// ════════════════════════════════════════════════════════════════════════════

public sealed class AllTweakDefinitionAuditTests
{
    public static IEnumerable<object[]> AllTweaks() =>
        new ITweakExecutor[]
        {
            new HagsEnableTweak(NullLogger<HagsEnableTweak>.Instance),
            new GameModeEnableTweak(NullLogger<GameModeEnableTweak>.Instance),
            new GameDvrDisableTweak(NullLogger<GameDvrDisableTweak>.Instance),
            new VisualEffectsPerformanceTweak(NullLogger<VisualEffectsPerformanceTweak>.Instance),
            new DisableMouseAccelerationTweak(NullLogger<DisableMouseAccelerationTweak>.Instance),
            new MmcssGamingPriorityTweak(NullLogger<MmcssGamingPriorityTweak>.Instance),
            new NetworkThrottlingDisableTweak(NullLogger<NetworkThrottlingDisableTweak>.Instance),
            new PowerPlanTweak(NullLogger<PowerPlanTweak>.Instance),
            new DisableFullscreenOptimizationsTweak(NullLogger<DisableFullscreenOptimizationsTweak>.Instance),
            new DisableVrrTearingTweak(NullLogger<DisableVrrTearingTweak>.Instance),
            new PreferDiscreteGpuForAppsTweak(NullLogger<PreferDiscreteGpuForAppsTweak>.Instance),
            new FlattenMouseCurvesTweak(NullLogger<FlattenMouseCurvesTweak>.Instance),
            new DisableUsbSelectiveSuspendTweak(NullLogger<DisableUsbSelectiveSuspendTweak>.Instance),
            new NeutralPointerSpeedTweak(NullLogger<NeutralPointerSpeedTweak>.Instance),
            new DisableLargeSystemCacheTweak(NullLogger<DisableLargeSystemCacheTweak>.Instance),
            new DisableClearPageFileTweak(NullLogger<DisableClearPageFileTweak>.Instance),
            new Irq8PriorityTweak(NullLogger<Irq8PriorityTweak>.Instance),
            new Win32PrioritySeparationTweak(NullLogger<Win32PrioritySeparationTweak>.Instance),
            new DisableSysMainTweak(NullLogger<DisableSysMainTweak>.Instance),
        }.Select(t => new object[] { t }).ToArray();

    [Theory]
    [MemberData(nameof(AllTweaks))]
    public void AllTweaks_HaveNonEmptyId(ITweakExecutor tweak) =>
        tweak.Definition.Id.Should().NotBeNullOrWhiteSpace(
            because: $"{tweak.GetType().Name} must have a non-empty ID");

    [Theory]
    [MemberData(nameof(AllTweaks))]
    public void AllTweaks_IdFollowsDotConvention(ITweakExecutor tweak) =>
        tweak.Definition.Id.Should().Contain(".",
            because: $"{tweak.GetType().Name} ID must be dot-separated: category.subsystem.action");

    [Theory]
    [MemberData(nameof(AllTweaks))]
    public void AllTweaks_HaveNonEmptyDisplayName(ITweakExecutor tweak) =>
        tweak.Definition.DisplayName.Should().NotBeNullOrWhiteSpace();

    [Theory]
    [MemberData(nameof(AllTweaks))]
    public void AllTweaks_HaveDetailedExplanation_MinLength(ITweakExecutor tweak) =>
        tweak.Definition.DetailedExplanation.Length.Should().BeGreaterThan(100,
            because: $"{tweak.Definition.Id} explanation is too short to be meaningful");

    [Theory]
    [MemberData(nameof(AllTweaks))]
    public void AllTweaks_HaveNonEmptyPotentialDisadvantages(ITweakExecutor tweak) =>
        tweak.Definition.PotentialDisadvantages.Should().NotBeNullOrWhiteSpace(
            because: $"{tweak.Definition.Id} must honestly state trade-offs");

    [Theory]
    [MemberData(nameof(AllTweaks))]
    public void AllTweaks_SupportedBuildsNotEmpty_IfNotAlwaysApplicable(ITweakExecutor tweak)
    {
        // All tweaks must declare at least one supported Windows build
        tweak.Definition.SupportedWindowsBuilds.Should().NotBeEmpty(
            because: $"{tweak.Definition.Id} must specify supported Windows builds");
    }

    [Fact]
    public void AllTweaks_IdsAreUnique_WithinSet()
    {
        var allIds = AllTweaks()
            .Select(arr => ((ITweakExecutor)arr[0]).Definition.Id)
            .ToList();
        allIds.Should().OnlyHaveUniqueItems("all tweak IDs must be unique");
    }
}

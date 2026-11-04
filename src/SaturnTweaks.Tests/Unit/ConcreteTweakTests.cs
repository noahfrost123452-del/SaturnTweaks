using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine.Tweaks;
using Xunit;

namespace SaturnTweaks.Tests.Unit;

public sealed class HagsEnableTweakTests
{
    private readonly HagsEnableTweak _tweak = new(NullLogger<HagsEnableTweak>.Instance);

    [Fact]
    public async Task IsApplicable_Win10Build19041WithDiscreteGpu_ReturnsTrue()
    {
        var snapshot = new SystemSnapshot
        {
            OsBuildNumber = 19041,
            Gpus = new List<GpuInfo>
            {
                new() { Name = "NVIDIA RTX 3070", Vendor = "NVIDIA", IsIntegrated = false }
            }
        };

        var result = await _tweak.IsApplicableAsync(snapshot);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsApplicable_OlderBuild_ReturnsFalse()
    {
        var snapshot = new SystemSnapshot
        {
            OsBuildNumber = 18363, // 1909 — before HAGS
            Gpus = new List<GpuInfo>
            {
                new() { Name = "NVIDIA GTX 1060", Vendor = "NVIDIA", IsIntegrated = false }
            }
        };

        var result = await _tweak.IsApplicableAsync(snapshot);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsApplicable_IntegratedGpuOnly_ReturnsFalse()
    {
        var snapshot = new SystemSnapshot
        {
            OsBuildNumber = 22621,
            Gpus = new List<GpuInfo>
            {
                new() { Name = "Intel UHD 630", Vendor = "Intel", IsIntegrated = true }
            }
        };

        var result = await _tweak.IsApplicableAsync(snapshot);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsApplicable_NoGpus_ReturnsFalse()
    {
        var snapshot = new SystemSnapshot
        {
            OsBuildNumber = 22621,
            Gpus = new List<GpuInfo>()
        };

        var result = await _tweak.IsApplicableAsync(snapshot);
        result.Should().BeFalse();
    }

    [Fact]
    public void Definition_HasCorrectId()
    {
        _tweak.Definition.Id.Should().Be("perf.hags.enable");
    }

    [Fact]
    public void Definition_RequiresRestart()
    {
        _tweak.Definition.RequiresRestart.Should().BeTrue();
    }

    [Fact]
    public void Definition_RequiresAdmin()
    {
        _tweak.Definition.RequiresAdminPrivileges.Should().BeTrue();
    }

    [Fact]
    public void Definition_NotInRecommendedPreset()
    {
        // HAGS is conditional — not blanket recommended
        _tweak.Definition.IncludedInRecommendedPreset.Should().BeFalse();
    }
}

public sealed class GameModeEnableTweakTests
{
    private readonly GameModeEnableTweak _tweak = new(NullLogger<GameModeEnableTweak>.Instance);

    [Fact]
    public async Task IsApplicable_AnyModernWindows_ReturnsTrue()
    {
        var snapshot = new SystemSnapshot { OsBuildNumber = 19041 };
        var result = await _tweak.IsApplicableAsync(snapshot);
        result.Should().BeTrue();
    }

    [Fact]
    public void Definition_DoesNotRequireAdmin()
    {
        _tweak.Definition.RequiresAdminPrivileges.Should().BeFalse();
    }

    [Fact]
    public void Definition_DoesNotRequireRestart()
    {
        _tweak.Definition.RequiresRestart.Should().BeFalse();
    }

    [Fact]
    public void Definition_IsInRecommendedPreset()
    {
        _tweak.Definition.IncludedInRecommendedPreset.Should().BeTrue();
    }

    [Fact]
    public void Definition_RiskIsSafe()
    {
        _tweak.Definition.RiskLevel.Should().Be(TweakRisk.Safe);
    }
}

public sealed class GameDvrDisableTweakTests
{
    private readonly GameDvrDisableTweak _tweak = new(NullLogger<GameDvrDisableTweak>.Instance);

    [Fact]
    public void Definition_RiskIsSafe() => _tweak.Definition.RiskLevel.Should().Be(TweakRisk.Safe);

    [Fact]
    public void Definition_IsInRecommendedPreset() => _tweak.Definition.IncludedInRecommendedPreset.Should().BeTrue();

    [Fact]
    public void Definition_NoAdminNeeded() => _tweak.Definition.RequiresAdminPrivileges.Should().BeFalse();
}

public sealed class PowerPlanTweakTests
{
    private readonly PowerPlanTweak _tweak = new(NullLogger<PowerPlanTweak>.Instance);

    [Fact]
    public void Definition_RiskIsModerate()
    {
        // High performance has real trade-offs (heat, power) — must be Moderate, not Safe
        _tweak.Definition.RiskLevel.Should().Be(TweakRisk.Moderate);
    }

    [Fact]
    public void Definition_NotInRecommendedPreset()
    {
        // Laptop vs desktop distinction means it can't be blanket recommended
        _tweak.Definition.IncludedInRecommendedPreset.Should().BeFalse();
    }

    [Fact]
    public async Task IsApplicable_Always_ReturnsTrue()
    {
        // User makes the laptop/desktop trade-off decision — we don't block
        var result = await _tweak.IsApplicableAsync(new SystemSnapshot());
        result.Should().BeTrue();
    }
}

public sealed class NetworkThrottlingTweakTests
{
    private readonly NetworkThrottlingDisableTweak _tweak =
        new(NullLogger<NetworkThrottlingDisableTweak>.Instance);

    [Fact]
    public void Definition_RequiresAdmin()
    {
        _tweak.Definition.RequiresAdminPrivileges.Should().BeTrue();
    }

    [Fact]
    public void Definition_NotInRecommendedPreset()
    {
        _tweak.Definition.IncludedInRecommendedPreset.Should().BeFalse();
    }

    [Fact]
    public void Definition_DetailedExplanationMentionsPing()
    {
        // Must honestly state this does NOT lower in-game ping
        _tweak.Definition.DetailedExplanation.Should().ContainAny("ping", "latency", "does not");
    }
}

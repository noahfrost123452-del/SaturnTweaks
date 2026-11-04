using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine;
using Xunit;

namespace SaturnTweaks.Tests.Unit;

// ════════════════════════════════════════════════════════════════════════════
// MonitorSample model tests
// ════════════════════════════════════════════════════════════════════════════

public sealed class MonitorSampleTests
{
    [Fact]
    public void MemoryDisplay_GbRange_FormatsCorrectly()
    {
        var sample = new MonitorSample { MemoryUsedBytes = 8L * 1_073_741_824 };
        sample.MemoryDisplay.Should().Be("8.0 GB");
    }

    [Fact]
    public void MemoryDisplay_MbRange_FormatsCorrectly()
    {
        var sample = new MonitorSample { MemoryUsedBytes = 512L * 1_048_576 };
        sample.MemoryDisplay.Should().Be("512 MB");
    }

    [Fact]
    public void GpuPercent_IsNullByDefault()
    {
        // GPU utilization is not fabricated — must be null without vendor SDK
        var sample = new MonitorSample();
        sample.GpuPercent.Should().BeNull(
            because: "GPU utilization requires NVAPI/ADL — must not be fabricated");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// MetricBaseline tests
// ════════════════════════════════════════════════════════════════════════════

public sealed class MetricBaselineTests
{
    [Fact]
    public void DefaultBaseline_HasZeroSampleCount()
    {
        var baseline = new MetricBaseline();
        baseline.SampleCount.Should().Be(0);
    }

    [Fact]
    public void Baseline_WithLabel_PreservesLabel()
    {
        var baseline = new MetricBaseline { Label = "Before Optimization" };
        baseline.Label.Should().Be("Before Optimization");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// MonitoringService.GenerateReport static method tests
// These test the report generation without requiring live counters.
// ════════════════════════════════════════════════════════════════════════════

public sealed class GenerateReportTests
{
    private static TweakOperationRecord MakeRecord(string id, TweakResult result,
        string? orig = "0", string? newVal = "1") => new()
    {
        TweakId = id,
        DisplayName = $"Tweak {id}",
        Result = result,
        OriginalValue = orig,
        NewValue = newVal,
        SessionId = "test_session"
    };

    [Fact]
    public void GenerateReport_NoBaselines_StillProducesReport()
    {
        var before = new MetricBaseline { Label = "Not captured", SampleCount = 0 };
        var after = new MetricBaseline { Label = "Not captured", SampleCount = 0 };
        var records = new List<TweakOperationRecord>
        {
            MakeRecord("t1", TweakResult.AppliedAndVerified),
            MakeRecord("t2", TweakResult.Failed)
        };

        var report = MonitoringService.GenerateReport(before, after, records);

        report.Should().NotBeNullOrWhiteSpace();
        report.Should().Contain("SATURN TWEAKS");
        report.Should().Contain("Applied:  1");
        report.Should().Contain("Failed:   1");
    }

    [Fact]
    public void GenerateReport_WithBaselines_IncludesMetrics()
    {
        var before = new MetricBaseline
        {
            Label = "Before",
            SampleCount = 5,
            CpuAvgPercent = 25.0,
            CpuPeakPercent = 40.0,
            MemoryAvgBytes = 8L * 1_073_741_824
        };
        var after = new MetricBaseline
        {
            Label = "After",
            SampleCount = 5,
            CpuAvgPercent = 20.0,
            CpuPeakPercent = 35.0,
            MemoryAvgBytes = 7L * 1_073_741_824
        };
        var records = new List<TweakOperationRecord>
        {
            MakeRecord("t1", TweakResult.AppliedAndVerified)
        };

        var report = MonitoringService.GenerateReport(before, after, records);

        report.Should().Contain("BEFORE / AFTER");
        report.Should().Contain("25.0%");
        report.Should().Contain("20.0%");
    }

    [Fact]
    public void GenerateReport_AlreadyConfiguredNotCounted_AsApplied()
    {
        var before = new MetricBaseline { SampleCount = 0 };
        var after = new MetricBaseline { SampleCount = 0 };
        var records = new List<TweakOperationRecord>
        {
            MakeRecord("t1", TweakResult.AlreadyConfigured),
            MakeRecord("t2", TweakResult.AlreadyConfigured),
            MakeRecord("t3", TweakResult.AppliedAndVerified)
        };

        var report = MonitoringService.GenerateReport(before, after, records);

        report.Should().Contain("Applied:  1");
        report.Should().NotContain("Applied:  3");
    }

    [Fact]
    public void GenerateReport_RequiresRestartRecord_IsIncludedAsApplied()
    {
        var before = new MetricBaseline { SampleCount = 0 };
        var after = new MetricBaseline { SampleCount = 0 };
        var records = new List<TweakOperationRecord>
        {
            MakeRecord("t1", TweakResult.RequiresRestart)
        };

        var report = MonitoringService.GenerateReport(before, after, records);

        report.Should().Contain("Applied:  1");
        report.Should().Contain("Restart required");
    }

    [Fact]
    public void GenerateReport_NeverFabricatesGpuData()
    {
        var before = new MetricBaseline { SampleCount = 5, CpuAvgPercent = 30 };
        var after = new MetricBaseline { SampleCount = 5, CpuAvgPercent = 25 };
        var records = new List<TweakOperationRecord>();

        var report = MonitoringService.GenerateReport(before, after, records);

        // Must state GPU data is unavailable, not invent numbers
        Assert.True(
            new[] { "GPU utilization", "vendor SDK", "NVAPI", "unavailable" }
                .Any(term => report.Contains(term, StringComparison.OrdinalIgnoreCase)),
            "report must not fabricate GPU data");
    }

    [Fact]
    public void GenerateReport_IncludesMeasurementLimitations()
    {
        var before = new MetricBaseline { SampleCount = 3 };
        var after = new MetricBaseline { SampleCount = 3 };
        var records = new List<TweakOperationRecord>();

        var report = MonitoringService.GenerateReport(before, after, records);

        // Must acknowledge that desktop metrics ≠ in-game performance
        Assert.True(
            new[] { "in-game", "frame-time", "environmental" }
                .Any(term => report.Contains(term, StringComparison.OrdinalIgnoreCase)),
            "report must acknowledge measurement limitations");
    }

    [Fact]
    public void GenerateReportJson_ProducesValidJson()
    {
        var records = new List<TweakOperationRecord>
        {
            MakeRecord("t1", TweakResult.AppliedAndVerified)
        };

        var json = MonitoringService.GenerateReportJson(null, null, records);

        // Should be parseable JSON
        var action = () => System.Text.Json.JsonDocument.Parse(json);
        action.Should().NotThrow();
    }

    [Fact]
    public void GenerateReportJson_ContainsNotes()
    {
        var json = MonitoringService.GenerateReportJson(null, null,
            new List<TweakOperationRecord>());

        json.Should().Contain("Notes");
        json.Should().ContainAny("NVAPI", "ADL", "vendor SDK");
    }
}

// ════════════════════════════════════════════════════════════════════════════
// GameLibraryService tests — file system operations only (no registry)
// ════════════════════════════════════════════════════════════════════════════

public sealed class GameLibraryServiceTests : IAsyncLifetime
{
    private GameLibraryService _service = null!;
    private string _testBackupRoot = "";

    public async Task InitializeAsync()
    {
        _testBackupRoot = Path.Combine(Path.GetTempPath(), $"SaturnGameTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testBackupRoot);

        // Override backup root via environment for test isolation
        Environment.SetEnvironmentVariable("SATURN_GAME_BACKUP_DIR", _testBackupRoot);
        _service = new GameLibraryService(NullLogger<GameLibraryService>.Instance);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("SATURN_GAME_BACKUP_DIR", null);
        try { Directory.Delete(_testBackupRoot, recursive: true); } catch { }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DetectGames_ReturnsListWithoutThrowing()
    {
        // Detection may return empty list if no launchers installed — that is valid
        var games = await _service.DetectGamesAsync();
        games.Should().NotBeNull();
    }

    [Fact]
    public async Task DetectGames_AllResultsHaveNames()
    {
        var games = await _service.DetectGamesAsync();
        foreach (var game in games)
            game.Name.Should().NotBeNullOrWhiteSpace(
                because: $"all detected games must have a name (found: {game.InstallPath})");
    }

    [Fact]
    public async Task BackupGameConfig_NonExistentPath_ReturnsNull()
    {
        var game = new DetectedGame
        {
            Name = "NonExistentGame",
            InstallPath = Path.Combine(_testBackupRoot, "DoesNotExist"),
            IsInstalled = false
        };

        var backup = await _service.BackupGameConfigAsync(game);
        backup.Should().BeNull(
            because: "a game with a non-existent install path has nothing to back up");
    }

    [Fact]
    public async Task BackupGameConfig_GameWithConfigs_CreatesBackup()
    {
        // Create a fake game directory with a config file
        var gameDir = Path.Combine(_testBackupRoot, "TestGame");
        Directory.CreateDirectory(gameDir);
        await File.WriteAllTextAsync(Path.Combine(gameDir, "settings.ini"),
            "[Graphics]\nResolution=1920x1080");

        var game = new DetectedGame
        {
            Name = "TestGame",
            InstallPath = gameDir,
            IsInstalled = true
        };

        var backup = await _service.BackupGameConfigAsync(game);

        backup.Should().NotBeNull();
        backup!.GameName.Should().Be("TestGame");
        backup.OriginalPaths.Should().HaveCount(1);
        File.Exists(Path.Combine(backup.BackupPath, "settings.ini")).Should().BeTrue();
    }

    [Fact]
    public async Task GetBackupsForGame_AfterBackup_ReturnsThatBackup()
    {
        var gameDir = Path.Combine(_testBackupRoot, "BackupListGame");
        Directory.CreateDirectory(gameDir);
        await File.WriteAllTextAsync(Path.Combine(gameDir, "config.cfg"), "key=value");

        var game = new DetectedGame
        {
            Name = "BackupListGame",
            InstallPath = gameDir,
            IsInstalled = true
        };

        await _service.BackupGameConfigAsync(game);
        var backups = await _service.GetBackupsForGameAsync("BackupListGame");

        backups.Should().HaveCount(1);
        backups[0].GameName.Should().Be("BackupListGame");
    }

    [Fact]
    public async Task GetBackupsForGame_NoBackups_ReturnsEmpty()
    {
        var backups = await _service.GetBackupsForGameAsync("GameThatWasNeverBacked");
        backups.Should().BeEmpty();
    }

    [Fact]
    public void DetectedGame_SourceDisplay_ReflectsEnum()
    {
        var steamGame = new DetectedGame { Source = GameSource.Steam, Name = "Test" };
        steamGame.SourceDisplay.Should().Be("Steam");

        var epicGame = new DetectedGame { Source = GameSource.Epic, Name = "Test" };
        epicGame.SourceDisplay.Should().Be("Epic");

        var manualGame = new DetectedGame { Source = GameSource.Manual, Name = "Test" };
        manualGame.SourceDisplay.Should().Be("Manual");
    }
}

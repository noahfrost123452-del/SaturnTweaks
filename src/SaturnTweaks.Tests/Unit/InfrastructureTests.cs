using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine;
using Xunit;

namespace SaturnTweaks.Tests.Unit;

public sealed class SystemSnapshotTests
{
    [Theory]
    [InlineData(22000, true)]   // Win11 RTM
    [InlineData(22621, true)]   // Win11 22H2
    [InlineData(19041, false)]  // Win10 2004
    [InlineData(17763, false)]  // Win10 1809
    public void IsWindows11_DerivedFromBuildNumber(int build, bool expected)
    {
        var snapshot = new SystemSnapshot { OsBuildNumber = build };
        snapshot.IsWindows11.Should().Be(expected);
    }

    [Fact]
    public void MemoryUsagePercent_CalculatesCorrectly()
    {
        var snapshot = new SystemSnapshot
        {
            TotalPhysicalMemoryBytes = 16L * 1024 * 1024 * 1024, // 16 GB
            AvailablePhysicalMemoryBytes = 4L * 1024 * 1024 * 1024  // 4 GB free
        };

        snapshot.MemoryUsagePercent.Should().BeApproximately(75.0, precision: 0.01);
    }

    [Fact]
    public void MemoryUsagePercent_ZeroTotal_ReturnsZero()
    {
        var snapshot = new SystemSnapshot
        {
            TotalPhysicalMemoryBytes = 0,
            AvailablePhysicalMemoryBytes = 0
        };

        snapshot.MemoryUsagePercent.Should().Be(0);
    }

    [Fact]
    public void DriveInfo_FreePercent_CalculatesCorrectly()
    {
        var drive = new DriveInfo2
        {
            TotalBytes = 1_000_000_000L,
            FreeBytes = 250_000_000L
        };

        drive.FreePercent.Should().BeApproximately(25.0, precision: 0.01);
    }
}

public sealed class TweakStateTests
{
    [Theory]
    [InlineData(TweakResult.AppliedAndVerified, true)]
    [InlineData(TweakResult.RequiresRestart, true)]
    [InlineData(TweakResult.Failed, false)]
    [InlineData(TweakResult.NotApplicable, false)]
    [InlineData(TweakResult.AlreadyConfigured, false)]
    public void IsApplied_TrueForAppliedStates(TweakResult result, bool expectedIsApplied)
    {
        var state = new TweakState { TweakId = "t", LastResult = result };
        state.IsApplied.Should().Be(expectedIsApplied);
    }

    [Fact]
    public void CanRollBack_TrueWhenAppliedAndHasOriginalValue()
    {
        var state = new TweakState
        {
            TweakId = "t",
            LastResult = TweakResult.AppliedAndVerified,
            OriginalValueSnapshot = "0"
        };
        state.CanRollBack.Should().BeTrue();
    }

    [Fact]
    public void CanRollBack_FalseWhenNoOriginalValue()
    {
        var state = new TweakState
        {
            TweakId = "t",
            LastResult = TweakResult.AppliedAndVerified,
            OriginalValueSnapshot = null
        };
        state.CanRollBack.Should().BeFalse();
    }
}

public sealed class ChangeHistoryRepositoryTests : IAsyncLifetime
{
    private ChangeHistoryRepository _repo = null!;
    private string _testDir = "";

    public async Task InitializeAsync()
    {
        // Override storage path via environment for test isolation
        _testDir = Path.Combine(Path.GetTempPath(), $"SaturnTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        Environment.SetEnvironmentVariable("SATURN_HISTORY_DIR", _testDir);
        _repo = new ChangeHistoryRepository(NullLogger<ChangeHistoryRepository>.Instance);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("SATURN_HISTORY_DIR", null);
        try { Directory.Delete(_testDir, recursive: true); } catch { /* best effort */ }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Record_ThenGetAll_ReturnsStoredRecord()
    {
        var record = new TweakOperationRecord
        {
            TweakId = "t.test", DisplayName = "Test",
            Result = TweakResult.AppliedAndVerified,
            SessionId = "session1"
        };

        await _repo.RecordAsync(record);
        var all = await _repo.GetAllRecordsAsync();

        all.Should().ContainSingle(r => r.TweakId == "t.test");
    }

    [Fact]
    public async Task GetSessionRecords_FiltersCorrectly()
    {
        var s1 = new TweakOperationRecord
        {
            TweakId = "a", DisplayName = "A",
            Result = TweakResult.AppliedAndVerified, SessionId = "session_A"
        };
        var s2 = new TweakOperationRecord
        {
            TweakId = "b", DisplayName = "B",
            Result = TweakResult.AppliedAndVerified, SessionId = "session_B"
        };

        await _repo.RecordAsync(s1);
        await _repo.RecordAsync(s2);

        var sessionARecords = await _repo.GetSessionRecordsAsync("session_A");
        sessionARecords.Should().ContainSingle(r => r.TweakId == "a");
        sessionARecords.Should().NotContain(r => r.TweakId == "b");
    }

    [Fact]
    public async Task GetLatestForTweak_ReturnsMostRecent()
    {
        var old = new TweakOperationRecord
        {
            TweakId = "dup", DisplayName = "D",
            Result = TweakResult.AppliedAndVerified,
            Timestamp = DateTimeOffset.UtcNow.AddHours(-2),
            SessionId = "s1"
        };
        var recent = new TweakOperationRecord
        {
            TweakId = "dup", DisplayName = "D",
            Result = TweakResult.RolledBack,
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = "s2"
        };

        await _repo.RecordAsync(old);
        await _repo.RecordAsync(recent);

        var latest = await _repo.GetLatestForTweakAsync("dup");
        latest.Should().NotBeNull();
        latest!.Result.Should().Be(TweakResult.RolledBack);
    }

    [Fact]
    public async Task ExportImport_RoundTrips()
    {
        var record = new TweakOperationRecord
        {
            TweakId = "export.test", DisplayName = "Export Test",
            Result = TweakResult.AppliedAndVerified,
            OriginalValue = "0", NewValue = "1",
            SessionId = "export_session"
        };
        await _repo.RecordAsync(record);

        var exportPath = Path.Combine(_testDir, "export.json");
        await _repo.ExportAsync(exportPath);

        // New repo instance, import the file
        var repo2 = new ChangeHistoryRepository(NullLogger<ChangeHistoryRepository>.Instance);
        await repo2.ImportAsync(exportPath);

        var all = await repo2.GetAllRecordsAsync();
        all.Should().Contain(r => r.TweakId == "export.test" && r.OriginalValue == "0");
    }

    [Fact]
    public async Task DeleteSession_RemovesOnlyThatSession()
    {
        await _repo.RecordAsync(new TweakOperationRecord
        {
            TweakId = "keep", DisplayName = "Keep",
            Result = TweakResult.AppliedAndVerified, SessionId = "keep_session"
        });
        await _repo.RecordAsync(new TweakOperationRecord
        {
            TweakId = "del", DisplayName = "Del",
            Result = TweakResult.AppliedAndVerified, SessionId = "del_session"
        });

        await _repo.DeleteSessionAsync("del_session");

        var all = await _repo.GetAllRecordsAsync();
        all.Should().Contain(r => r.SessionId == "keep_session");
        all.Should().NotContain(r => r.SessionId == "del_session");
    }
}

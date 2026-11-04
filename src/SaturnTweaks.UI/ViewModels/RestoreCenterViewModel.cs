using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine;
using System.Collections.ObjectModel;

namespace SaturnTweaks.UI.ViewModels;

public partial class RestoreCenterViewModel : ObservableObject
{
    private readonly IChangeHistoryRepository _history;
    private readonly TweakOrchestrator _orchestrator;

    [ObservableProperty] private ObservableCollection<SessionGroup> _sessions = new();
    [ObservableProperty] private SessionGroup? _selectedSession;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isRollingBack;
    [ObservableProperty] private string _statusMessage = "";

    public RestoreCenterViewModel(IChangeHistoryRepository history, TweakOrchestrator orchestrator)
    {
        _history = history;
        _orchestrator = orchestrator;
        _ = LoadHistorySafeAsync();
    }

    private async Task LoadHistorySafeAsync()
    {
        try { await LoadHistoryAsync(); }
        catch (Exception ex) { StatusMessage = $"Failed to load history: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        IsLoading = true;
        Sessions.Clear();
        try
        {
            var all = await _history.GetAllRecordsAsync();
            var grouped = all
                .Where(r => r.SessionId is not null)
                .GroupBy(r => r.SessionId!)
                .OrderByDescending(g => g.Max(r => r.Timestamp));

            foreach (var group in grouped)
            {
                Sessions.Add(new SessionGroup
                {
                    SessionId = group.Key,
                    Timestamp = group.Max(r => r.Timestamp),
                    Records = group.ToList(),
                    AppliedCount = group.Count(r => r.Result == TweakResult.AppliedAndVerified),
                    RollbackCount = group.Count(r => r.Result == TweakResult.RolledBack)
                });
            }

            if (!Sessions.Any())
                StatusMessage = "No optimization history found.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load history: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RollbackSessionAsync()
    {
        if (SelectedSession is null) return;

        IsRollingBack = true;
        StatusMessage = $"Rolling back session {SelectedSession.SessionId[..8]}…";
        try
        {
            var results = await _orchestrator.RollbackSessionAsync(SelectedSession.SessionId);
            var succeeded = results.Count(r => r.Result == TweakResult.RolledBack);
            var failed = results.Count(r => r.Result == TweakResult.Failed);
            StatusMessage = $"Rollback complete: {succeeded} restored, {failed} failed.";
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Rollback failed: {ex.Message}";
        }
        finally
        {
            IsRollingBack = false;
        }
    }

    [RelayCommand]
    private async Task ExportHistoryAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Change History",
            Filter = "JSON files (*.json)|*.json",
            FileName = $"saturn_history_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _history.ExportAsync(dialog.FileName);
                StatusMessage = $"Exported to {dialog.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export failed: {ex.Message}";
            }
        }
    }
}

public sealed class SessionGroup
{
    public string SessionId { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public List<TweakOperationRecord> Records { get; set; } = new();
    public int AppliedCount { get; set; }
    public int RollbackCount { get; set; }
    public string DisplayName => $"Session {SessionId[..8]}  ·  {Timestamp.LocalDateTime:MMM d, HH:mm}";
    public string Summary => $"{AppliedCount} applied  ·  {RollbackCount} rolled back";
    public bool CanRollBack => AppliedCount > 0;
}

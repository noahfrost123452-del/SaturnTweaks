using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine;
using System.Collections.ObjectModel;

namespace SaturnTweaks.UI.ViewModels;

public partial class AdvancedViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly TweakOrchestrator _orchestrator;

    [ObservableProperty] private ObservableCollection<TweakRowItem> _tweaks = new();
    [ObservableProperty] private TweakRowItem? _selectedTweak;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _dryRunMode = true; // Advanced defaults to dry-run ON
    [ObservableProperty] private string _sessionSummary = "";
    [ObservableProperty] private bool _userHasAcknowledgedRisk;

    public AdvancedViewModel(MainViewModel main, TweakOrchestrator orchestrator)
    {
        _main = main;
        _orchestrator = orchestrator;
        _main.PropertyChanged += OnMainPropertyChanged;
        if (_main.SystemSnapshot is not null)
            _ = RefreshFromSnapshotSafeAsync();
    }

    private void OnMainPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SystemSnapshot))
            _ = RefreshFromSnapshotSafeAsync();
    }

    private async Task RefreshFromSnapshotSafeAsync()
    {
        try { await LoadTweaksAsync(); }
        catch (Exception ex) { SessionSummary = $"Could not load advanced tweaks: {ex.Message}"; }
    }

    private async Task LoadTweaksAsync()
    {
        if (_main.SystemSnapshot is null) return;
        if (SelectedTweak is not null) SelectedTweak.IsPreviewed = false;
        SelectedTweak = null;
        Tweaks.Clear();
        var snapshot = _main.SystemSnapshot;

        // Advanced section includes Advanced category + any Moderate/High risk from other categories
        var advancedTweaks = _orchestrator.AllTweaks
            .Where(t => t.Definition.Category == TweakCategory.Advanced
                     || t.Definition.RiskLevel >= TweakRisk.Moderate);

        foreach (var exec in advancedTweaks)
        {
            Tweaks.Add(await TweakRowItem.CreateAsync(exec, snapshot));
        }
        SelectedTweak = Tweaks.FirstOrDefault(t => t.IsApplicable) ?? Tweaks.FirstOrDefault();
        if (SelectedTweak is not null) SelectedTweak.IsPreviewed = true;
    }

    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        if (_main.SystemSnapshot is null) return;
        var selectedIds = Tweaks.Where(t => t.IsSelected && t.IsApplicable && !t.IsAlreadyConfigured)
            .Select(t => t.TweakId).ToList();
        if (selectedIds.Count == 0)
        {
            SessionSummary = "Select one or more compatible tweaks. Click a row to preview its proposed change.";
            return;
        }
        if (!UserHasAcknowledgedRisk && !DryRunMode)
        {
            SessionSummary = "Please acknowledge the advanced tweaks warning before applying.";
            return;
        }

        IsRunning = true;
        SessionSummary = "";
        try
        {
            var ids = selectedIds;
            var progress = new Progress<TweakOperationRecord>(record =>
            {
                var row = Tweaks.FirstOrDefault(t => t.TweakId == record.TweakId);
                if (row is not null) row.LastResult = record.Result;
            });

            var session = await _orchestrator.RunSessionAsync(
                _main.SystemSnapshot, ids, DryRunMode, progress);

            SessionSummary = DryRunMode
                ? $"Dry run — {session.AppliedCount} would apply, {session.SkippedCount} not applicable."
                : $"{session.AppliedCount} applied, {session.FailedCount} failed"
                  + (session.RequiresRestart ? " · Restart required." : ".");

            await LoadTweaksAsync();
        }
        finally { IsRunning = false; }
    }
}

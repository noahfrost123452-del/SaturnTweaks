using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine;
using System.Collections.ObjectModel;

namespace SaturnTweaks.UI.ViewModels;

public partial class PerformanceViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly TweakOrchestrator _orchestrator;

    [ObservableProperty] private ObservableCollection<TweakRowItem> _tweaks = new();
    [ObservableProperty] private TweakRowItem? _selectedTweak;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _dryRunMode;
    [ObservableProperty] private string _sessionSummary = "";
    [ObservableProperty] private ObservableCollection<TweakOperationRecord> _lastSessionResults = new();

    public PerformanceViewModel(MainViewModel main, TweakOrchestrator orchestrator)
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
        catch (Exception ex) { SessionSummary = $"Could not load tweaks: {ex.Message}"; }
    }

    private async Task LoadTweaksAsync()
    {
        if (_main.SystemSnapshot is null) return;

        if (SelectedTweak is not null) SelectedTweak.IsPreviewed = false;
        SelectedTweak = null;
        Tweaks.Clear();
        var snapshot = _main.SystemSnapshot;

        var perfAndInputTweaks = _orchestrator.AllTweaks
            .Where(t => t.Definition.Category is TweakCategory.Performance
                                              or TweakCategory.Power
                                              or TweakCategory.Input);

        foreach (var exec in perfAndInputTweaks)
        {
            Tweaks.Add(await TweakRowItem.CreateAsync(
                exec, snapshot, selected: exec.Definition.IncludedInRecommendedPreset));
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
            SessionSummary = "Select at least one compatible tweak. Select a row to preview its exact change before applying.";
            return;
        }
        IsRunning = true;
        LastSessionResults.Clear();
        SessionSummary = "";

        try
        {
            var tweakIds = selectedIds;
            var progress = new Progress<TweakOperationRecord>(record =>
            {
                LastSessionResults.Add(record);
                // Update the row's state
                var row = Tweaks.FirstOrDefault(t => t.TweakId == record.TweakId);
                if (row is not null)
                    row.LastResult = record.Result;
            });

            var session = await _orchestrator.RunSessionAsync(
                _main.SystemSnapshot, tweakIds, DryRunMode, progress);

            SessionSummary = DryRunMode
                ? $"Dry run complete — {session.AppliedCount} would apply, {session.SkippedCount} not applicable."
                : $"Complete — {session.AppliedCount} applied, {session.AlreadyConfiguredCount} already ok, " +
                  $"{session.FailedCount} failed" +
                  (session.RequiresRestart ? " · Restart required for some changes." : ".");

            // Refresh states
            await LoadTweaksAsync();
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var t in Tweaks.Where(t => t.IsApplicable && !t.IsAlreadyConfigured))
            t.IsSelected = true;
    }

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var t in Tweaks)
            t.IsSelected = t.IsApplicable && !t.IsAlreadyConfigured &&
                           _orchestrator.AllTweaks
                               .First(e => e.Definition.Id == t.TweakId)
                               .Definition.IncludedInRecommendedPreset;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var t in Tweaks) t.IsSelected = false;
    }
}


using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine;
using System.Collections.ObjectModel;

namespace SaturnTweaks.UI.ViewModels;

public partial class InputViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly TweakOrchestrator _orchestrator;

    [ObservableProperty] private ObservableCollection<TweakRowItem> _tweaks = new();
    [ObservableProperty] private TweakRowItem? _selectedTweak;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _dryRunMode;
    [ObservableProperty] private string _sessionSummary = "";

    // Detected mouse settings (read-only, real values)
    [ObservableProperty] private string _mouseSensitivity = "—";
    [ObservableProperty] private string _mouseSpeed = "—";
    [ObservableProperty] private string _accelerationStatus = "—";
    [ObservableProperty] private string _pointerSpeedValue = "—";
    [ObservableProperty] private string _rawInputNote = "";

    public InputViewModel(MainViewModel main, TweakOrchestrator orchestrator)
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
        try { ReadMouseSettings(); await LoadTweaksAsync(); }
        catch (Exception ex) { SessionSummary = $"Could not load input settings: {ex.Message}"; }
    }

    private void ReadMouseSettings()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
            if (key is null)
            {
                MouseSensitivity = "Registry unavailable";
                return;
            }

            var speed = key.GetValue("MouseSpeed")?.ToString() ?? "1";
            var sensitivity = key.GetValue("MouseSensitivity")?.ToString() ?? "10";
            var threshold1 = key.GetValue("MouseThreshold1")?.ToString() ?? "6";
            var threshold2 = key.GetValue("MouseThreshold2")?.ToString() ?? "10";

            MouseSpeed = speed;
            PointerSpeedValue = sensitivity;

            // MouseSpeed=0 means acceleration off; 1 or 2 means various accel curves active
            AccelerationStatus = speed switch
            {
                "0" => "Disabled (good for gaming)",
                "1" => "Enabled — threshold1=" + threshold1,
                "2" => "Enabled — threshold1=" + threshold1 + " threshold2=" + threshold2,
                _ => $"Unknown value: {speed}"
            };

            MouseSensitivity = $"Sensitivity: {sensitivity}/20  ·  Speed value: {speed}";

            RawInputNote = "Note: Most modern FPS games use Raw Input and bypass all Windows " +
                           "pointer settings. Check your game's input settings — look for " +
                           "'Raw Input' or 'Mouse Acceleration: Off' in the game's options.";
        }
        catch (Exception)
        {
            MouseSensitivity = "Could not read mouse settings";
        }
    }

    private async Task LoadTweaksAsync()
    {
        if (_main.SystemSnapshot is null) return;
        if (SelectedTweak is not null) SelectedTweak.IsPreviewed = false;
        SelectedTweak = null;
        Tweaks.Clear();
        var snapshot = _main.SystemSnapshot;

        var inputTweaks = _orchestrator.AllTweaks
            .Where(t => t.Definition.Category == TweakCategory.Input);

        foreach (var exec in inputTweaks)
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
            var session = await _orchestrator.RunSessionAsync(_main.SystemSnapshot, ids, DryRunMode, progress);
            SessionSummary = DryRunMode
                ? $"Dry run — {session.AppliedCount} would apply."
                : $"{session.AppliedCount} applied, {session.FailedCount} failed.";
            ReadMouseSettings();
            await LoadTweaksAsync();
        }
        finally { IsRunning = false; }
    }
}

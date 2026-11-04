using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine;
using System.Collections.ObjectModel;

namespace SaturnTweaks.UI.ViewModels;

public partial class GraphicsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly TweakOrchestrator _orchestrator;

    [ObservableProperty] private ObservableCollection<TweakRowItem> _tweaks = new();
    [ObservableProperty] private TweakRowItem? _selectedTweak;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _dryRunMode;
    [ObservableProperty] private string _sessionSummary = "";
    [ObservableProperty] private ObservableCollection<DisplayInfo> _displays = new();
    [ObservableProperty] private ObservableCollection<GpuDetailItem> _gpuDetails = new();

    public SystemSnapshot? Snapshot => _main.SystemSnapshot;

    // Primary GPU display properties
    public string PrimaryGpuName => Snapshot?.Gpus.FirstOrDefault()?.Name ?? "—";
    public string PrimaryGpuVram
    {
        get
        {
            var vram = Snapshot?.Gpus.FirstOrDefault()?.DedicatedVramBytes ?? 0;
            return vram > 0 ? $"{vram / 1_073_741_824.0:F1} GB" : "—";
        }
    }
    public string PrimaryDriverVersion => Snapshot?.Gpus.FirstOrDefault()?.DriverVersion ?? "—";
    public bool HagsEnabled => Snapshot?.HagsEnabled ?? false;
    public bool HagsSupported => Snapshot?.HagsSupported ?? false;
    public bool NvidiaPresent => Snapshot?.NvidiaDriverPresent ?? false;
    public bool AmdPresent => Snapshot?.AmdDriverPresent ?? false;
    public string VendorDisplay
    {
        get
        {
            if (NvidiaPresent) return "NVIDIA";
            if (AmdPresent) return "AMD";
            return Snapshot?.Gpus.FirstOrDefault()?.Vendor ?? "—";
        }
    }
    public string GpuCountDisplay => Snapshot?.Gpus.Count switch
    {
        null or 0 => "None detected",
        1 => "1 GPU detected",
        var n => $"{n} GPUs detected"
    };

    // VBS/HVCI note — these affect GPU scheduler, user should know
    public bool VbsEnabled => Snapshot?.VbsEnabled ?? false;
    public bool HvciEnabled => Snapshot?.HvciEnabled ?? false;

    public GraphicsViewModel(MainViewModel main, TweakOrchestrator orchestrator)
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
        try
        {
            RefreshGpuDetails();
            RefreshDisplays();
            await LoadTweaksAsync();
            OnPropertyChanged(nameof(Snapshot));
            OnPropertyChanged(nameof(PrimaryGpuName));
            OnPropertyChanged(nameof(PrimaryGpuVram));
            OnPropertyChanged(nameof(PrimaryDriverVersion));
            OnPropertyChanged(nameof(HagsEnabled));
            OnPropertyChanged(nameof(HagsSupported));
            OnPropertyChanged(nameof(NvidiaPresent));
            OnPropertyChanged(nameof(AmdPresent));
            OnPropertyChanged(nameof(VendorDisplay));
            OnPropertyChanged(nameof(GpuCountDisplay));
            OnPropertyChanged(nameof(VbsEnabled));
            OnPropertyChanged(nameof(HvciEnabled));
        }
        catch (Exception ex) { SessionSummary = $"Could not load graphics information: {ex.Message}"; }
    }

    private void RefreshGpuDetails()
    {
        GpuDetails.Clear();
        if (Snapshot is null) return;
        foreach (var gpu in Snapshot.Gpus)
        {
            GpuDetails.Add(new GpuDetailItem
            {
                Name = gpu.Name,
                Vendor = gpu.Vendor,
                VramDisplay = gpu.DedicatedVramBytes > 0
                    ? $"{gpu.DedicatedVramBytes / 1_073_741_824.0:F1} GB"
                    : "Shared / Unknown",
                DriverVersion = gpu.DriverVersion,
                IsIntegrated = gpu.IsIntegrated,
                TypeDisplay = gpu.IsIntegrated ? "Integrated" : "Discrete",
                UtilizationDisplay = gpu.UtilizationPercent.HasValue
                    ? $"{gpu.UtilizationPercent.Value:F0}%"
                    : "N/A (requires vendor SDK)"
            });
        }
    }

    private void RefreshDisplays()
    {
        Displays.Clear();
        if (Snapshot is null) return;
        foreach (var monitor in Snapshot.Monitors)
        {
            Displays.Add(new DisplayInfo
            {
                Name = monitor.Name,
                Resolution = $"{monitor.WidthPixels} × {monitor.HeightPixels}",
                RefreshRate = $"{monitor.RefreshRateHz:F0} Hz",
                IsPrimary = monitor.IsPrimary,
                HdrStatus = monitor.HdrEnabled ? "HDR On" : "SDR",
                VrrStatus = monitor.VrrSupported ? "VRR Supported" : "No VRR"
            });
        }
    }

    private async Task LoadTweaksAsync()
    {
        if (Snapshot is null) return;
        if (SelectedTweak is not null) SelectedTweak.IsPreviewed = false;
        SelectedTweak = null;
        Tweaks.Clear();

        var graphicsTweaks = _orchestrator.AllTweaks
            .Where(t => t.Definition.Category == TweakCategory.Graphics);

        foreach (var exec in graphicsTweaks)
        {
            Tweaks.Add(await TweakRowItem.CreateAsync(exec, Snapshot));
        }
        SelectedTweak = Tweaks.FirstOrDefault(t => t.IsApplicable) ?? Tweaks.FirstOrDefault();
        if (SelectedTweak is not null) SelectedTweak.IsPreviewed = true;
    }

    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        if (Snapshot is null) return;
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

            var session = await _orchestrator.RunSessionAsync(Snapshot, ids, DryRunMode, progress);
            SessionSummary = DryRunMode
                ? $"Dry run — {session.AppliedCount} would apply."
                : $"{session.AppliedCount} applied, {session.FailedCount} failed"
                  + (session.RequiresRestart ? " · Restart required." : ".");

            await LoadTweaksAsync();
        }
        finally { IsRunning = false; }
    }
}

public sealed class GpuDetailItem
{
    public string Name { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string VramDisplay { get; set; } = "";
    public string DriverVersion { get; set; } = "";
    public bool IsIntegrated { get; set; }
    public string TypeDisplay { get; set; } = "";
    public string UtilizationDisplay { get; set; } = "";
}

public sealed class DisplayInfo
{
    public string Name { get; set; } = "";
    public string Resolution { get; set; } = "";
    public string RefreshRate { get; set; } = "";
    public bool IsPrimary { get; set; }
    public string HdrStatus { get; set; } = "";
    public string VrrStatus { get; set; } = "";
    public string PrimaryBadge => IsPrimary ? "Primary" : "";
}

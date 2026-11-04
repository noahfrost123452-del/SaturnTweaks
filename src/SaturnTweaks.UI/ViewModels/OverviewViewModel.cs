using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine;
using System.Collections.ObjectModel;

namespace SaturnTweaks.UI.ViewModels;

public partial class OverviewViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly TweakOrchestrator _orchestrator;
    private SystemSnapshot? _lastAnalyzedSnapshot;

    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private ObservableCollection<OptimizationSuggestion> _suggestions = new();
    [ObservableProperty] private int _applicableCount;
    [ObservableProperty] private int _appliedCount;
    [ObservableProperty] private int _pendingCount;

    public SystemSnapshot? Snapshot => _main.SystemSnapshot;
    public string SystemStatus => _main.StatusMessage;
    public bool IsSystemLoading => _main.IsLoading;

    [RelayCommand]
    private async Task RefreshSystemAsync()
    {
        IsAnalyzing = true;
        try { await _main.InitializeAsync(); }
        finally { IsAnalyzing = false; }
    }

    // Formatted display properties derived from the snapshot
    public string CpuDisplay => Snapshot is null ? "—"
        : $"{Snapshot.CpuName} · {Snapshot.CpuCoreCount}C / {Snapshot.CpuThreadCount}T";

    public string GpuDisplay => Snapshot?.Gpus.FirstOrDefault()?.Name ?? "—";

    public string RamDisplay => Snapshot is null ? "—"
        : $"{FormatBytes(Snapshot.TotalPhysicalMemoryBytes)} total · " +
          $"{Snapshot.MemoryUsagePercent:F0}% used";

    public string OsDisplay => Snapshot is null ? "—"
        : $"{Snapshot.OsEdition} · Build {Snapshot.OsBuildNumber} · {Snapshot.OsArchitecture}";

    public string PowerDisplay => Snapshot?.CurrentPowerPlan ?? "—";

    public string StorageDisplay
    {
        get
        {
            if (Snapshot is null) return "—";
            var drives = Snapshot.Drives;
            if (!drives.Any()) return "No drives detected";
            return string.Join("  ·  ", drives.Select(d =>
                $"{d.DriveLetter.TrimEnd('\\')} {d.DriveType} {FormatBytes(d.FreeBytes)} free"));
        }
    }

    public string NetworkDisplay
    {
        get
        {
            if (Snapshot is null) return "—";
            var adapter = Snapshot.NetworkAdapters.FirstOrDefault(a => a.IsConnected);
            if (adapter is null) return "No active connection";
            var speed = adapter.LinkSpeedBps > 0
                ? $" · {FormatBits(adapter.LinkSpeedBps)}"
                : "";
            return $"{adapter.ConnectionType}{speed} · {adapter.IpAddress}";
        }
    }

    public string StartupAppDisplay => Snapshot is null ? "—"
        : $"{Snapshot.StartupApplicationCount} startup items";

    public OverviewViewModel(MainViewModel main, TweakOrchestrator orchestrator)
    {
        _main = main;
        _orchestrator = orchestrator;
        _main.PropertyChanged += OnMainPropertyChanged;
        if (_main.SystemSnapshot is not null)
        {
            RefreshDisplayProperties();
            _ = AnalyzeSnapshotSafeAsync();
        }
    }

    private void OnMainPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SystemSnapshot))
        {
            RefreshDisplayProperties();
            _ = AnalyzeSnapshotSafeAsync();
        }
        else if (e.PropertyName == nameof(MainViewModel.StatusMessage))
        {
            OnPropertyChanged(nameof(SystemStatus));
        }
        else if (e.PropertyName == nameof(MainViewModel.IsLoading))
        {
            OnPropertyChanged(nameof(IsSystemLoading));
        }
    }

    private async Task AnalyzeSnapshotSafeAsync()
    {
        var current = Snapshot;
        if (current is null || ReferenceEquals(current, _lastAnalyzedSnapshot))
            return;

        _lastAnalyzedSnapshot = current;
        try { await AnalyzePcAsync(); }
        catch (Exception ex)
        {
            Suggestions.Clear();
            Suggestions.Add(new OptimizationSuggestion
            {
                Title = "Analysis could not complete",
                Description = ex.Message,
                Risk = TweakRisk.Moderate,
                Category = "Diagnostics",
                IsObservation = true
            });
        }
    }

    private void RefreshDisplayProperties()
    {
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(CpuDisplay));
        OnPropertyChanged(nameof(GpuDisplay));
        OnPropertyChanged(nameof(RamDisplay));
        OnPropertyChanged(nameof(OsDisplay));
        OnPropertyChanged(nameof(PowerDisplay));
        OnPropertyChanged(nameof(StorageDisplay));
        OnPropertyChanged(nameof(NetworkDisplay));
        OnPropertyChanged(nameof(StartupAppDisplay));
    }

    [RelayCommand]
    private async Task AnalyzePcAsync()
    {
        if (Snapshot is null) return;
        IsAnalyzing = true;
        Suggestions.Clear();

        try
        {
            var applicable = await _orchestrator.GetApplicableRecommendedTweaksAsync(Snapshot);
            ApplicableCount = applicable.Count;
            PendingCount = applicable.Count;

            foreach (var tweak in applicable)
            {
                Suggestions.Add(new OptimizationSuggestion
                {
                    TweakId = tweak.Definition.Id,
                    Title = tweak.Definition.DisplayName,
                    Description = tweak.Definition.ShortDescription,
                    Risk = tweak.Definition.RiskLevel,
                    Category = tweak.Definition.Category.ToString()
                });
            }

            // System-level observations — all use Snapshot (uppercase, consistent)
            if (Snapshot.IsOnBattery && Snapshot.IsLaptop)
                Suggestions.Add(new OptimizationSuggestion
                {
                    Title = "Running on Battery",
                    Description = "Gaming performance is typically reduced on battery. Connect to AC for full performance.",
                    Risk = TweakRisk.Safe,
                    Category = "Power",
                    IsObservation = true
                });

            if (Snapshot.Drives.Any(d => d.FreePercent < 10))
                Suggestions.Add(new OptimizationSuggestion
                {
                    Title = "Low Storage Space",
                    Description = "One or more drives have less than 10% free space, which may affect performance.",
                    Risk = TweakRisk.Moderate,
                    Category = "Storage",
                    IsObservation = true
                });

            // Fixed: was referencing lowercase `snapshot` alias (now removed)
            if (!Snapshot.IsRunningAsAdministrator)
                Suggestions.Add(new OptimizationSuggestion
                {
                    Title = "Administrator Privileges Not Available",
                    Description = "Some optimizations require administrator privileges. Re-launch as administrator to access all tweaks.",
                    Risk = TweakRisk.Safe,
                    Category = "System",
                    IsObservation = true
                });
        }
        catch (Exception ex)
        {
            Suggestions.Add(new OptimizationSuggestion
            {
                Title = "Analysis incomplete",
                Description = ex.Message,
                Risk = TweakRisk.Moderate,
                Category = "Diagnostics",
                IsObservation = true
            });
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }

    private static string FormatBits(long bps)
    {
        if (bps >= 1_000_000_000) return $"{bps / 1_000_000_000.0:F1} Gbps";
        if (bps >= 1_000_000) return $"{bps / 1_000_000.0:F0} Mbps";
        return $"{bps / 1000.0:F0} Kbps";
    }
}

public sealed class OptimizationSuggestion
{
    public string TweakId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public TweakRisk Risk { get; set; }
    public string Category { get; set; } = "";
    public bool IsObservation { get; set; }

    public string RiskLabel => Risk.ToString();
    public string RiskColor => Risk switch
    {
        TweakRisk.Safe     => "#22C55E",
        TweakRisk.Low      => "#84CC16",
        TweakRisk.Moderate => "#F59E0B",
        TweakRisk.High     => "#F97316",
        TweakRisk.Critical => "#EF4444",
        _                  => "#6B698A"
    };
}

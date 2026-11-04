using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine;
using System.Collections.ObjectModel;

namespace SaturnTweaks.UI.ViewModels;

public partial class DiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly MonitoringService _monitor;
    private readonly IChangeHistoryRepository _history;
    private MetricBaseline? _beforeBaseline;

    [ObservableProperty] private bool _isMonitoring;
    [ObservableProperty] private string _cpuDisplay = "—";
    [ObservableProperty] private string _memoryDisplay = "—";
    [ObservableProperty] private string _diskDisplay = "—";
    [ObservableProperty] private string _gpuNote = "GPU utilization requires vendor SDK (NVAPI/ADL) — not available in this build.";
    [ObservableProperty] private string _monitoringStatus = "Monitoring stopped.";
    [ObservableProperty] private bool _isCapturingBaseline;
    [ObservableProperty] private string _baselineStatus = "No baseline captured.";
    [ObservableProperty] private string _reportText = "";
    [ObservableProperty] private bool _hasReport;

    // Rolling chart data — last 60 samples
    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> MemoryHistory { get; } = new();

    public DiagnosticsViewModel(MonitoringService monitor, IChangeHistoryRepository history)
    {
        _monitor = monitor;
        _history = history;

        _monitor.SampleRecorded += OnSampleRecorded;
    }

    private void OnSampleRecorded(object? sender, MonitorSample sample)
    {
        CpuDisplay = $"{sample.CpuPercent:F1}%";
        MemoryDisplay = sample.MemoryDisplay;
        DiskDisplay = $"R: {FormatBps(sample.DiskReadBytesPerSec)}  W: {FormatBps(sample.DiskWriteBytesPerSec)}";

        CpuHistory.Add(sample.CpuPercent);
        if (CpuHistory.Count > 60) CpuHistory.RemoveAt(0);

        var memGb = sample.MemoryUsedBytes / 1_073_741_824.0;
        MemoryHistory.Add(memGb);
        if (MemoryHistory.Count > 60) MemoryHistory.RemoveAt(0);
    }

    [RelayCommand]
    private void ToggleMonitoring()
    {
        if (IsMonitoring)
        {
            _monitor.Stop();
            IsMonitoring = false;
            MonitoringStatus = $"Stopped. {_monitor.Samples.Count} samples collected.";
        }
        else
        {
            _monitor.ClearSamples();
            CpuHistory.Clear();
            MemoryHistory.Clear();
            _monitor.Start(intervalMs: 1000);
            IsMonitoring = true;
            MonitoringStatus = "Monitoring — sampling every 1 second.";
        }
    }

    [RelayCommand]
    private async Task CaptureBeforeBaselineAsync()
    {
        if (!IsMonitoring)
        {
            // Auto-start monitoring for the capture window
            _monitor.Start();
            IsMonitoring = true;
        }

        IsCapturingBaseline = true;
        BaselineStatus = "Capturing BEFORE baseline (5 seconds)…";
        try
        {
            _beforeBaseline = await _monitor.CaptureBaselineAsync("Before Optimization", 5000);
            BaselineStatus = $"BEFORE captured: avg CPU {_beforeBaseline.CpuAvgPercent:F1}%  " +
                             $"mem {FormatBytes(_beforeBaseline.MemoryAvgBytes)}  " +
                             $"({_beforeBaseline.SampleCount} samples)";
        }
        finally
        {
            IsCapturingBaseline = false;
        }
    }

    [RelayCommand]
    private async Task GenerateReportAsync()
    {
        IsCapturingBaseline = true;
        BaselineStatus = "Capturing AFTER baseline (5 seconds)…";
        try
        {
            MetricBaseline? afterBaseline = null;
            if (IsMonitoring)
                afterBaseline = await _monitor.CaptureBaselineAsync("After Optimization", 5000);

            // Get the most recent session records
            var allRecords = await _history.GetAllRecordsAsync();
            var recentSession = allRecords
                .Where(r => r.SessionId is not null)
                .GroupBy(r => r.SessionId!)
                .OrderByDescending(g => g.Max(r => r.Timestamp))
                .FirstOrDefault()
                ?.ToList() ?? new List<TweakOperationRecord>();

            ReportText = MonitoringService.GenerateReport(
                _beforeBaseline ?? new MetricBaseline { Label = "Not captured", SampleCount = 0 },
                afterBaseline ?? new MetricBaseline { Label = "Not captured", SampleCount = 0 },
                recentSession);

            HasReport = true;
            BaselineStatus = afterBaseline is not null
                ? $"AFTER captured: avg CPU {afterBaseline.CpuAvgPercent:F1}%  mem {FormatBytes(afterBaseline.MemoryAvgBytes)}"
                : "No AFTER baseline — start monitoring first.";
        }
        finally
        {
            IsCapturingBaseline = false;
        }
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        if (!HasReport) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Optimization Report",
            Filter = "Text files (*.txt)|*.txt|JSON files (*.json)|*.json",
            FileName = $"saturn_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            if (dialog.FilterIndex == 2)
            {
                // JSON export
                var allRecords = await _history.GetAllRecordsAsync();
                var recentSession = allRecords
                    .Where(r => r.SessionId is not null)
                    .GroupBy(r => r.SessionId!)
                    .OrderByDescending(g => g.Max(r => r.Timestamp))
                    .FirstOrDefault()
                    ?.ToList() ?? new List<TweakOperationRecord>();

                var json = MonitoringService.GenerateReportJson(_beforeBaseline, null, recentSession);
                await File.WriteAllTextAsync(dialog.FileName, json);
            }
            else
            {
                await File.WriteAllTextAsync(dialog.FileName, ReportText);
            }

            BaselineStatus = $"Exported to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            BaselineStatus = $"Export failed: {ex.Message}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F0} MB";
        return $"{bytes / 1_024.0:F0} KB";
    }

    private static string FormatBps(double bps)
    {
        if (bps >= 1_048_576) return $"{bps / 1_048_576.0:F1} MB/s";
        if (bps >= 1_024) return $"{bps / 1_024.0:F0} KB/s";
        return $"{bps:F0} B/s";
    }

    public void Dispose()
    {
        _monitor.SampleRecorded -= OnSampleRecorded;
        _monitor.Dispose();
    }
}

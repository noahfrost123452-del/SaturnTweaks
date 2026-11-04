using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine;
using System.Collections.ObjectModel;
using System.Management;

namespace SaturnTweaks.UI.ViewModels;

public partial class MemoryStorageViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly TweakOrchestrator _orchestrator;

    [ObservableProperty] private ObservableCollection<TweakRowItem> _tweaks = new();
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _dryRunMode;
    [ObservableProperty] private string _sessionSummary = "";

    // Memory
    [ObservableProperty] private string _totalRamDisplay = "—";
    [ObservableProperty] private string _usedRamDisplay = "—";
    [ObservableProperty] private double _ramUsagePercent;
    [ObservableProperty] private string _pageFileStatus = "—";
    [ObservableProperty] private ObservableCollection<ProcessMemoryItem> _topProcesses = new();

    // Storage
    [ObservableProperty] private ObservableCollection<DriveDisplayItem> _drives = new();

    // Temp cleanup
    [ObservableProperty] private ObservableCollection<TempFolderItem> _tempFolders = new();
    [ObservableProperty] private string _totalTempSize = "—";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isCleaning;
    [ObservableProperty] private string _cleanupResult = "";

    public MemoryStorageViewModel(MainViewModel main, TweakOrchestrator orchestrator)
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
            RefreshMemoryDisplay();
            RefreshDriveDisplay();
            await LoadTweaksAsync();
        }
        catch (Exception ex) { SessionSummary = $"Could not load memory/storage information: {ex.Message}"; }
    }

    private void RefreshMemoryDisplay()
    {
        var snap = _main.SystemSnapshot;
        if (snap is null) return;

        TotalRamDisplay = FormatBytes(snap.TotalPhysicalMemoryBytes);
        var used = snap.TotalPhysicalMemoryBytes - snap.AvailablePhysicalMemoryBytes;
        UsedRamDisplay = $"{FormatBytes(used)} used of {FormatBytes(snap.TotalPhysicalMemoryBytes)}";
        RamUsagePercent = snap.MemoryUsagePercent;
        ReadPageFileStatus();
        RefreshTopProcesses();
    }

    private void ReadPageFileStatus()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PageFileUsage");
            var parts = new List<string>();
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "pagefile";
                var used = Convert.ToInt64(obj["CurrentUsage"] ?? 0L);
                var peak = Convert.ToInt64(obj["PeakUsage"] ?? 0L);
                parts.Add($"{name}: {used} MB used, {peak} MB peak");
            }
            PageFileStatus = parts.Count > 0
                ? string.Join("  ·  ", parts)
                : "System-managed (no fixed size)";
        }
        catch
        {
            PageFileStatus = "Could not read page file information";
        }
    }

    private void RefreshTopProcesses()
    {
        TopProcesses.Clear();
        try
        {
            var procs = System.Diagnostics.Process.GetProcesses()
                .Where(p => { try { return p.WorkingSet64 > 0; } catch { return false; } })
                .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } })
                .Take(8);

            foreach (var p in procs)
            {
                try
                {
                    TopProcesses.Add(new ProcessMemoryItem
                    {
                        Name = p.ProcessName,
                        MemoryDisplay = FormatBytes(p.WorkingSet64),
                        MemoryBytes = p.WorkingSet64
                    });
                }
                catch { /* process may have exited */ }
            }
        }
        catch { /* GetProcesses failed */ }
    }

    private void RefreshDriveDisplay()
    {
        Drives.Clear();
        if (_main.SystemSnapshot is null) return;

        foreach (var d in _main.SystemSnapshot.Drives)
        {
            Drives.Add(new DriveDisplayItem
            {
                DriveLetter = d.DriveLetter,
                Label = string.IsNullOrWhiteSpace(d.Label) ? "Local Drive" : d.Label,
                DriveType = d.DriveType,
                FileSystem = d.FileSystem,
                TotalDisplay = FormatBytes(d.TotalBytes),
                FreeDisplay = FormatBytes(d.FreeBytes),
                FreePercent = d.FreePercent,
                UsedPercent = 100 - d.FreePercent,
                HealthStatus = d.SmartStatus ?? "Unknown",
                LowSpaceWarning = d.FreePercent < 10
            });
        }
    }

    private async Task LoadTweaksAsync()
    {
        if (_main.SystemSnapshot is null) return;
        Tweaks.Clear();
        var snapshot = _main.SystemSnapshot;

        var memTweaks = _orchestrator.AllTweaks
            .Where(t => t.Definition.Category == TweakCategory.Memory);

        foreach (var exec in memTweaks)
        {
            Tweaks.Add(await TweakRowItem.CreateAsync(exec, snapshot));
        }
    }

    [RelayCommand]
    private async Task ScanTempFilesAsync()
    {
        IsScanning = true;
        TempFolders.Clear();
        TotalTempSize = "Scanning…";
        CleanupResult = "";

        try
        {
            var folders = GetTempFolderPaths();
            long totalBytes = 0;

            foreach (var (label, path) in folders)
            {
                if (!Directory.Exists(path)) continue;
                long size = 0;
                int count = 0;
                try
                {
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    count = files.Length;
                    size = files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
                }
                catch { /* access denied — skip */ }

                totalBytes += size;
                TempFolders.Add(new TempFolderItem
                {
                    Label = label,
                    Path = path,
                    FileCount = count,
                    SizeDisplay = FormatBytes(size),
                    SizeBytes = size,
                    IsSelected = size > 0
                });
            }

            TotalTempSize = $"{FormatBytes(totalBytes)} across {TempFolders.Count} folders";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task CleanSelectedTempAsync()
    {
        var toClean = TempFolders.Where(f => f.IsSelected && f.SizeBytes > 0).ToList();
        if (!toClean.Any())
        {
            CleanupResult = "No folders selected.";
            return;
        }

        IsCleaning = true;
        CleanupResult = "Cleaning…";
        long freed = 0;
        int errors = 0;

        try
        {
            foreach (var folder in toClean)
            {
                if (!Directory.Exists(folder.Path)) continue;

                foreach (var file in Directory.GetFiles(folder.Path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        freed += fi.Length;
                        File.Delete(file);
                    }
                    catch { errors++; }
                }

                // Delete empty subdirectories (best effort)
                foreach (var dir in Directory.GetDirectories(folder.Path))
                {
                    try { Directory.Delete(dir, recursive: true); }
                    catch { }
                }
            }

            CleanupResult = $"Freed {FormatBytes(freed)}" +
                            (errors > 0 ? $"  ·  {errors} files in use (skipped)" : "  ·  Complete.");

            await ScanTempFilesAsync();
        }
        finally
        {
            IsCleaning = false;
        }
    }

    [RelayCommand]
    private async Task ApplySelectedTweaksAsync()
    {
        if (_main.SystemSnapshot is null) return;
        IsRunning = true;
        SessionSummary = "";
        try
        {
            var ids = Tweaks.Where(t => t.IsSelected && t.IsApplicable).Select(t => t.TweakId);
            var session = await _orchestrator.RunSessionAsync(_main.SystemSnapshot, ids, DryRunMode);
            SessionSummary = DryRunMode
                ? $"Dry run — {session.AppliedCount} would apply."
                : $"{session.AppliedCount} applied, {session.FailedCount} failed.";
            await LoadTweaksAsync();
        }
        finally { IsRunning = false; }
    }

    private static List<(string Label, string Path)> GetTempFolderPaths()
    {
        var winTemp = Path.GetTempPath();
        var sysTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        var prefetch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");

        return new List<(string, string)>
        {
            ("User Temp (%TEMP%)", winTemp),
            ("Windows Temp", sysTemp),
            ("Windows Prefetch", prefetch)
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F0} MB";
        if (bytes >= 1_024) return $"{bytes / 1_024.0:F0} KB";
        return $"{bytes} B";
    }
}

public sealed class DriveDisplayItem
{
    public string DriveLetter { get; set; } = "";
    public string Label { get; set; } = "";
    public string DriveType { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public string TotalDisplay { get; set; } = "";
    public string FreeDisplay { get; set; } = "";
    public double FreePercent { get; set; }
    public double UsedPercent { get; set; }
    public string HealthStatus { get; set; } = "";
    public bool LowSpaceWarning { get; set; }
    public string Summary => $"{DriveLetter}  {Label}  ·  {DriveType}  ·  {FreeDisplay} free of {TotalDisplay}";
}

public sealed class TempFolderItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";
    public int FileCount { get; set; }
    public string SizeDisplay { get; set; } = "";
    public long SizeBytes { get; set; }
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class ProcessMemoryItem
{
    public string Name { get; set; } = "";
    public string MemoryDisplay { get; set; } = "";
    public long MemoryBytes { get; set; }
}

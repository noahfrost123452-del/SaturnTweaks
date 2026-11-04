using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;
using System.Windows.Media;

namespace SaturnTweaks.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISystemDetector _detector;

    [ObservableProperty] private string _currentPage = "Overview";
    [ObservableProperty] private SystemSnapshot? _systemSnapshot;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _statusMessage = "Initializing…";

    // Active-page flags for sidebar Tag binding.
    // The SidebarNavItem ControlTemplate triggers on Tag="Active".
    // These are updated by Navigate() so the correct item highlights.
    public string OverviewActive      => CurrentPage == "Overview"     ? "Active" : "";
    public string PerformanceActive   => CurrentPage == "Performance"  ? "Active" : "";
    public string GraphicsActive      => CurrentPage == "Graphics"     ? "Active" : "";
    public string NetworkActive       => CurrentPage == "Network"      ? "Active" : "";
    public string InputActive         => CurrentPage == "Input"        ? "Active" : "";
    public string MemoryActive        => CurrentPage == "Memory"       ? "Active" : "";
    public string GameProfilesActive  => CurrentPage == "GameProfiles" ? "Active" : "";
    public string AdvancedActive      => CurrentPage == "Advanced"     ? "Active" : "";
    public string DiagnosticsActive   => CurrentPage == "Diagnostics"  ? "Active" : "";
    public string RestoreActive       => CurrentPage == "Restore"      ? "Active" : "";
    public string SettingsActive      => CurrentPage == "Settings"     ? "Active" : "";

    public event EventHandler<string>? NavigationRequested;

    public string AdminStatusText => SystemSnapshot?.IsRunningAsAdministrator == true
        ? "Administrator"
        : "Standard User";

    public Color AdminStatusColor => SystemSnapshot?.IsRunningAsAdministrator == true
        ? Color.FromRgb(0x22, 0xC5, 0x5E)
        : Color.FromRgb(0xF5, 0x9E, 0x0B);

    public MainViewModel(ISystemDetector detector)
    {
        _detector = detector;
    }

    public async Task InitializeAsync()
    {
        StatusMessage = "Detecting hardware…";
        IsLoading = true;
        try
        {
            SystemSnapshot = await _detector.CaptureSnapshotAsync();
            OnPropertyChanged(nameof(AdminStatusText));
            OnPropertyChanged(nameof(AdminStatusColor));
            StatusMessage = "Ready";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Detection error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Navigate(string pageName)
    {
        NavigationRequested?.Invoke(this, pageName);
        // Notify all active-page properties so the sidebar highlights update
        OnPropertyChanged(nameof(OverviewActive));
        OnPropertyChanged(nameof(PerformanceActive));
        OnPropertyChanged(nameof(GraphicsActive));
        OnPropertyChanged(nameof(NetworkActive));
        OnPropertyChanged(nameof(InputActive));
        OnPropertyChanged(nameof(MemoryActive));
        OnPropertyChanged(nameof(GameProfilesActive));
        OnPropertyChanged(nameof(AdvancedActive));
        OnPropertyChanged(nameof(DiagnosticsActive));
        OnPropertyChanged(nameof(RestoreActive));
        OnPropertyChanged(nameof(SettingsActive));
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;

namespace SaturnTweaks.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly string _settingsPath;

    [ObservableProperty] private bool _launchMinimized;
    [ObservableProperty] private bool _showNotifications = true;
    [ObservableProperty] private bool _confirmBeforeApply = true;
    [ObservableProperty] private string _logLevel = "Information";
    [ObservableProperty] private bool _keepLogsForDays30 = true;
    [ObservableProperty] private string _saveStatus = "";

    // App info (not configurable — display only)
    public string AppVersion => "1.0.0";
    public string AppName => "Saturn Tweaks";
    public string DataFolder { get; }

    public SettingsViewModel()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DataFolder = Path.Combine(appData, "SaturnTweaks");
        _settingsPath = Path.Combine(DataFolder, "settings.json");
        Directory.CreateDirectory(DataFolder);
        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var json = File.ReadAllText(_settingsPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("launchMinimized", out var lm))
                LaunchMinimized = lm.GetBoolean();
            if (root.TryGetProperty("showNotifications", out var sn))
                ShowNotifications = sn.GetBoolean();
            if (root.TryGetProperty("confirmBeforeApply", out var cb))
                ConfirmBeforeApply = cb.GetBoolean();
            if (root.TryGetProperty("logLevel", out var ll))
                LogLevel = ll.GetString() ?? "Information";
            if (root.TryGetProperty("keepLogsForDays30", out var kl))
                KeepLogsForDays30 = kl.GetBoolean();
        }
        catch
        {
            // Default values already set — proceed silently
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new
            {
                launchMinimized = LaunchMinimized,
                showNotifications = ShowNotifications,
                confirmBeforeApply = ConfirmBeforeApply,
                logLevel = LogLevel,
                keepLogsForDays30 = KeepLogsForDays30
            };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_settingsPath, json);
            SaveStatus = "Settings saved.";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try { System.Diagnostics.Process.Start("explorer.exe", DataFolder); }
        catch { SaveStatus = "Could not open folder."; }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var historyFile = Path.Combine(DataFolder, "History", "change_history.json");
        if (File.Exists(historyFile))
        {
            try
            {
                await Task.Run(() => File.Delete(historyFile));
                SaveStatus = "Change history cleared.";
            }
            catch (Exception ex)
            {
                SaveStatus = $"Clear failed: {ex.Message}";
            }
        }
        else
        {
            SaveStatus = "No history file found.";
        }
    }
}

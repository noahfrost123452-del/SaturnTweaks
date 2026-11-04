using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaturnTweaks.Engine;
using System.Collections.ObjectModel;

namespace SaturnTweaks.UI.ViewModels;

public partial class GameProfilesViewModel : ObservableObject
{
    private readonly GameLibraryService _libraryService;

    [ObservableProperty] private ObservableCollection<DetectedGame> _games = new();
    [ObservableProperty] private DetectedGame? _selectedGame;
    [ObservableProperty] private ObservableCollection<GameConfigBackup> _backups = new();
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isBackingUp;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _manualGameName = "";
    [ObservableProperty] private string _manualGamePath = "";

    public GameProfilesViewModel(GameLibraryService libraryService)
    {
        _libraryService = libraryService;
    }

    [RelayCommand]
    private async Task ScanLibrariesAsync()
    {
        IsScanning = true;
        StatusMessage = "Scanning Steam, Epic, and Xbox libraries…";
        Games.Clear();
        try
        {
            var found = await _libraryService.DetectGamesAsync();
            foreach (var game in found)
                Games.Add(game);

            StatusMessage = Games.Count > 0
                ? $"Found {Games.Count} games across {Games.Select(g => g.Source).Distinct().Count()} launcher(s)."
                : "No games detected. Try adding manually or ensure launchers are installed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task SelectGameAsync(DetectedGame? game)
    {
        SelectedGame = game;
        Backups.Clear();
        if (game is null) return;

        try
        {
            var backups = await _libraryService.GetBackupsForGameAsync(game.Name);
            foreach (var b in backups)
                Backups.Add(b);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load backups: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BackupSelectedGameAsync()
    {
        if (SelectedGame is null)
        {
            StatusMessage = "Select a game first.";
            return;
        }

        IsBackingUp = true;
        StatusMessage = $"Backing up config files for {SelectedGame.Name}…";
        try
        {
            var backup = await _libraryService.BackupGameConfigAsync(SelectedGame);
            if (backup is not null)
            {
                Backups.Insert(0, backup);
                StatusMessage = $"Backed up {backup.OriginalPaths.Count} file(s) to {backup.BackupPath}";
            }
            else
            {
                StatusMessage = "No config files found to back up for this game.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Backup failed: {ex.Message}";
        }
        finally
        {
            IsBackingUp = false;
        }
    }

    [RelayCommand]
    private void AddManualGame()
    {
        if (string.IsNullOrWhiteSpace(ManualGameName) || string.IsNullOrWhiteSpace(ManualGamePath))
        {
            StatusMessage = "Enter both a game name and install path.";
            return;
        }

        if (!Directory.Exists(ManualGamePath))
        {
            StatusMessage = $"Path does not exist: {ManualGamePath}";
            return;
        }

        Games.Add(new DetectedGame
        {
            Name = ManualGameName.Trim(),
            Source = GameSource.Manual,
            InstallPath = ManualGamePath.Trim(),
            IsInstalled = true
        });

        StatusMessage = $"Added {ManualGameName}.";
        ManualGameName = "";
        ManualGamePath = "";
    }

    [RelayCommand]
    private void BrowseGamePath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Game Executable",
            Filter = "Executable files (*.exe)|*.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            ManualGamePath = Path.GetDirectoryName(dialog.FileName) ?? "";
            if (string.IsNullOrWhiteSpace(ManualGameName))
                ManualGameName = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }
}

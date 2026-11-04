using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Text.Json;

namespace SaturnTweaks.Engine;

/// <summary>
/// Detects installed games from known launcher registrations.
/// Only reads — never modifies game installations or configurations.
/// Game config backup/restore uses copies in %LOCALAPPDATA%\SaturnTweaks\GameBackups.
/// </summary>
public sealed class GameLibraryService
{
    private readonly ILogger<GameLibraryService> _logger;
    private readonly string _backupRoot;

    public GameLibraryService(ILogger<GameLibraryService> logger)
    {
        _logger = logger;
        // SATURN_GAME_BACKUP_DIR env var allows tests to redirect storage to a temp directory
        var overrideDir = Environment.GetEnvironmentVariable("SATURN_GAME_BACKUP_DIR");
        _backupRoot = overrideDir is not null
            ? overrideDir
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SaturnTweaks", "GameBackups");
        Directory.CreateDirectory(_backupRoot);
    }

    // ─── Detection ────────────────────────────────────────────────────────

    public async Task<List<DetectedGame>> DetectGamesAsync(CancellationToken ct = default)
    {
        var games = new List<DetectedGame>();

        await Task.Run(() =>
        {
            games.AddRange(DetectSteamGames());
            games.AddRange(DetectEpicGames());
            games.AddRange(DetectXboxGames());
        }, ct);

        return games.OrderBy(g => g.Name).ToList();
    }

    private List<DetectedGame> DetectSteamGames()
    {
        var results = new List<DetectedGame>();

        try
        {
            // Steam install path from registry
            string? steamPath = null;
            using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            steamPath = steamKey?.GetValue("SteamPath")?.ToString();

            if (steamPath is null || !Directory.Exists(steamPath))
            {
                using var steamKey2 = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\WOW6432Node\Valve\Steam");
                steamPath = steamKey2?.GetValue("InstallPath")?.ToString();
            }

            if (steamPath is null) return results;

            // Parse libraryfolders.vdf to find all Steam library locations
            var libraryFolders = new List<string> { Path.Combine(steamPath, "steamapps") };

            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                var vdfContent = File.ReadAllText(vdfPath);
                // Simple VDF path extraction — look for "path" key values
                var lines = vdfContent.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split('"');
                        if (parts.Length >= 4)
                        {
                            var folderPath = parts[3].Replace(@"\\", @"\");
                            var appsPath = Path.Combine(folderPath, "steamapps");
                            if (Directory.Exists(appsPath) && !libraryFolders.Contains(appsPath))
                                libraryFolders.Add(appsPath);
                        }
                    }
                }
            }

            // Parse .acf (app content files) for game metadata
            foreach (var libFolder in libraryFolders)
            {
                if (!Directory.Exists(libFolder)) continue;

                foreach (var acfFile in Directory.GetFiles(libFolder, "appmanifest_*.acf"))
                {
                    var game = ParseSteamAcf(acfFile);
                    if (game is not null)
                        results.Add(game);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Steam game detection failed.");
        }

        return results;
    }

    private DetectedGame? ParseSteamAcf(string acfPath)
    {
        try
        {
            var content = File.ReadAllText(acfPath);
            var name = ExtractVdfValue(content, "name");
            var appId = ExtractVdfValue(content, "appid");
            var installDir = ExtractVdfValue(content, "installdir");

            if (name is null || installDir is null) return null;

            // Resolve actual path
            var libFolder = Path.GetDirectoryName(acfPath)!;
            var gamePath = Path.Combine(libFolder, "common", installDir);

            return new DetectedGame
            {
                Name = name,
                Source = GameSource.Steam,
                InstallPath = gamePath,
                ExecutableHint = FindLikelyExe(gamePath),
                LibraryId = appId ?? "",
                IsInstalled = Directory.Exists(gamePath)
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractVdfValue(string content, string key)
    {
        var pattern = $"\"{key}\"";
        var idx = content.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        idx += pattern.Length;
        while (idx < content.Length && content[idx] != '"') idx++;
        if (idx >= content.Length) return null;
        idx++; // skip opening quote

        var end = content.IndexOf('"', idx);
        if (end < 0) return null;

        return content[idx..end];
    }

    private List<DetectedGame> DetectEpicGames()
    {
        var results = new List<DetectedGame>();

        try
        {
            // Epic stores manifests in %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests
            var manifestDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (!Directory.Exists(manifestDir)) return results;

            foreach (var manifestFile in Directory.GetFiles(manifestDir, "*.item"))
            {
                try
                {
                    var json = File.ReadAllText(manifestFile);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
                    var installLoc = root.TryGetProperty("InstallLocation", out var il) ? il.GetString() : null;
                    var launchExe = root.TryGetProperty("LaunchExecutable", out var le) ? le.GetString() : null;

                    if (name is null || installLoc is null) continue;

                    var exePath = launchExe is not null
                        ? Path.Combine(installLoc, launchExe)
                        : FindLikelyExe(installLoc);

                    results.Add(new DetectedGame
                    {
                        Name = name,
                        Source = GameSource.Epic,
                        InstallPath = installLoc,
                        ExecutableHint = exePath,
                        IsInstalled = Directory.Exists(installLoc)
                    });
                }
                catch { /* Skip malformed manifest */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Epic Games detection failed.");
        }

        return results;
    }

    private List<DetectedGame> DetectXboxGames()
    {
        var results = new List<DetectedGame>();

        try
        {
            // Xbox Game Pass / Windows Store games installed via package manager
            // We read from the Gaming\Xbox Gaming Overlay registry path for game info
            // Full WinRT enumeration requires UWP APIs — we use a simpler approach:
            // read known install paths from %ProgramFiles%\WindowsApps (requires admin)
            var windowsAppsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps");

            if (!Directory.Exists(windowsAppsPath)) return results;

            try
            {
                foreach (var dir in Directory.GetDirectories(windowsAppsPath))
                {
                    var dirName = Path.GetFileName(dir);
                    // Xbox/MS games typically have "Microsoft." or "Xbox." prefix
                    if (!dirName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) &&
                        !dirName.StartsWith("Xbox", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Skip runtime packages — look for actual games (have AppxManifest.xml)
                    var manifestPath = Path.Combine(dir, "AppxManifest.xml");
                    if (!File.Exists(manifestPath)) continue;

                    var name = ExtractXmlValue(File.ReadAllText(manifestPath), "DisplayName");
                    if (name is null || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                        name = dirName.Split('_')[0];

                    results.Add(new DetectedGame
                    {
                        Name = name,
                        Source = GameSource.Xbox,
                        InstallPath = dir,
                        ExecutableHint = FindLikelyExe(dir),
                        IsInstalled = true
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogDebug("WindowsApps directory requires admin — Xbox game detection skipped.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Xbox game detection failed.");
        }

        return results;
    }

    private static string? ExtractXmlValue(string xml, string tagName)
    {
        var open = $"<{tagName}>";
        var idx = xml.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        idx += open.Length;
        var end = xml.IndexOf($"</{tagName}>", idx, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;
        return xml[idx..end].Trim();
    }

    private static string? FindLikelyExe(string? directory)
    {
        if (directory is null || !Directory.Exists(directory)) return null;

        try
        {
            // Heuristic: largest .exe in the root that isn't an uninstaller/launcher helper
            var exes = Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .Where(f => !f.Name.Contains("unins", StringComparison.OrdinalIgnoreCase)
                         && !f.Name.Contains("setup", StringComparison.OrdinalIgnoreCase)
                         && !f.Name.Contains("redist", StringComparison.OrdinalIgnoreCase)
                         && !f.Name.Contains("crash", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.Length)
                .FirstOrDefault();

            return exes?.FullName;
        }
        catch
        {
            return null;
        }
    }

    // ─── Config backup/restore ─────────────────────────────────────────────

    public async Task<GameConfigBackup?> BackupGameConfigAsync(
        DetectedGame game, CancellationToken ct = default)
    {
        if (game.InstallPath is null) return null;

        var backupDir = Path.Combine(_backupRoot, SanitizeName(game.Name),
            DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(backupDir);

        var configPaths = FindGameConfigs(game);
        var backedUp = new List<string>();

        foreach (var config in configPaths)
        {
            if (!File.Exists(config)) continue;
            var rel = Path.GetFileName(config);
            var dest = Path.Combine(backupDir, rel);
            try
            {
                await Task.Run(() => File.Copy(config, dest, overwrite: true), ct);
                backedUp.Add(config);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not back up {Config}.", config);
            }
        }

        if (backedUp.Count == 0) return null;

        var backup = new GameConfigBackup
        {
            GameName = game.Name,
            BackupPath = backupDir,
            OriginalPaths = backedUp,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var metaPath = Path.Combine(backupDir, "backup_meta.json");
        await File.WriteAllTextAsync(metaPath,
            JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }), ct);

        _logger.LogInformation("Backed up {Count} config files for {Game}.", backedUp.Count, game.Name);
        return backup;
    }

    private static List<string> FindGameConfigs(DetectedGame game)
    {
        var configs = new List<string>();
        if (game.InstallPath is null) return configs;

        // Common config file extensions to back up
        var extensions = new[] { "*.cfg", "*.ini", "*.json", "*.xml", "*.txt" };
        var configDirs = new List<string> { game.InstallPath };

        // Common subdirectory names for game configs
        foreach (var subdir in new[] { "config", "cfg", "settings", "data", "save" })
        {
            var sub = Path.Combine(game.InstallPath, subdir);
            if (Directory.Exists(sub)) configDirs.Add(sub);
        }

        foreach (var dir in configDirs)
        {
            foreach (var ext in extensions)
            {
                try
                {
                    configs.AddRange(Directory.GetFiles(dir, ext, SearchOption.TopDirectoryOnly)
                        .Take(20)); // cap per directory to avoid backing up entire game data
                }
                catch { /* skip inaccessible dirs */ }
            }
        }

        return configs;
    }

    public async Task<List<GameConfigBackup>> GetBackupsForGameAsync(
        string gameName, CancellationToken ct = default)
    {
        var backups = new List<GameConfigBackup>();
        var gameDir = Path.Combine(_backupRoot, SanitizeName(gameName));
        if (!Directory.Exists(gameDir)) return backups;

        foreach (var backupDir in Directory.GetDirectories(gameDir))
        {
            var metaPath = Path.Combine(backupDir, "backup_meta.json");
            if (!File.Exists(metaPath)) continue;

            try
            {
                var json = await File.ReadAllTextAsync(metaPath, ct);
                var backup = JsonSerializer.Deserialize<GameConfigBackup>(json);
                if (backup is not null) backups.Add(backup);
            }
            catch { /* skip malformed */ }
        }

        return backups.OrderByDescending(b => b.CreatedAt).ToList();
    }

    private static string SanitizeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

public sealed class DetectedGame
{
    public string Name { get; set; } = "";
    public GameSource Source { get; set; }
    public string? InstallPath { get; set; }
    public string? ExecutableHint { get; set; }
    public string LibraryId { get; set; } = "";
    public bool IsInstalled { get; set; }
    public string SourceDisplay => Source.ToString();
    public string StatusDisplay => IsInstalled ? "Installed" : "Not Found";
}

public enum GameSource { Steam, Epic, Xbox, Manual }

public sealed class GameConfigBackup
{
    public string GameName { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public List<string> OriginalPaths { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public string DisplayDate => CreatedAt.LocalDateTime.ToString("MMM d, yyyy HH:mm");
}

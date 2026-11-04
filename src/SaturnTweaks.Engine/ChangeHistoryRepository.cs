using System.Text.Json;
using Microsoft.Extensions.Logging;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;

namespace SaturnTweaks.Engine;

/// <summary>
/// Persists tweak operation records as a JSON file in %LOCALAPPDATA%\SaturnTweaks.
/// Thread-safe via a per-file lock.
/// </summary>
public sealed class ChangeHistoryRepository : IChangeHistoryRepository
{
    private readonly ILogger<ChangeHistoryRepository> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public ChangeHistoryRepository(ILogger<ChangeHistoryRepository> logger)
    {
        _logger = logger;
        // SATURN_HISTORY_DIR env var allows tests to redirect storage to a temp directory
        var overrideDir = Environment.GetEnvironmentVariable("SATURN_HISTORY_DIR");
        var dir = overrideDir is not null
            ? overrideDir
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SaturnTweaks", "History");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "change_history.json");
    }

    public async Task RecordAsync(TweakOperationRecord record, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var records = await LoadAllAsync();
            records.Add(record);
            await SaveAllAsync(records);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<TweakOperationRecord>> GetSessionRecordsAsync(
        string sessionId, CancellationToken ct = default)
    {
        var all = await GetAllRecordsAsync(ct);
        return all.Where(r => r.SessionId == sessionId).ToList();
    }

    public async Task<IReadOnlyList<TweakOperationRecord>> GetAllRecordsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return (await LoadAllAsync()).AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TweakOperationRecord?> GetLatestForTweakAsync(string tweakId, CancellationToken ct = default)
    {
        var all = await GetAllRecordsAsync(ct);
        return all.Where(r => r.TweakId == tweakId)
                  .OrderByDescending(r => r.Timestamp)
                  .FirstOrDefault();
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var records = await LoadAllAsync();
            records.RemoveAll(r => r.SessionId == sessionId);
            await SaveAllAsync(records);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ExportAsync(string filePath, CancellationToken ct = default)
    {
        var all = await GetAllRecordsAsync(ct);
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(all, _jsonOptions), ct);
    }

    public async Task<IReadOnlyList<TweakOperationRecord>> ImportAsync(string filePath, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(filePath, ct);
        var imported = JsonSerializer.Deserialize<List<TweakOperationRecord>>(json, _jsonOptions)
                       ?? new List<TweakOperationRecord>();

        await _lock.WaitAsync(ct);
        try
        {
            var existing = await LoadAllAsync();
            existing.AddRange(imported);
            await SaveAllAsync(existing);
        }
        finally
        {
            _lock.Release();
        }

        return imported;
    }

    private async Task<List<TweakOperationRecord>> LoadAllAsync()
    {
        if (!File.Exists(_filePath)) return new List<TweakOperationRecord>();
        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<List<TweakOperationRecord>>(json, _jsonOptions)
                   ?? new List<TweakOperationRecord>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load change history — starting fresh.");
            return new List<TweakOperationRecord>();
        }
    }

    private async Task SaveAllAsync(List<TweakOperationRecord> records)
    {
        var json = JsonSerializer.Serialize(records, _jsonOptions);
        var tmp = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmp, json);
        File.Move(tmp, _filePath, overwrite: true);
    }
}

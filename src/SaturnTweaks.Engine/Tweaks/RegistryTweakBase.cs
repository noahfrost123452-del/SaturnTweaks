using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;

namespace SaturnTweaks.Engine.Tweaks;

/// <summary>
/// Base for tweaks that read/write a single Windows registry value.
/// Subclasses provide the definition and desired value; this handles
/// backup, apply, verify, and rollback uniformly.
///
/// VERIFICATION CONTRACT: ApplyAsync performs a read-back after SetValue and
/// returns VerificationInconclusive rather than AppliedAndVerified if the
/// value does not match after writing. Never reports success from a write
/// that was not confirmed by a subsequent read.
///
/// ROLLBACK CONTRACT: RollbackAsync preserves the original registry value type
/// by capturing the RegistryValueKind at backup time. When the original value
/// was absent, it deletes the value rather than writing a default.
/// </summary>
public abstract class RegistryTweakBase : ITweakExecutor
{
    protected readonly ILogger Logger;

    protected RegistryTweakBase(ILogger logger)
    {
        Logger = logger;
    }

    public abstract TweakDefinition Definition { get; }

    /// <summary>Registry hive — defaults to LocalMachine.</summary>
    protected virtual RegistryHive Hive => RegistryHive.LocalMachine;

    /// <summary>Subkey path under the hive.</summary>
    protected abstract string SubKeyPath { get; }

    /// <summary>Registry value name.</summary>
    protected abstract string ValueName { get; }

    /// <summary>The desired value when the tweak is applied.</summary>
    protected abstract object DesiredValue { get; }

    /// <summary>
    /// Registry value kind used when WRITING DesiredValue.
    /// The original value's kind is captured separately at backup time for accurate rollback.
    /// </summary>
    protected virtual RegistryValueKind ValueKind => RegistryValueKind.DWord;

    /// <summary>Human-readable location and target shown in the pre-apply UI preview.</summary>
    public virtual string PreviewLocation => $"{Hive}\\{SubKeyPath}";

    /// <summary>Exact value this tweak intends to write. Previewing never writes to the registry.</summary>
    public virtual string PreviewTarget => $"{ValueName} = {FormatPreviewValue(DesiredValue)} ({ValueKind})";

    protected static string FormatPreviewValue(object value) => value switch
    {
        byte[] bytes => $"Binary [{Convert.ToHexString(bytes)}]",
        string text => $"\"{text}\"",
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? value.ToString() ?? "(unknown)",
        _ => value.ToString() ?? "(unknown)"
    };

    public virtual Task<bool> IsApplicableAsync(SystemSnapshot snapshot, CancellationToken ct = default)
        => Task.FromResult(true);

    public virtual async Task<string?> InspectCurrentStateAsync(SystemSnapshot snapshot, CancellationToken ct = default)
        => await Task.Run(() => ReadCurrentValue()?.ToString(), ct);

    public virtual async Task<bool> IsAlreadyConfiguredAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var current = ReadCurrentValue();
            return current is not null && ValuesEqual(current, DesiredValue);
        }, ct);
    }

    public virtual async Task<TweakOperationRecord> ApplyAsync(
        SystemSnapshot snapshot,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            // Capture original value AND its type before any write
            var (originalValue, originalKind) = ReadCurrentValueWithKind();
            var originalValueStr = originalValue?.ToString();

            if (dryRun)
            {
                return new TweakOperationRecord
                {
                    TweakId = Definition.Id,
                    DisplayName = Definition.DisplayName,
                    Result = TweakResult.DryRun,
                    OriginalValue = SerializeBackupValue(originalValue, originalKind),
                    NewValue = DesiredValue.ToString()
                };
            }

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(Hive, RegistryView.Registry64);
                using var key = baseKey.CreateSubKey(SubKeyPath, writable: true);
                if (key is null)
                    return Fail(
                        SerializeBackupValue(originalValue, originalKind),
                        $"Could not open or create registry key: {SubKeyPath}");

                key.SetValue(ValueName, DesiredValue, ValueKind);

                // --- VERIFICATION READ-BACK ---
                // Read the value back immediately after writing.
                // Do not report success from a write that is not confirmed by a read.
                var written = key.GetValue(ValueName);
                if (written is null || !ValuesEqual(written, DesiredValue))
                {
                    Logger.LogWarning(
                        "[{TweakId}] Write verification failed: wrote {Desired}, read back {Actual}",
                        Definition.Id, DesiredValue, written?.ToString() ?? "(null)");

                    return new TweakOperationRecord
                    {
                        TweakId = Definition.Id,
                        DisplayName = Definition.DisplayName,
                        Result = TweakResult.VerificationInconclusive,
                        OriginalValue = SerializeBackupValue(originalValue, originalKind),
                        NewValue = written?.ToString(),
                        ErrorMessage = $"Value written but read-back returned '{written}' instead of '{DesiredValue}'."
                    };
                }

                Logger.LogInformation("[{TweakId}] Applied and verified: {Key}\\{Value} = {New}",
                    Definition.Id, SubKeyPath, ValueName, DesiredValue);

                // Encode original value AND its kind in OriginalValue so rollback
                // can restore the exact original type, not just guess from content.
                var backupStr = SerializeBackupValue(originalValue, originalKind);

                return new TweakOperationRecord
                {
                    TweakId = Definition.Id,
                    DisplayName = Definition.DisplayName,
                    Result = Definition.RequiresRestart
                        ? TweakResult.RequiresRestart
                        : TweakResult.AppliedAndVerified,
                    OriginalValue = backupStr,
                    NewValue = DesiredValue.ToString()
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new TweakOperationRecord
                {
                    TweakId = Definition.Id,
                    DisplayName = Definition.DisplayName,
                    Result = TweakResult.RequiresAdminPrivileges,
                    OriginalValue = SerializeBackupValue(originalValue, originalKind),
                    ErrorMessage = "Administrator privileges required to modify this registry key."
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[{TweakId}] Apply failed.", Definition.Id);
                return Fail(SerializeBackupValue(originalValue, originalKind), ex.Message);
            }
        }, ct);
    }

    public virtual async Task<bool> VerifyAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var current = ReadCurrentValue();
            return current is not null && ValuesEqual(current, DesiredValue);
        }, ct);
    }

    public virtual async Task<TweakOperationRecord> RollbackAsync(
        TweakOperationRecord originalRecord,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(Hive, RegistryView.Registry64);

                if (originalRecord.OriginalValue is null)
                {
                    // The value (and possibly the entire key) did not exist before this tweak ran.
                    // Re-open writable; if key no longer exists after deletion attempt, that is fine.
                    using var key = baseKey.OpenSubKey(SubKeyPath, writable: true);
                    key?.DeleteValue(ValueName, throwOnMissingValue: false);

                    Logger.LogInformation("[{TweakId}] Rollback: deleted value (was absent before apply).",
                        Definition.Id);
                }
                else
                {
                    // Deserialize the backup, preserving the original registry type.
                    var (restoredValue, restoredKind) = DeserializeBackupValue(originalRecord.OriginalValue);

                    using var key = baseKey.OpenSubKey(SubKeyPath, writable: true);
                    if (key is null)
                    {
                        // Key was deleted externally after we applied — recreate it.
                        using var recreated = baseKey.CreateSubKey(SubKeyPath, writable: true);
                        if (recreated is null)
                            return new TweakOperationRecord
                            {
                                TweakId = Definition.Id,
                                DisplayName = Definition.DisplayName,
                                Result = TweakResult.Failed,
                                ErrorMessage = $"Cannot recreate registry key for rollback: {SubKeyPath}"
                            };

                        recreated.SetValue(ValueName, restoredValue, restoredKind);
                    }
                    else
                    {
                        key.SetValue(ValueName, restoredValue, restoredKind);
                    }

                    // Verify rollback read-back
                    using var verifyKey = baseKey.OpenSubKey(SubKeyPath, writable: false);
                    var readBack = verifyKey?.GetValue(ValueName);
                    if (readBack is null || !ValuesEqual(readBack, restoredValue))
                    {
                        Logger.LogWarning("[{TweakId}] Rollback verification failed: expected {Expected}, got {Got}",
                            Definition.Id, restoredValue, readBack?.ToString() ?? "(null)");
                        return new TweakOperationRecord
                        {
                            TweakId = Definition.Id,
                            DisplayName = Definition.DisplayName,
                            Result = TweakResult.Failed,
                            ErrorMessage = $"Rollback write-back verification failed: read '{readBack}' after restoring '{restoredValue}'."
                        };
                    }

                    Logger.LogInformation("[{TweakId}] Rolled back and verified: restored {Kind} '{Original}'.",
                        Definition.Id, restoredKind, restoredValue);
                }

                return new TweakOperationRecord
                {
                    TweakId = Definition.Id,
                    DisplayName = Definition.DisplayName,
                    Result = TweakResult.RolledBack,
                    OriginalValue = originalRecord.NewValue,
                    NewValue = originalRecord.OriginalValue
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[{TweakId}] Rollback failed.", Definition.Id);
                return Fail(null, ex.Message);
            }
        }, ct);
    }

    // ─── Backup serialisation ─────────────────────────────────────────────
    // Format: "<RegistryValueKind>:<value>" e.g. "DWord:2" or "String:high performance"
    // When the value was absent: null (no encoding needed)
    // Binary values: "Binary:<hex>" e.g. "Binary:00000000000000000000"

    private static string SerializeBackupValue(object? value, RegistryValueKind kind)
    {
        if (value is null) return null!; // null OriginalValue means "was absent"

        var kindStr = kind.ToString();
        string valueStr = kind == RegistryValueKind.Binary && value is byte[] bytes
            ? Convert.ToHexString(bytes)
            : value.ToString() ?? "";

        return $"{kindStr}:{valueStr}";
    }

    private static (object value, RegistryValueKind kind) DeserializeBackupValue(string backup)
    {
        // Legacy format from v1 (no kind prefix) — treat as DWord if parseable, else String
        if (!backup.Contains(':'))
        {
            if (int.TryParse(backup, out var legacyInt))
                return (legacyInt, RegistryValueKind.DWord);
            return (backup, RegistryValueKind.String);
        }

        var sep = backup.IndexOf(':');
        var kindStr = backup[..sep];
        var valueStr = backup[(sep + 1)..];

        if (!Enum.TryParse<RegistryValueKind>(kindStr, out var kind))
            return (valueStr, RegistryValueKind.String);

        return kind switch
        {
            RegistryValueKind.DWord =>
                int.TryParse(valueStr, out var dw)
                    ? (dw, RegistryValueKind.DWord)
                    : (valueStr, RegistryValueKind.String),
            RegistryValueKind.QWord =>
                long.TryParse(valueStr, out var qw)
                    ? (qw, RegistryValueKind.QWord)
                    : (valueStr, RegistryValueKind.String),
            RegistryValueKind.Binary =>
                (Convert.FromHexString(valueStr), RegistryValueKind.Binary),
            _ => (valueStr, kind)
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    protected (object? value, RegistryValueKind kind) ReadCurrentValueWithKind()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(Hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(SubKeyPath, writable: false);
            if (key is null) return (null, RegistryValueKind.Unknown);

            var value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var kind = value is not null
                ? key.GetValueKind(ValueName)
                : RegistryValueKind.Unknown;

            return (value, kind);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "[{TweakId}] Registry read failed for {Key}\\{Value}.",
                Definition.Id, SubKeyPath, ValueName);
            return (null, RegistryValueKind.Unknown);
        }
    }

    protected object? ReadCurrentValue()
    {
        var (value, _) = ReadCurrentValueWithKind();
        return value;
    }

    /// <summary>
    /// Value equality that handles the common case where the registry returns
    /// int for DWord but DesiredValue may be declared as a different numeric type.
    /// Also handles byte[] comparison for Binary values.
    /// </summary>
    private static bool ValuesEqual(object a, object b)
    {
        if (a is byte[] ba && b is byte[] bb)
            return ba.SequenceEqual(bb);

        // Normalise to string for cross-type comparison (int 2 == string "2")
        return a.ToString() == b.ToString();
    }

    private TweakOperationRecord Fail(string? originalValue, string message) =>
        new()
        {
            TweakId = Definition.Id,
            DisplayName = Definition.DisplayName,
            Result = TweakResult.Failed,
            OriginalValue = originalValue,
            ErrorMessage = message
        };
}

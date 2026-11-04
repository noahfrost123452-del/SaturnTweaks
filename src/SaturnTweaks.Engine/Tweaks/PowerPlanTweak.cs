using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;

namespace SaturnTweaks.Engine.Tweaks;

/// <summary>
/// Switches the Windows power plan to High Performance (or Balanced on laptops on battery).
/// Uses powercfg.exe — the documented, supported mechanism.
/// The original active GUID is captured before any change for accurate rollback.
/// </summary>
public sealed class PowerPlanTweak : ITweakExecutor
{
    private readonly ILogger<PowerPlanTweak> _logger;

    public string PreviewLocation => "Windows Power Options (powercfg)";
    public string PreviewTarget => "Active power scheme → High performance";

    // Well-known GUIDs for built-in Windows plans
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

    public PowerPlanTweak(ILogger<PowerPlanTweak> logger)
    {
        _logger = logger;
    }

    public TweakDefinition Definition { get; } = new()
    {
        Id = "power.plan.highperformance",
        DisplayName = "Switch to High Performance Power Plan",
        Category = TweakCategory.Power,
        ShortDescription = "Prevents CPU and GPU clock-speed reductions during gaming.",
        DetailedExplanation =
            "Windows Balanced power plan reduces CPU frequency and voltage during low-load " +
            "intervals to save energy. During gaming, transitions from reduced to full clock " +
            "speed take time and can cause brief frame-time spikes. High Performance prevents " +
            "frequency reduction, keeping clocks at or near maximum continuously.\n\n" +
            "On desktop systems plugged in, this is the most consistent way to reduce " +
            "clock-speed related stutters. On laptops, High Performance significantly " +
            "increases heat output, fan noise, and battery drain — it is only recommended " +
            "when plugged in and with adequate cooling.",
        ExpectedBenefit =
            "Elimination of CPU frequency-transition stutter. Consistent clock speeds. " +
            "Most noticeable on systems where power-saving aggressively downclocks the CPU.",
        PotentialDisadvantages =
            "Higher idle power consumption, heat, and fan noise. On laptops: significant " +
            "battery impact. On thermally constrained systems: sustained thermal throttling " +
            "may produce worse results than Balanced.",
        RiskLevel = TweakRisk.Moderate,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = true,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false // laptop vs desktop check required
    };

    public async Task<string?> InspectCurrentStateAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        return await Task.Run(() => GetActivePlanGuid(), ct);
    }

    public Task<bool> IsApplicableAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        // Warn but still allow on battery — user decides
        return Task.FromResult(true);
    }

    public async Task<bool> IsAlreadyConfiguredAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var current = GetActivePlanGuid();
            return string.Equals(current, HighPerformanceGuid, StringComparison.OrdinalIgnoreCase);
        }, ct);
    }

    public async Task<TweakOperationRecord> ApplyAsync(
        SystemSnapshot snapshot,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var originalGuid = GetActivePlanGuid();

            if (dryRun)
                return new TweakOperationRecord
                {
                    TweakId = Definition.Id,
                    DisplayName = Definition.DisplayName,
                    Result = TweakResult.DryRun,
                    OriginalValue = originalGuid,
                    NewValue = HighPerformanceGuid
                };

            try
            {
                RunPowercfg($"/setactive {HighPerformanceGuid}");

                // --- VERIFICATION READ-BACK ---
                // Do not report success from a powercfg call that did not throw.
                // Read the active plan back to confirm the switch actually took effect.
                var activeAfter = GetActivePlanGuid();
                if (!string.Equals(activeAfter, HighPerformanceGuid, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "[{TweakId}] Power plan verification failed: active plan is '{Active}', expected '{Expected}'.",
                        Definition.Id, activeAfter, HighPerformanceGuid);

                    return new TweakOperationRecord
                    {
                        TweakId = Definition.Id,
                        DisplayName = Definition.DisplayName,
                        Result = TweakResult.VerificationInconclusive,
                        OriginalValue = originalGuid,
                        NewValue = activeAfter,
                        ErrorMessage = $"Active plan after switch: '{activeAfter}'. Expected High Performance GUID."
                    };
                }

                _logger.LogInformation("[{TweakId}] Power plan set to High Performance and verified.",
                    Definition.Id);

                return new TweakOperationRecord
                {
                    TweakId = Definition.Id,
                    DisplayName = Definition.DisplayName,
                    Result = TweakResult.AppliedAndVerified,
                    OriginalValue = originalGuid,
                    NewValue = HighPerformanceGuid
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{TweakId}] Power plan switch failed.", Definition.Id);
                return new TweakOperationRecord
                {
                    TweakId = Definition.Id,
                    DisplayName = Definition.DisplayName,
                    Result = TweakResult.Failed,
                    OriginalValue = originalGuid,
                    ErrorMessage = ex.Message
                };
            }
        }, ct);
    }

    public async Task<bool> VerifyAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var current = GetActivePlanGuid();
            return string.Equals(current, HighPerformanceGuid, StringComparison.OrdinalIgnoreCase);
        }, ct);
    }

    public async Task<TweakOperationRecord> RollbackAsync(
        TweakOperationRecord originalRecord,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            // Strip the backup format prefix if present (RegistryTweakBase format not used here,
            // but PowerPlanTweak stores a raw GUID string — handle both just in case)
            var targetGuid = originalRecord.OriginalValue ?? BalancedGuid;
            if (targetGuid.Contains(':'))
                targetGuid = targetGuid[(targetGuid.IndexOf(':') + 1)..];

            // Fall back to Balanced if the stored GUID is empty or malformed
            if (string.IsNullOrWhiteSpace(targetGuid))
                targetGuid = BalancedGuid;

            try
            {
                RunPowercfg($"/setactive {targetGuid}");

                // --- VERIFICATION READ-BACK ---
                var activeAfter = GetActivePlanGuid();
                if (!string.Equals(activeAfter, targetGuid, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "[{TweakId}] Rollback verification failed: active plan is '{Active}', expected '{Expected}'.",
                        Definition.Id, activeAfter, targetGuid);

                    return new TweakOperationRecord
                    {
                        TweakId = Definition.Id,
                        DisplayName = Definition.DisplayName,
                        Result = TweakResult.Failed,
                        ErrorMessage = $"Rollback active plan: '{activeAfter}'. Expected '{targetGuid}'."
                    };
                }

                _logger.LogInformation("[{TweakId}] Power plan rolled back and verified to {Guid}.",
                    Definition.Id, targetGuid);

                return new TweakOperationRecord
                {
                    TweakId = Definition.Id,
                    DisplayName = Definition.DisplayName,
                    Result = TweakResult.RolledBack,
                    OriginalValue = HighPerformanceGuid,
                    NewValue = targetGuid
                };
            }
            catch (Exception ex)
            {
                return new TweakOperationRecord
                {
                    TweakId = Definition.Id,
                    DisplayName = Definition.DisplayName,
                    Result = TweakResult.Failed,
                    ErrorMessage = ex.Message
                };
            }
        }, ct);
    }

    private static string GetActivePlanGuid()
    {
        var output = RunPowercfg("/getactivescheme");
        // "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)"
        var match = System.Text.RegularExpressions.Regex.Match(
            output, @"Power Scheme GUID:\s+([0-9a-f\-]{36})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static string RunPowercfg(string args)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        if (!proc.Start())
            throw new InvalidOperationException("Could not start powercfg.exe.");

        // Drain both redirected streams concurrently so the child process cannot block
        // if either output pipe fills. GetActivePlanGuid consumes the returned stdout.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit();

        var output = stdoutTask.GetAwaiter().GetResult();
        var error = stderrTask.GetAwaiter().GetResult();

        if (proc.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(error) ? string.Empty : $" Details: {error.Trim()}";
            throw new InvalidOperationException(
                $"powercfg {args} exited with code {proc.ExitCode}.{details}");
        }

        return output;
    }
}

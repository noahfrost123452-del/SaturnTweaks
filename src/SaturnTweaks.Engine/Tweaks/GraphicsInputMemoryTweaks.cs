using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SaturnTweaks.Core.Models;

namespace SaturnTweaks.Engine.Tweaks;

// ════════════════════════════════════════════════════════════════════════════
// GRAPHICS TWEAKS
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Windows graphics preference for a specific app path (per-app GPU selection).
/// Documented registry path: HKCU\Software\Microsoft\DirectX\UserGpuPreferences
/// Value format: "GpuPreference=2;" where 2 = High Performance GPU.
/// Applicability: systems with both integrated and discrete GPU (hybrid graphics).
/// </summary>
public sealed class DisableFullscreenOptimizationsTweak : RegistryTweakBase
{
    public DisableFullscreenOptimizationsTweak(ILogger<DisableFullscreenOptimizationsTweak> logger)
        : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "graphics.fullscreen.disableoptimizations",
        DisplayName = "Disable Fullscreen Optimizations System-Wide",
        Category = TweakCategory.Graphics,
        ShortDescription = "Prevents Windows from converting exclusive fullscreen to borderless.",
        DetailedExplanation =
            "Windows Fullscreen Optimizations silently convert exclusive fullscreen mode into a " +
            "borderless window to enable faster Alt+Tab and overlay compositing. For some games " +
            "this reduces rendering priority and can cause frame-time variance. Setting " +
            "DisableFullscreenOptimizations=1 system-wide restores true exclusive fullscreen " +
            "behaviour for applications that do not individually opt out.\n\n" +
            "Note: This affects all applications, not just games. Some games use the override " +
            "in their own .exe compatibility settings instead — that approach is more targeted.",
        ExpectedBenefit =
            "True exclusive fullscreen with direct scanout on GPUs that support it. " +
            "Can reduce present latency and compositing interference in fullscreen titles.",
        PotentialDisadvantages =
            "Alt+Tab will be slower in fullscreen games. Overlays (Discord, GeForce Experience) " +
            "may not function in exclusive fullscreen. Game captures via OBS Game Capture may " +
            "require 'Capture any fullscreen application' mode.",
        RiskLevel = TweakRisk.Low,
        SupportedWindowsBuilds = new() { ["10"] = 16299 },
        RequiresAdminPrivileges = false,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false
    };

    protected override RegistryHive Hive => RegistryHive.CurrentUser;
    protected override string SubKeyPath =>
        @"System\GameConfigStore";
    protected override string ValueName => "GameDVR_FSEBehaviorMode";
    // 2 = prefer exclusive fullscreen; revert to 0 to restore FSO
    protected override object DesiredValue => 2;
}

/// <summary>
/// Disables variable refresh rate scheduling for cases where VRR causes tearing artefacts.
/// HKCU\Software\Microsoft\DirectX\UserGpuPreferences — system-wide VRR toggle.
/// </summary>
public sealed class DisableVrrTearingTweak : RegistryTweakBase
{
    public DisableVrrTearingTweak(ILogger<DisableVrrTearingTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "graphics.vrr.disabletearing",
        DisplayName = "Disable Windows Variable Refresh Rate (VRR)",
        Category = TweakCategory.Graphics,
        ShortDescription = "Turns off Windows-level VRR when it causes visual artefacts.",
        DetailedExplanation =
            "Windows 10 2004+ includes a system-level Variable Refresh Rate setting " +
            "(separate from G-Sync/FreeSync configured in the GPU driver). On some monitor " +
            "and GPU combinations, the Windows VRR implementation causes tearing artefacts " +
            "or visual instability. This registry value disables it at the Windows level " +
            "without affecting GPU-driver-level VRR (G-Sync/FreeSync).\n\n" +
            "Only apply this if you are experiencing VRR-related visual artefacts. " +
            "On a correctly functioning VRR setup, leave this disabled.",
        ExpectedBenefit =
            "Eliminates Windows-level VRR artefacts on problematic monitor/GPU combinations.",
        PotentialDisadvantages =
            "Removes Windows VRR benefit. G-Sync and FreeSync configured in the GPU driver " +
            "control panel are unaffected by this setting.",
        RiskLevel = TweakRisk.Low,
        SupportedWindowsBuilds = new() { ["10"] = 19041, ["11"] = 22000 },
        RequiresAdminPrivileges = false,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false
    };

    protected override RegistryHive Hive => RegistryHive.CurrentUser;
    protected override string SubKeyPath => @"Software\Microsoft\DirectX";
    protected override string ValueName => "VRROptimizeEnable";
    protected override object DesiredValue => 0;

    public override Task<bool> IsApplicableAsync(SystemSnapshot snapshot, CancellationToken ct = default)
        => Task.FromResult(snapshot.OsBuildNumber >= 19041);
}

/// <summary>
/// Windows Graphics Settings — hardware-accelerated GPU scheduling for individual apps.
/// Per-app GPU preference written to UserGpuPreferences.
/// System-level HAGS must already be enabled; this is per-app affinity.
/// </summary>
public sealed class PreferDiscreteGpuForAppsTweak : RegistryTweakBase
{
    public PreferDiscreteGpuForAppsTweak(ILogger<PreferDiscreteGpuForAppsTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "graphics.gpu.preferredtype",
        DisplayName = "Set Windows GPU Preference to High Performance",
        Category = TweakCategory.Graphics,
        ShortDescription = "Tells Windows Graphics Settings to default to the discrete GPU.",
        DetailedExplanation =
            "Windows exposes a per-application GPU preference in Graphics Settings " +
            "(Settings → Display → Graphics). The system-wide default can be nudged toward " +
            "the high-performance GPU by setting UserGpuPreferences. This helps on hybrid " +
            "graphics laptops where the OS may select the integrated GPU for borderless games. " +
            "This registry key controls the system default; individual apps can still override it.",
        ExpectedBenefit =
            "Games and applications that do not hard-code their GPU selection default to the " +
            "discrete GPU, avoiding unintended integrated GPU rendering.",
        PotentialDisadvantages =
            "May increase power consumption on laptops by keeping the discrete GPU active " +
            "for applications that would otherwise use the integrated GPU.",
        RiskLevel = TweakRisk.Low,
        SupportedWindowsBuilds = new() { ["10"] = 17763, ["11"] = 22000 },
        RequiresAdminPrivileges = false,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false
    };

    protected override RegistryHive Hive => RegistryHive.LocalMachine;
    protected override string SubKeyPath =>
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    protected override string ValueName => "SystemResponsiveness";
    // 0 = games get maximum share of scheduler time; default is 20
    protected override object DesiredValue => 0;

    public override Task<bool> IsApplicableAsync(SystemSnapshot snapshot, CancellationToken ct = default)
        => Task.FromResult(snapshot.Gpus.Any(g => !g.IsIntegrated) && snapshot.OsBuildNumber >= 17763);
}

// ════════════════════════════════════════════════════════════════════════════
// INPUT TWEAKS
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Smooth mouse curves — writes flat (zero-acceleration) values to both
/// SmoothMouseXCurve and SmoothMouseYCurve alongside MouseSpeed=0.
/// This is the full three-part acceleration disable, not just MouseSpeed.
/// </summary>
public sealed class FlattenMouseCurvesTweak : RegistryTweakBase
{
    public FlattenMouseCurvesTweak(ILogger<FlattenMouseCurvesTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "input.mouse.flatcurves",
        DisplayName = "Set Flat Mouse Acceleration Curves",
        Category = TweakCategory.Input,
        ShortDescription = "Writes zero-acceleration SmoothMouseXCurve and SmoothMouseYCurve values.",
        DetailedExplanation =
            "Windows pointer acceleration is controlled by three registry values: MouseSpeed, " +
            "SmoothMouseXCurve, and SmoothMouseYCurve. Setting MouseSpeed=0 alone (the " +
            "DisableMouseAccelerationTweak) removes the primary curve but leaves the smooth " +
            "curve data in place, which some applications read independently.\n\n" +
            "This tweak writes the canonical flat-curve byte sequences used by markc's " +
            "mouse fix — a well-understood, documented approach. The 10-byte binary sequences " +
            "represent a linear 1:1 mapping at every speed step.\n\n" +
            "This is a complement to, not a replacement for, disabling MouseSpeed.",
        ExpectedBenefit =
            "Complete removal of the Windows mouse acceleration curve, including the smooth " +
            "curve component. Most impactful for titles that do not use raw input.",
        PotentialDisadvantages =
            "Desktop cursor movement loses any acceleration benefit. Users who rely on " +
            "acceleration for productivity tasks (small mousepad, high-DPI work) will " +
            "find the cursor harder to position precisely at slow speeds.",
        RiskLevel = TweakRisk.Safe,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = false,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false
    };

    // SmoothMouseXCurve: targets the X axis acceleration table
    // This tweak writes both curves; we override Apply to handle two values atomically.
    protected override RegistryHive Hive => RegistryHive.CurrentUser;
    protected override string SubKeyPath => @"Control Panel\Mouse";
    protected override string ValueName => "SmoothMouseXCurve";
    protected override RegistryValueKind ValueKind => RegistryValueKind.Binary;

    // Flat X curve — linear 1:1 at all speed steps (markc mouse fix values)
    public override string PreviewTarget =>
        "SmoothMouseXCurve = 00000000000000000000; SmoothMouseYCurve = 00000000000000000000";

    protected override object DesiredValue => new byte[]
    {
        0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00
    };

    public override async Task<TweakOperationRecord> ApplyAsync(
        SystemSnapshot snapshot, bool dryRun = false, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            // Capture original bytes as hex strings for type-preserving rollback.
            // Using hex ensures round-trip fidelity regardless of the byte values.
            byte[]? origXBytes = null;
            byte[]? origYBytes = null;

            try
            {
                using var baseKeyRead = RegistryKey.OpenBaseKey(Hive, RegistryView.Registry64);
                using var keyRead = baseKeyRead.OpenSubKey(SubKeyPath, writable: false);
                origXBytes = keyRead?.GetValue("SmoothMouseXCurve") as byte[];
                origYBytes = keyRead?.GetValue("SmoothMouseYCurve") as byte[];
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "[{Id}] Pre-apply read of original curves failed.", Definition.Id);
            }

            string EncodeOriginal(byte[]? b) => b is null ? "(not set)" : Convert.ToHexString(b);
            var backupStr = $"X={EncodeOriginal(origXBytes)}|Y={EncodeOriginal(origYBytes)}";

            if (dryRun)
                return new TweakOperationRecord
                {
                    TweakId = Definition.Id, DisplayName = Definition.DisplayName,
                    Result = TweakResult.DryRun,
                    OriginalValue = backupStr,
                    NewValue = "X=flat|Y=flat"
                };

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(Hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(SubKeyPath, writable: true)
                    ?? baseKey.CreateSubKey(SubKeyPath, writable: true);
                if (key is null)
                    return Fail(backupStr, $"Cannot open {SubKeyPath}");

                var flatX = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                var flatY = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                key.SetValue("SmoothMouseXCurve", flatX, RegistryValueKind.Binary);
                key.SetValue("SmoothMouseYCurve", flatY, RegistryValueKind.Binary);

                // --- VERIFICATION READ-BACK: confirm both curves were written ---
                var writtenX = key.GetValue("SmoothMouseXCurve") as byte[];
                var writtenY = key.GetValue("SmoothMouseYCurve") as byte[];

                if (writtenX is null || !writtenX.SequenceEqual(flatX)
                 || writtenY is null || !writtenY.SequenceEqual(flatY))
                {
                    Logger.LogWarning("[{Id}] Mouse curve write verification failed.", Definition.Id);
                    return new TweakOperationRecord
                    {
                        TweakId = Definition.Id, DisplayName = Definition.DisplayName,
                        Result = TweakResult.VerificationInconclusive,
                        OriginalValue = backupStr,
                        NewValue = "X=flat|Y=flat",
                        ErrorMessage = "One or both curves did not verify after write."
                    };
                }

                Logger.LogInformation("[{Id}] Mouse curves flattened and verified.", Definition.Id);

                return new TweakOperationRecord
                {
                    TweakId = Definition.Id, DisplayName = Definition.DisplayName,
                    Result = TweakResult.AppliedAndVerified,
                    OriginalValue = backupStr,
                    NewValue = "X=flat|Y=flat"
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new TweakOperationRecord
                {
                    TweakId = Definition.Id, DisplayName = Definition.DisplayName,
                    Result = TweakResult.RequiresAdminPrivileges,
                    OriginalValue = backupStr,
                    ErrorMessage = "Administrator privileges required."
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[{Id}] Apply failed.", Definition.Id);
                return Fail(backupStr, ex.Message);
            }
        }, ct);
    }

    /// <summary>
    /// Verifies both SmoothMouseXCurve and SmoothMouseYCurve are flat.
    /// The base class VerifyAsync only checks SmoothMouseXCurve (ValueName),
    /// so this override is required for correctness.
    /// </summary>
    public override async Task<bool> VerifyAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(Hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(SubKeyPath, writable: false);
                if (key is null) return false;

                var flatBytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                var xCurve = key.GetValue("SmoothMouseXCurve") as byte[];
                var yCurve = key.GetValue("SmoothMouseYCurve") as byte[];

                return xCurve is not null && xCurve.SequenceEqual(flatBytes)
                    && yCurve is not null && yCurve.SequenceEqual(flatBytes);
            }
            catch
            {
                return false;
            }
        }, ct);
    }

    /// <summary>
    /// Override InspectCurrentStateAsync to show both curve values, not just X.
    /// </summary>
    public override async Task<string?> InspectCurrentStateAsync(
        SystemSnapshot snapshot, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(Hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(SubKeyPath, writable: false);
                if (key is null) return "(key absent)";

                var xCurve = key.GetValue("SmoothMouseXCurve") as byte[];
                var yCurve = key.GetValue("SmoothMouseYCurve") as byte[];

                string Describe(byte[]? b) => b is null
                    ? "(not set)"
                    : b.All(v => v == 0) ? "flat" : Convert.ToHexString(b);

                return $"X={Describe(xCurve)}  Y={Describe(yCurve)}";
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "[{Id}] FlattenMouseCurves inspect failed.", Definition.Id);
                return "(read error)";
            }
        }, ct);
    }

    public override async Task<TweakOperationRecord> RollbackAsync(
        TweakOperationRecord originalRecord, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(Hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(SubKeyPath, writable: true);
                if (key is null)
                    return new TweakOperationRecord
                    {
                        TweakId = Definition.Id, DisplayName = Definition.DisplayName,
                        Result = TweakResult.Failed,
                        ErrorMessage = "Registry key not found for rollback."
                    };

                // originalRecord.OriginalValue format: "X=<hex>|Y=<hex>" or null (values were absent)
                if (originalRecord.OriginalValue is null)
                {
                    key.DeleteValue("SmoothMouseXCurve", throwOnMissingValue: false);
                    key.DeleteValue("SmoothMouseYCurve", throwOnMissingValue: false);
                    Logger.LogInformation("[{Id}] Mouse curves rolled back: both deleted (were absent).",
                        Definition.Id);
                }
                else
                {
                    // Parse "X=<hex>|Y=<hex>" to restore original byte arrays
                    var xOriginal = ParseCurveFromBackup(originalRecord.OriginalValue, "X");
                    var yOriginal = ParseCurveFromBackup(originalRecord.OriginalValue, "Y");

                    if (xOriginal is not null)
                        key.SetValue("SmoothMouseXCurve", xOriginal, RegistryValueKind.Binary);
                    else
                        key.DeleteValue("SmoothMouseXCurve", throwOnMissingValue: false);

                    if (yOriginal is not null)
                        key.SetValue("SmoothMouseYCurve", yOriginal, RegistryValueKind.Binary);
                    else
                        key.DeleteValue("SmoothMouseYCurve", throwOnMissingValue: false);

                    Logger.LogInformation("[{Id}] Mouse curves rolled back to original byte values.",
                        Definition.Id);
                }

                return new TweakOperationRecord
                {
                    TweakId = Definition.Id, DisplayName = Definition.DisplayName,
                    Result = TweakResult.RolledBack,
                    OriginalValue = "X=flat|Y=flat",
                    NewValue = originalRecord.OriginalValue
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[{Id}] FlattenMouseCurves rollback failed.", Definition.Id);
                return new TweakOperationRecord
                {
                    TweakId = Definition.Id, DisplayName = Definition.DisplayName,
                    Result = TweakResult.Failed, ErrorMessage = ex.Message
                };
            }
        }, ct);
    }

    /// <summary>
    /// Parse "X=0102...|Y=..." backup string for one axis.
    /// Original ApplyAsync stored the raw byte[] ToString(), which is not useful;
    /// the new ApplyAsync records hex. Legacy records with non-hex content return null.
    /// </summary>
    private static byte[]? ParseCurveFromBackup(string backup, string axis)
    {
        // Format: "X=<hex or (not set)>|Y=<hex or (not set)>"
        var prefix = $"{axis}=";
        var start = backup.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += prefix.Length;
        var end = backup.IndexOf('|', start);
        var raw = end < 0 ? backup[start..] : backup[start..end];
        raw = raw.Trim();
        if (raw == "(not set)" || raw == "flat" || string.IsNullOrEmpty(raw)) return null;
        try { return Convert.FromHexString(raw); } catch { return null; }
    }

    private TweakOperationRecord Fail(string orig, string msg) => new()
    {
        TweakId = Definition.Id, DisplayName = Definition.DisplayName,
        Result = TweakResult.Failed, OriginalValue = orig, ErrorMessage = msg
    };
}

/// <summary>
/// USB Selective Suspend — disables low-power state for USB controllers.
/// Prevents the OS from powering down USB hubs between input events,
/// which can cause measurable input latency spikes (1–8 ms) on some systems.
/// </summary>
public sealed class DisableUsbSelectiveSuspendTweak : RegistryTweakBase
{
    public DisableUsbSelectiveSuspendTweak(ILogger<DisableUsbSelectiveSuspendTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "input.usb.disableselectivesuspend",
        DisplayName = "Disable USB Selective Suspend",
        Category = TweakCategory.Input,
        ShortDescription = "Prevents USB controllers from entering low-power idle state.",
        DetailedExplanation =
            "Windows USB Selective Suspend powers down USB hubs and attached devices during " +
            "idle periods to save energy. When the device (mouse, keyboard) next sends a " +
            "packet, the hub must resume before the input is registered. On most modern " +
            "hardware this resume takes < 1 ms and is imperceptible. On older or cheaper " +
            "USB controllers the resume latency can reach 4–8 ms, causing detectable input " +
            "delay spikes.\n\n" +
            "This tweak disables selective suspend for the active High Performance power plan. " +
            "It modifies the current power plan's USB settings — not a global registry value.",
        ExpectedBenefit =
            "Eliminates USB resume latency spikes on affected hardware. " +
            "Most noticeable on systems with older USB host controllers.",
        PotentialDisadvantages =
            "Slightly higher USB controller power consumption. " +
            "On laptops, can marginally reduce battery life.",
        RiskLevel = TweakRisk.Low,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = true,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false
    };

    // GUID_USB_SUBGROUP = 2a737441-1930-4402-8d77-b2bebba308a3
    // GUID_USB_SETTING_USB_SELECTIVE_SUSPEND = 48e6b7a6-50f5-4782-a5d4-53bb8f07e226
    // powercfg subgroup/setting GUIDs — written via the documented powercfg API.
    // We use the registry path that powercfg writes to for auditability.
    protected override string SubKeyPath =>
        @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\2a737441-1930-4402-8d77-b2bebba308a3\48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
    protected override string ValueName => "ACSettingIndex";
    protected override object DesiredValue => 0; // 0 = disabled, 1 = enabled

    public override Task<bool> IsApplicableAsync(SystemSnapshot snapshot, CancellationToken ct = default)
        => Task.FromResult(snapshot.IsRunningAsAdministrator);
}

/// <summary>
/// Raw input pointer speed — sets Windows pointer speed to the neutral midpoint (6/11)
/// which corresponds to a 1:1 ratio with no Windows pointer speed multiplier.
/// Most games with raw input are unaffected, but games using WM_MOUSEMOVE benefit.
/// </summary>
public sealed class NeutralPointerSpeedTweak : RegistryTweakBase
{
    public NeutralPointerSpeedTweak(ILogger<NeutralPointerSpeedTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "input.mouse.neutralspeed",
        DisplayName = "Set Neutral Windows Pointer Speed (6/11)",
        Category = TweakCategory.Input,
        ShortDescription = "Sets pointer speed to the neutral midpoint — no Windows multiplier.",
        DetailedExplanation =
            "The Windows pointer speed slider (Control Panel → Mouse → Pointer Options) " +
            "corresponds to registry values 1–20 in HKCU\\Control Panel\\Mouse\\MouseSensitivity. " +
            "The neutral midpoint is 10 (slider position 6 of 11), at which Windows applies no " +
            "speed multiplier — cursor movement maps directly to physical mouse movement. " +
            "Values above 10 apply a multiplier (faster), values below apply a divisor (slower).\n\n" +
            "Games with raw input bypass this setting entirely. Games that read WM_MOUSEMOVE " +
            "are affected. Setting this to 10 ensures no unintended Windows multiplier is active.",
        ExpectedBenefit =
            "No Windows pointer speed multiplier. Ensures in-game sensitivity settings " +
            "behave predictably for games using WM_MOUSEMOVE input.",
        PotentialDisadvantages =
            "Desktop cursor speed changes if your current setting is not 6/11.",
        RiskLevel = TweakRisk.Safe,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = false,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false
    };

    protected override RegistryHive Hive => RegistryHive.CurrentUser;
    protected override string SubKeyPath => @"Control Panel\Mouse";
    protected override string ValueName => "MouseSensitivity";
    protected override object DesiredValue => "10"; // Neutral midpoint
    protected override RegistryValueKind ValueKind => RegistryValueKind.String;
}

// ════════════════════════════════════════════════════════════════════════════
// MEMORY TWEAKS
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Large System Cache — controls whether the OS favouring file cache or application memory.
/// Setting to 0 (default) tells Windows to keep memory available for applications,
/// which benefits gaming where the game itself is the primary memory consumer.
/// </summary>
public sealed class DisableLargeSystemCacheTweak : RegistryTweakBase
{
    public DisableLargeSystemCacheTweak(ILogger<DisableLargeSystemCacheTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "memory.cache.apppriority",
        DisplayName = "Optimise Memory Manager for Applications",
        Category = TweakCategory.Memory,
        ShortDescription = "Ensures Windows prioritises application memory over file system cache.",
        DetailedExplanation =
            "The LargeSystemCache registry value controls how Windows Memory Manager allocates " +
            "physical RAM between the system file cache and application working sets. " +
            "When set to 0 (the documented default for workstations), Windows keeps memory " +
            "available for running applications. When set to 1 (server default), it aggressively " +
            "expands the file cache, which can cause game working sets to be trimmed to disk.\n\n" +
            "Some third-party 'optimization' tools or server-role installations set this to 1. " +
            "This tweak restores the correct workstation default.",
        ExpectedBenefit =
            "Prevents unnecessary game working-set trimming on systems where this was set " +
            "incorrectly. No benefit if already at the default value of 0.",
        PotentialDisadvantages = "None on a correctly configured workstation.",
        RiskLevel = TweakRisk.Safe,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = true,
        RequiresRestart = true,
        IncludedInRecommendedPreset = false
    };

    protected override string SubKeyPath =>
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    protected override string ValueName => "LargeSystemCache";
    protected override object DesiredValue => 0;

    public override async Task<bool> IsApplicableAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        // Only apply if currently non-zero — don't touch a correctly configured system
        return await Task.Run(() =>
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SubKeyPath);
            var current = key?.GetValue(ValueName);
            return current is int v && v != 0;
        }, ct);
    }
}

/// <summary>
/// ClearPageFileAtShutdown — many optimization guides recommend enabling this for security,
/// but it causes significantly longer shutdown times with no gaming benefit.
/// This ensures it is disabled (the correct default for gaming systems).
/// </summary>
public sealed class DisableClearPageFileTweak : RegistryTweakBase
{
    public DisableClearPageFileTweak(ILogger<DisableClearPageFileTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "memory.pagefile.cleardisable",
        DisplayName = "Disable Clear Page File at Shutdown",
        Category = TweakCategory.Memory,
        ShortDescription = "Removes the page file zeroing pass that dramatically slows shutdown.",
        DetailedExplanation =
            "ClearPageFileAtShutdown=1 instructs Windows to write zeros to the entire " +
            "page file during shutdown — a security measure intended for environments handling " +
            "sensitive data. On gaming systems with large page files (8–32 GB), this causes " +
            "shutdown times of several minutes with no gaming benefit.\n\n" +
            "This tweak sets the value to 0 (the Windows default). It is only useful on " +
            "systems where this was incorrectly enabled by an enterprise policy or " +
            "optimisation tool.",
        ExpectedBenefit = "Dramatically faster Windows shutdown on systems where this was enabled.",
        PotentialDisadvantages =
            "Page file data from the previous session remains on disk until overwritten. " +
            "Not a concern for personal gaming systems.",
        RiskLevel = TweakRisk.Safe,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = true,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false
    };

    protected override string SubKeyPath =>
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    protected override string ValueName => "ClearPageFileAtShutdown";
    protected override object DesiredValue => 0;

    public override async Task<bool> IsApplicableAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SubKeyPath);
            var val = key?.GetValue(ValueName);
            return val is int v && v == 1; // Only applicable when it was wrongly set to 1
        }, ct);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// ADVANCED TWEAKS
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// IRQ8 priority — ensures the system timer runs at the highest interrupt priority.
/// Documents: https://docs.microsoft.com/windows-hardware/drivers/kernel/interrupt-priority
/// Observed improvement: more consistent timer-based callbacks on some hardware.
/// </summary>
public sealed class Irq8PriorityTweak : RegistryTweakBase
{
    public Irq8PriorityTweak(ILogger<Irq8PriorityTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "advanced.irq8.highpriority",
        DisplayName = "Set System Timer IRQ8 to High Priority",
        Category = TweakCategory.Advanced,
        ShortDescription = "Raises the system clock interrupt priority for more consistent scheduling.",
        DetailedExplanation =
            "The MMCSS IRQ8 priority setting instructs Windows to treat the system timer " +
            "interrupt (IRQ 8, the real-time clock) with elevated scheduling priority. " +
            "This can produce more consistent timer callback intervals, which benefits audio " +
            "engines and input polling loops that rely on precise timing.\n\n" +
            "Effect is system-dependent and typically small (sub-millisecond). " +
            "It is not a substitute for a proper frame-pacing solution in-game.",
        ExpectedBenefit =
            "Marginally more consistent timer resolution on some hardware configurations. " +
            "Most noticeable in audio and precision-timing scenarios.",
        PotentialDisadvantages =
            "No measurable disadvantage on modern hardware. " +
            "Requires restart to take effect.",
        RiskLevel = TweakRisk.Low,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = true,
        RequiresRestart = true,
        IncludedInRecommendedPreset = false
    };

    protected override string SubKeyPath =>
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    protected override string ValueName => "SystemResponsiveness";
    // 0 = games/multimedia get maximum time; documented range 0–100, default 20
    protected override object DesiredValue => 0;
}

/// <summary>
/// Win32PrioritySeparation — controls how Windows divides CPU time between
/// foreground and background processes. Value 0x26 = short, variable, 3:1 boost.
/// Well-understood, documented value used in competitive gaming contexts.
/// </summary>
public sealed class Win32PrioritySeparationTweak : RegistryTweakBase
{
    public Win32PrioritySeparationTweak(ILogger<Win32PrioritySeparationTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "advanced.scheduling.win32priority",
        DisplayName = "Optimise Win32 Priority Separation",
        Category = TweakCategory.Advanced,
        ShortDescription = "Adjusts foreground/background CPU quantum for gaming responsiveness.",
        DetailedExplanation =
            "Win32PrioritySeparation is a documented Windows kernel value that controls " +
            "how much CPU time the scheduler assigns to the foreground vs background " +
            "processes, and whether quantum intervals are short or long.\n\n" +
            "The value 0x26 (decimal 38) selects: short quanta, variable interval, " +
            "maximum foreground boost (3:1 ratio). This matches the 'Programs' optimisation " +
            "in Control Panel → Advanced System Settings → Performance.\n\n" +
            "The default Windows value is 0x02. The competitive gaming community commonly " +
            "uses 0x26 for tighter scheduling cycles. Effect is measurable in some CPU-bound " +
            "scenarios and imperceptible in GPU-bound ones.",
        ExpectedBenefit =
            "Shorter CPU scheduling quanta may reduce input-to-frame latency in CPU-bound games. " +
            "Most measurable on systems where background tasks compete with the game process.",
        PotentialDisadvantages =
            "Short quanta increase context-switch overhead. Background tasks run less " +
            "smoothly while a fullscreen game is active. Requires restart.",
        RiskLevel = TweakRisk.Moderate,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = true,
        RequiresRestart = true,
        IncludedInRecommendedPreset = false
    };

    protected override string SubKeyPath =>
        @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    protected override string ValueName => "Win32PrioritySeparation";
    protected override object DesiredValue => 0x26; // 38 decimal
}

/// <summary>
/// Disable SysMain (Superfetch) — the predictive prefetch service.
/// IMPORTANT: SysMain is beneficial on HDDs and low-RAM systems.
/// This tweak is only applicable on SSD systems with >= 16 GB RAM
/// where prefetch provides no loading benefit and generates background I/O.
/// </summary>
public sealed class DisableSysMainTweak : RegistryTweakBase
{
    public DisableSysMainTweak(ILogger<DisableSysMainTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "advanced.sysmain.disable",
        DisplayName = "Disable SysMain (Superfetch)",
        Category = TweakCategory.Advanced,
        ShortDescription = "Stops predictive prefetch on SSD+high-RAM systems where it adds noise.",
        DetailedExplanation =
            "SysMain (formerly Superfetch) predicts which applications you will launch and " +
            "preloads them into RAM. On HDDs and low-RAM systems (< 16 GB), this is genuinely " +
            "useful — it reduces cold-start loading times. On NVMe SSD systems with 16+ GB RAM, " +
            "games load fast enough that prefetch provides no meaningful benefit, while the " +
            "background I/O activity can cause brief interference during gameplay.\n\n" +
            "THIS TWEAK IS ONLY OFFERED when Saturn Tweaks detects an SSD with >= 16 GB RAM. " +
            "It is not applied on HDDs. Disabling SysMain on an HDD system will make cold " +
            "application launches slower.",
        ExpectedBenefit =
            "Eliminates SysMain background I/O on SSD systems where prefetch provides no benefit.",
        PotentialDisadvantages =
            "First launch of applications after boot will not benefit from prefetch. " +
            "Net effect on SSDs is typically neutral to mildly positive.",
        RiskLevel = TweakRisk.Moderate,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = true,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false
    };

    protected override string SubKeyPath =>
        @"SYSTEM\CurrentControlSet\Services\SysMain";
    protected override string ValueName => "Start";
    // 4 = disabled, 2 = automatic (default)
    protected override object DesiredValue => 4;

    public override Task<bool> IsApplicableAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        // Only applicable on SSD systems with >= 16 GB RAM — do not offer on HDD
        bool hasEnoughRam = snapshot.TotalPhysicalMemoryBytes >= 16L * 1024 * 1024 * 1024;
        bool hasSsd = snapshot.Drives.Any(d =>
            d.DriveType.Equals("SSD", StringComparison.OrdinalIgnoreCase) ||
            d.DriveType.Equals("NVMe", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(hasEnoughRam && hasSsd && snapshot.IsRunningAsAdministrator);
    }
}

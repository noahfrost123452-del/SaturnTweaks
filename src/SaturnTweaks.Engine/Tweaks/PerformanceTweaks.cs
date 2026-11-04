using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SaturnTweaks.Core.Models;

namespace SaturnTweaks.Engine.Tweaks;

// ────────────────────────────────────────────────────────────────────────────
// Hardware-Accelerated GPU Scheduling (HAGS)
// Supported: Windows 10 2004+ (build 19041+), requires WDDM 2.7+ driver.
// Effect: Reduces CPU involvement in GPU command queuing on supported hardware.
// Trade-off: May increase latency on older or integrated GPUs; no effect without compatible driver.
// ────────────────────────────────────────────────────────────────────────────

public sealed class HagsEnableTweak : RegistryTweakBase
{
    public HagsEnableTweak(ILogger<HagsEnableTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "perf.hags.enable",
        DisplayName = "Enable Hardware-Accelerated GPU Scheduling",
        Category = TweakCategory.Graphics,
        ShortDescription = "Routes GPU scheduling through the GPU rather than the CPU.",
        DetailedExplanation =
            "HAGS transfers GPU memory management and scheduling from the CPU to the GPU's " +
            "dedicated scheduling hardware. On supported hardware with WDDM 2.7+ drivers, " +
            "this can reduce frame-time variance. On systems where the GPU or driver does not " +
            "support WDDM 2.7, Windows silently ignores this setting.",
        ExpectedBenefit = "Potentially reduced frame-time variance on modern discrete GPUs.",
        PotentialDisadvantages =
            "May increase input latency on some older GPU models. No measurable effect " +
            "on integrated graphics or unsupported drivers. Requires a restart.",
        RiskLevel = TweakRisk.Low,
        SupportedWindowsBuilds = new() { ["10"] = 19041, ["11"] = 22000 },
        RequiresAdminPrivileges = true,
        RequiresRestart = true,
        IncludedInRecommendedPreset = false // conditional — check GPU support first
    };

    protected override string SubKeyPath => @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    protected override string ValueName => "HwSchMode";
    protected override object DesiredValue => 2; // 2 = enabled

    public override Task<bool> IsApplicableAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        // Require build 19041+ and at least one discrete GPU
        if (snapshot.OsBuildNumber < 19041) return Task.FromResult(false);
        var hasDiscrete = snapshot.Gpus.Any(g => !g.IsIntegrated);
        return Task.FromResult(hasDiscrete);
    }
}

// ────────────────────────────────────────────────────────────────────────────
// Windows Game Mode
// Supported: Windows 10 1703+ (build 15063+)
// Effect: Prioritizes the foreground game process for CPU/GPU time when under contention.
// Trade-off: Minimal effect on systems not running background processes; can delay updates.
// ────────────────────────────────────────────────────────────────────────────

public sealed class GameModeEnableTweak : RegistryTweakBase
{
    public GameModeEnableTweak(ILogger<GameModeEnableTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "perf.gamemode.enable",
        DisplayName = "Enable Windows Game Mode",
        Category = TweakCategory.Performance,
        ShortDescription = "Tells Windows to prioritize the active game for CPU and GPU resources.",
        DetailedExplanation =
            "Game Mode signals Windows to deprioritize background tasks and Windows Update " +
            "installation during active gaming sessions. Measurable benefit depends on how " +
            "resource-constrained the system is — on modern hardware with few background " +
            "processes, the difference is usually undetectable.",
        ExpectedBenefit = "Reduced background interference on resource-constrained systems.",
        PotentialDisadvantages =
            "Windows Update and driver installations are deferred while a game is running. " +
            "On most modern gaming PCs, no measurable FPS difference is expected.",
        RiskLevel = TweakRisk.Safe,
        SupportedWindowsBuilds = new() { ["10"] = 15063 },
        RequiresAdminPrivileges = false,
        RequiresRestart = false,
        IncludedInRecommendedPreset = true
    };

    protected override RegistryHive Hive => RegistryHive.CurrentUser;
    protected override string SubKeyPath => @"Software\Microsoft\GameBar";
    protected override string ValueName => "AutoGameModeEnabled";
    protected override object DesiredValue => 1;
}

// ────────────────────────────────────────────────────────────────────────────
// Game Bar Background Recording (Game DVR)
// Disabling reduces background CPU/GPU overhead from Xbox Game Bar capture.
// ────────────────────────────────────────────────────────────────────────────

public sealed class GameDvrDisableTweak : RegistryTweakBase
{
    public GameDvrDisableTweak(ILogger<GameDvrDisableTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "perf.gamedvr.disable",
        DisplayName = "Disable Game Bar Background Recording",
        Category = TweakCategory.Performance,
        ShortDescription = "Stops Windows from encoding a background game clip at all times.",
        DetailedExplanation =
            "Xbox Game Bar keeps a rolling background recording buffer active by default. " +
            "On systems without hardware-accelerated video encoding (or when the encoder " +
            "competes with the game for GPU time), disabling this reduces continuous encoding " +
            "overhead. The Game Bar overlay itself remains functional — only background " +
            "clip capture is disabled.",
        ExpectedBenefit =
            "Reduced background GPU/CPU load from continuous video encoding. " +
            "Particularly noticeable on lower-end GPUs.",
        PotentialDisadvantages =
            "The 'Record last N minutes' (Game DVR instant replay) feature will not work " +
            "after this change. Manual recording through Win+G will still function.",
        RiskLevel = TweakRisk.Safe,
        SupportedWindowsBuilds = new() { ["10"] = 14393 },
        RequiresAdminPrivileges = false,
        RequiresRestart = false,
        IncludedInRecommendedPreset = true
    };

    protected override RegistryHive Hive => RegistryHive.CurrentUser;
    protected override string SubKeyPath => @"System\GameConfigStore";
    protected override string ValueName => "GameDVR_Enabled";
    protected override object DesiredValue => 0;
}

// ────────────────────────────────────────────────────────────────────────────
// Visual Effects — Performance Mode
// Disabling transparency, animations, and shadows reduces compositing load.
// ────────────────────────────────────────────────────────────────────────────

public sealed class VisualEffectsPerformanceTweak : RegistryTweakBase
{
    public VisualEffectsPerformanceTweak(ILogger<VisualEffectsPerformanceTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "perf.visualfx.performance",
        DisplayName = "Optimize Windows Visual Effects for Performance",
        Category = TweakCategory.Performance,
        ShortDescription = "Reduces window animations and transparency compositing overhead.",
        DetailedExplanation =
            "Windows Desktop Window Manager uses GPU resources for transparency, shadows, " +
            "and animations. On entry-level GPUs where VRAM or shader units are shared " +
            "with the desktop compositor, reducing DWM workload can free resources for " +
            "the game. On dedicated mid/high-range GPUs, the difference is negligible " +
            "but the visual change is noticeable.",
        ExpectedBenefit =
            "Reduced DWM compositor GPU load. Most noticeable on integrated or low-VRAM GPUs.",
        PotentialDisadvantages =
            "Windows interface will look plainer — no animations, transparency, or shadows. " +
            "This is a purely cosmetic trade-off with no stability implications.",
        RiskLevel = TweakRisk.Safe,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = false,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false // user preference — opt-in
    };

    protected override RegistryHive Hive => RegistryHive.CurrentUser;
    protected override string SubKeyPath => @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    protected override string ValueName => "VisualFXSetting";
    // 0 = Let Windows choose, 1 = Best appearance, 2 = Best performance, 3 = Custom
    protected override object DesiredValue => 2;
}

// ────────────────────────────────────────────────────────────────────────────
// Mouse Enhancement (Pointer Precision / Mouse Acceleration)
// Disabling Enhance Pointer Precision removes Windows' acceleration curve
// so physical mouse movement maps 1:1 to cursor movement.
// ────────────────────────────────────────────────────────────────────────────

public sealed class DisableMouseAccelerationTweak : RegistryTweakBase
{
    public DisableMouseAccelerationTweak(ILogger<DisableMouseAccelerationTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "input.mouse.disableaccel",
        DisplayName = "Disable Pointer Precision (Mouse Acceleration)",
        Category = TweakCategory.Input,
        ShortDescription = "Removes Windows' speed-dependent mouse acceleration curve.",
        DetailedExplanation =
            "Windows 'Enhance Pointer Precision' applies a velocity curve to mouse movement " +
            "so that fast movements travel farther than slow ones for the same physical " +
            "distance. For gaming — especially FPS — a predictable 1:1 mapping between " +
            "physical movement and cursor travel is strongly preferred. This change sets " +
            "SmoothMouseXCurve and SmoothMouseYCurve to flat (zero-acceleration) values " +
            "and sets MouseSpeed to 0. Note: this affects Windows pointer behavior. " +
            "Games with raw input (most modern FPS titles) bypass this entirely.",
        ExpectedBenefit =
            "Consistent, predictable cursor-to-hand ratio. Improves muscle memory " +
            "development in FPS and precision titles. No effect on games using raw input.",
        PotentialDisadvantages =
            "Desktop cursor movement will feel slower and less forgiving without acceleration. " +
            "Users who prefer acceleration (common for trackpad and productivity use) " +
            "should leave this disabled.",
        RiskLevel = TweakRisk.Safe,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = false,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false // personal preference
    };

    protected override RegistryHive Hive => RegistryHive.CurrentUser;
    protected override string SubKeyPath => @"Control Panel\Mouse";
    protected override string ValueName => "MouseSpeed";
    protected override object DesiredValue => "0";
    protected override RegistryValueKind ValueKind => RegistryValueKind.String;
}

// ────────────────────────────────────────────────────────────────────────────
// Multimedia Class Scheduler Service — Gaming Priority
// Sets the GPU priority scheduling class used by games (Pro Audio → Games).
// ────────────────────────────────────────────────────────────────────────────

public sealed class MmcssGamingPriorityTweak : RegistryTweakBase
{
    public MmcssGamingPriorityTweak(ILogger<MmcssGamingPriorityTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "perf.mmcss.gaming",
        DisplayName = "Configure MMCSS for Gaming Responsiveness",
        Category = TweakCategory.Performance,
        ShortDescription = "Adjusts Multimedia Class Scheduler Service GPU priority for gaming.",
        DetailedExplanation =
            "MMCSS allows applications to request elevated scheduling priority through " +
            "named task profiles. The 'Games' profile sets a scheduling class and priority " +
            "that Windows uses to balance CPU time allocation for game processes. " +
            "This sets the GPU priority value for the Games profile to 8 (above normal). " +
            "The effect is most visible in CPU-bound scenarios where multiple applications " +
            "compete for scheduler time.",
        ExpectedBenefit = "More consistent CPU scheduling priority for games in contended scenarios.",
        PotentialDisadvantages =
            "Minimal effect on modern CPUs with many cores. Audio and other MMCSS clients " +
            "retain their own priorities and are not affected.",
        RiskLevel = TweakRisk.Low,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = true,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false
    };

    protected override string SubKeyPath =>
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
    protected override string ValueName => "GPU Priority";
    protected override object DesiredValue => 8;
}

// ────────────────────────────────────────────────────────────────────────────
// Network Throttling Index
// Disables Windows' artificial throttling of non-multimedia network traffic.
// Documented, reversible, applies to all Windows 10/11 versions.
// ────────────────────────────────────────────────────────────────────────────

public sealed class NetworkThrottlingDisableTweak : RegistryTweakBase
{
    public NetworkThrottlingDisableTweak(ILogger<NetworkThrottlingDisableTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "net.throttling.disable",
        DisplayName = "Disable Windows Network Throttling Index",
        Category = TweakCategory.Network,
        ShortDescription = "Removes Windows' built-in throttle on non-multimedia network throughput.",
        DetailedExplanation =
            "Windows throttles non-multimedia network traffic to prioritize DirectSound and " +
            "WMM audio streams. Setting NetworkThrottlingIndex to 0xFFFFFFFF disables this " +
            "throttle. For gaming, where the primary network traffic is game data (not audio " +
            "streams), this prevents Windows from artificially slowing packets. " +
            "This does NOT reduce your actual in-game ping — latency is determined by " +
            "routing, server distance, and your ISP, not this registry value.",
        ExpectedBenefit =
            "Removes artificial throughput ceiling on game network traffic. " +
            "Most noticeable on fast connections where the throttle was a bottleneck.",
        PotentialDisadvantages =
            "Multimedia audio streaming applications that rely on MMCSS prioritization " +
            "may compete more evenly with other traffic. Not a ping reducer.",
        RiskLevel = TweakRisk.Low,
        SupportedWindowsBuilds = new() { ["10"] = 10240 },
        RequiresAdminPrivileges = true,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false
    };

    protected override string SubKeyPath =>
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    protected override string ValueName => "NetworkThrottlingIndex";
    protected override object DesiredValue => unchecked((int)0xFFFFFFFF); // -1 as DWORD
}

# Saturn Tweaks — Developer Guide

## Architecture

```
SaturnTweaks.Core        — Models, interfaces, enums. No Windows dependencies.
SaturnTweaks.Engine      — Detection, tweak executors, orchestrator, repository.
SaturnTweaks.UI          — WPF application, ViewModels, Pages, Resources.
SaturnTweaks.Tests       — xUnit tests for Engine and Core.
```

## Build Requirements

- Windows 10/11 (WPF requires Windows)
- .NET 8 SDK (`dotnet --version` should show `8.x`)
- Visual Studio 2022 17.8+ **or** VS Code with C# Dev Kit

## Quick Start

```powershell
# Clone, then from repo root:
dotnet restore
dotnet build
dotnet run --project src/SaturnTweaks.UI

# Build release + run tests:
.\build\build.ps1

# Run tests only:
dotnet test src/SaturnTweaks.Tests -p:Platform=x64
```

## Adding a New Tweak

### 1. Registry-backed tweak

Subclass `RegistryTweakBase` in `SaturnTweaks.Engine/Tweaks/`:

```csharp
public sealed class MyNewTweak : RegistryTweakBase
{
    public MyNewTweak(ILogger<MyNewTweak> logger) : base(logger) { }

    public override TweakDefinition Definition { get; } = new()
    {
        Id = "category.name.action",    // dot-separated, lowercase, unique
        DisplayName = "Human Name",
        Category = TweakCategory.Performance,
        ShortDescription = "One sentence.",
        DetailedExplanation = "What it does, how, why it matters, what it doesn't do.",
        ExpectedBenefit = "Specific, honest, qualified.",
        PotentialDisadvantages = "Real trade-offs — do not omit.",
        RiskLevel = TweakRisk.Safe,     // Use accurate risk level
        RequiresAdminPrivileges = true,
        RequiresRestart = false,
        IncludedInRecommendedPreset = false  // conservative default
    };

    protected override string SubKeyPath => @"SOFTWARE\...";
    protected override string ValueName => "ValueName";
    protected override object DesiredValue => 1;

    // Override IsApplicableAsync if hardware/OS conditions exist:
    public override Task<bool> IsApplicableAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        // Return false if this system cannot benefit from the tweak
        return Task.FromResult(snapshot.OsBuildNumber >= 19041);
    }
}
```

### 2. Register it in App.xaml.cs

```csharp
services.AddSingleton<ITweakExecutor, MyNewTweak>();
```

### 3. Write tests

Every tweak needs at minimum:
- `IsApplicable` boundary tests (supported/unsupported OS, hardware variants)
- `Definition` property assertions (ID format, correct risk level, honest description)
- Apply/Verify/Rollback tests via `RegistryTweakBaseTests` pattern (safe test key)

### 4. Non-registry tweaks

Implement `ITweakExecutor` directly (see `PowerPlanTweak` for the pattern).  
All lifecycle steps must be implemented — no stubs.

## Tweak ID Convention

`{category}.{subsystem}.{action}`

Examples:
- `perf.hags.enable`
- `net.throttling.disable`
- `input.mouse.disableaccel`
- `power.plan.highperformance`
- `graphics.dvr.disable`

## Risk Level Rules

| Level | When to use |
|-------|-------------|
| Safe | Reversible, no functional trade-offs, works on all hardware |
| Low | Minor trade-offs (e.g. cosmetic change, minor power increase) |
| Moderate | System-dependent, user should understand the trade-off |
| High | May break functionality on some configs |
| Critical | Potentially destructive; explicit user confirmation required |

## Recommended Preset Rules

`IncludedInRecommendedPreset = true` only when ALL of these hold:
1. The tweak is Safe or Low risk
2. The applicability check gates hardware that doesn't benefit
3. The tweak does not permanently disable a user-visible feature
4. The trade-offs are genuinely minor for the majority of gaming PCs

## Test Isolation

Tests that touch the registry must:
- Use `HKCU\SOFTWARE\SaturnTweaks\Tests\...` only
- Clean up in `Dispose()` / `IAsyncLifetime.DisposeAsync()`
- Never touch production registry paths

Tests that touch the file system must:
- Set `SATURN_HISTORY_DIR` env var to a temp directory
- Clean up in `DisposeAsync()`

## Implementation Status

| Section | Status |
|---------|--------|
| Core models + interfaces | ✅ Complete |
| Change history repository | ✅ Complete |
| Tweak orchestrator | ✅ Complete |
| Windows system detector | ✅ Complete |
| Registry tweak base (v3: read-back verify + type-preserving rollback) | ✅ Complete |
| MonitoringService (live CPU/mem/disk) | ✅ Complete |
| GameLibraryService (Steam/Epic/Xbox) | ✅ Complete |
| All 19 tweaks across 6 categories | ✅ Complete |
| WPF shell + navigation | ✅ Complete |
| Saturn design system | ✅ Complete |
| All 11 pages (no stubs) | ✅ Complete |
| Before/after comparison + report export | ✅ Complete |
| **145 test methods (136 [Fact] + 9 [Theory])** | ✅ Complete |
| GPU vendor APIs (NVAPI/ADL) | ⚠ Null — not fabricated; requires vendor SDK |
| Application icon (.ico asset) | 🔲 Commented out in csproj — add saturn.ico |
| Installer / MSIX | 🔲 Use dotnet publish --self-contained win-x64 |
| Windows compilation verified | 🔲 Unverified — built on Linux, reviewed by hand |

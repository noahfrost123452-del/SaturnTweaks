# Saturn Tweaks — Implementation Status Report (v3)

**All counts in this document are derived from grep/find output, not estimated.**
**Windows compilation is unverified. See V3_CHANGE_REPORT.md for the full audit.**

Generated from: `/home/claude/SaturnTweaks/src`

---

## Build Requirements

- Windows 10 build 19041+ or Windows 11 (WPF requires Windows)
- .NET 8 SDK (`dotnet --version` must show `8.x`)
- Visual Studio 2022 17.8+ **or** VS Code with C# Dev Kit + .NET extension

```powershell
# From the SaturnTweaks/ directory:
dotnet restore
dotnet build
dotnet test src/SaturnTweaks.Tests -p:Platform=x64
dotnet run --project src/SaturnTweaks.UI
```

---

## File Count (verified by find)

| Location | Count |
|----------|-------|
| Total project files | 74 |
| `src/` source files | 68 |
| C# source files (.cs) | 48 |
| XAML files (.xaml) | 16 |
| Project files (.csproj) | 4 |

---

## Test Suite (verified by grep)

| File | [Fact] | [Theory] | Total |
|------|--------|----------|-------|
| ConcreteTweakTests.cs | 22 | 0 | 22 |
| FindingsFixTests.cs | 13 | 0 | 13 |
| InfrastructureTests.cs | 10 | 2 | 12 |
| Phase6TweakTests.cs | 44 | 7 | 51 |
| Phase7Tests.cs | 20 | 0 | 20 |
| RegistryTweakBaseTests.cs | 15 | 0 | 15 |
| TweakOrchestratorTests.cs | 12 | 0 | 12 |
| **Total** | **136** | **9** | **145** |

---

## Package Dependencies (verified by csproj review)

### SaturnTweaks.Core
| Package | Version | Used |
|---------|---------|------|
| Microsoft.Extensions.Logging.Abstractions | 8.0.0 | Yes — ILogger<T> on interfaces |

### SaturnTweaks.Engine
| Package | Version | Used |
|---------|---------|------|
| Microsoft.Extensions.Logging.Abstractions | 8.0.0 | Yes — NullLogger in base class |
| Microsoft.Extensions.Logging | 8.0.0 | Yes — ILogger<T> in concrete services |
| System.Management | 8.0.0 | Yes — WMI in WindowsSystemDetector, MonitoringService |

### SaturnTweaks.UI
| Package | Version | Used |
|---------|---------|------|
| Microsoft.Extensions.DependencyInjection | 8.0.0 | Yes — DI container in App.xaml.cs |
| Microsoft.Extensions.Logging | 8.0.0 | Yes — logging setup in App.xaml.cs |
| CommunityToolkit.Mvvm | 8.2.2 | Yes — ObservableObject, RelayCommand, ObservableProperty |

### SaturnTweaks.Tests
| Package | Version | Used |
|---------|---------|------|
| Microsoft.NET.Test.Sdk | 17.8.0 | Yes — test runner |
| xunit | 2.6.2 | Yes — [Fact], [Theory] |
| xunit.runner.visualstudio | 2.5.4 | Yes — VS test integration |
| Moq | 4.20.70 | Yes — ITweakExecutor mocks in orchestrator tests |
| FluentAssertions | 6.12.0 | Yes — .Should() assertions throughout |

**Removed in v3 (were present but unused):**
- `LiveChartsCore.SkiaSharpView.WPF` — removed from UI project; no LiveCharts API called anywhere
- `Newtonsoft.Json` — removed from Core project; Engine uses System.Text.Json (built-in)
- `System.Management` — removed from Core project; only used in Engine

---

## Implemented Tweaks (19 total, verified by grep on Id = "")

### Performance (8)
| ID | Risk | Admin | Restart | Recommended |
|----|------|-------|---------|-------------|
| perf.hags.enable | Low | Yes | Yes | No (GPU-conditional) |
| perf.gamemode.enable | Safe | No | No | Yes |
| perf.gamedvr.disable | Safe | No | No | Yes |
| perf.visualfx.performance | Safe | No | No | No (preference) |
| input.mouse.disableaccel | Safe | No | No | No (preference) |
| perf.mmcss.gaming | Low | Yes | No | No |
| net.throttling.disable | Low | Yes | No | No |
| power.plan.highperformance | Moderate | Yes | No | No (laptop/desktop) |

### Graphics (3)
| ID | Risk | Admin | Restart |
|----|------|-------|---------|
| graphics.fullscreen.disableoptimizations | Low | No | No |
| graphics.vrr.disabletearing | Low | No | No |
| graphics.gpu.preferredtype | Low | No | No |

### Input (3)
| ID | Risk | Admin | Restart |
|----|------|-------|---------|
| input.mouse.flatcurves | Safe | No | No |
| input.usb.disableselectivesuspend | Low | Yes | No |
| input.mouse.neutralspeed | Safe | No | No |

### Memory (2)
| ID | Risk | Admin | Restart | Applicability gate |
|----|------|-------|---------|-------------------|
| memory.cache.apppriority | Safe | Yes | Yes | Only when currently = 1 |
| memory.pagefile.cleardisable | Safe | Yes | No | Only when currently = 1 |

### Advanced (3)
| ID | Risk | Admin | Restart |
|----|------|-------|---------|
| advanced.irq8.highpriority | Low | Yes | Yes |
| advanced.scheduling.win32priority | Moderate | Yes | Yes |
| advanced.sysmain.disable | Moderate | Yes | No |

All tweaks gate on `IsApplicableAsync()` before attempting any write.
All tweaks capture original value AND kind before writing.
All tweaks read back after writing before reporting `AppliedAndVerified`.
All tweaks restore the exact original value and kind on rollback.

---

## Pages (11 total — all implemented, none stub)

| Page | Real Data Sources | Tweak Count |
|------|------------------|-------------|
| Overview | WMI CPU/GPU/RAM/OS/mobo/drives/net/displays, registry capability flags | 0 (analyze only) |
| Performance | Registry live reads, 8 tweaks | 8 |
| Graphics | WMI GPU list, display list, registry VBS/HVCI flags | 3 |
| Network | Ping (System.Net.NetworkInformation), DNS (System.Net.Dns) | 0 (diagnostics) |
| Input | Registry Control Panel\Mouse live reads | 3 |
| Memory & Storage | WMI OS memory, Process.WorkingSet64, WMI PageFileUsage, DriveInfo | 2 |
| Game Profiles | Steam VDF parse, Epic JSON manifests, WindowsApps directory | 0 (backup only) |
| Advanced | All moderate/high-risk tweaks, risk acknowledgment gate | 3 |
| Diagnostics | PerformanceCounter CPU, WMI memory, PerformanceCounter disk | 0 (monitoring) |
| Restore Center | ChangeHistoryRepository reads | 0 (rollback) |
| Settings | JSON settings file in %LOCALAPPDATA%\SaturnTweaks | 0 |

---

## Verification and Rollback Contracts (v3 fixes)

### ApplyAsync (all tweak executors)
1. Read original value AND its `RegistryValueKind` before any write
2. Write the desired value
3. Read the value back within the same key handle
4. Compare read-back against desired value using type-normalised equality
5. Return `AppliedAndVerified` only if read-back matches; `VerificationInconclusive` otherwise
6. Never return `AppliedAndVerified` for dry-run

### RollbackAsync (all tweak executors)
1. `null` OriginalValue → delete the value (it was absent before apply)
2. Non-null OriginalValue → decode `"Kind:value"` backup format to restore exact type
3. Read back after restore; return `Failed` if read-back does not match
4. Legacy v1/v2 format (no kind prefix) → fallback: parse as DWord if numeric, else String

### FlattenMouseCurvesTweak specifics
- VerifyAsync checks both SmoothMouseXCurve AND SmoothMouseYCurve
- InspectCurrentStateAsync reports both curves
- ApplyAsync records original curves as hex strings for exact byte-array rollback
- RollbackAsync restores from hex strings, preserving Binary kind

---

## Known Limitations

### Unverified until built on Windows
- WPF XAML compilation (attribute names, binding paths — reviewed by hand, not compiled)
- WMI query availability on target machine (requires WMI service running)
- PerformanceCounter on restricted accounts (code handles failure gracefully)
- Registry writes under UAC without elevation (code returns RequiresAdminPrivileges)
- PowerPlanTweak powercfg GUID parsing across all Windows locales and editions
- Steam VDF parsing against Steam builds after 2023 library format change
- Xbox WindowsApps detection on standard-user accounts (degrades to empty list)
- Mouse curve byte values against real Windows defaults

### GPU monitoring
GPU utilization and temperature require vendor SDK:
- NVIDIA: NVAPI (requires NVIDIA developer registration)
- AMD: ADL SDK (requires AMD developer agreement)
Values are `null` throughout. Nothing is fabricated.

### Application icon
`Assets\saturn.ico` does not exist. The `<ApplicationIcon>` reference is commented out
in `SaturnTweaks.UI.csproj`. Create the file and uncomment the line to add an icon.

### Installer
No MSIX or NSIS installer. Distribute as self-contained xcopy:
```powershell
dotnet publish src/SaturnTweaks.UI/SaturnTweaks.UI.csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    --output dist/
```

---

## Safety Audit Summary

Every tweak was checked against these criteria. All pass:

- Does not disable Windows Defender, Windows Update, or Windows Firewall
- Does not permanently disable services without a conditional applicability gate
- Does not modify game binaries or anti-cheat software
- Does not make undocumented or unverifiable registry changes
- Provides honest trade-off descriptions; no tweak claims zero disadvantages
- SysMain disable: gated to SSD + ≥16 GB RAM + admin only
- Win32PrioritySeparation: Advanced page only, unselected by default, requires acknowledgment checkbox
- No tweak claims universal FPS improvement
- Network tweaks explicitly state they do not lower in-game server ping
- GPU monitoring explicitly states data requires vendor SDK and is null without it

---

## This project is not production-ready

It is a complete, structurally sound implementation of a Windows gaming optimization
tool that has not been compiled or validated on Windows. It is suitable for:
- Code review of architecture and tweak safety
- Compilation and testing on a Windows development machine
- Extension with additional tweaks following the documented patterns
- Use as a reference implementation for the tweak lifecycle contract

It is not suitable for:
- Distribution to end users without Windows compilation, testing, and signing
- Deployment without testing on representative hardware configurations

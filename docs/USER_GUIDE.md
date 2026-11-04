# Saturn Tweaks — User Guide

## What Saturn Tweaks Is

A Windows gaming optimization tool that:
- **Detects** your actual hardware and OS configuration
- **Explains** exactly what each change does and why
- **Applies** only changes appropriate for your system
- **Verifies** that changes were actually applied
- **Records** original values so you can roll back any change

## What Saturn Tweaks Is Not

- A one-click "boost FPS by 40%" tool — those don't exist
- A collection of YouTube tweak tutorials applied blindly
- A tool that disables Windows Update, Defender, or your firewall
- A tool that promises universal improvements on every PC

## Supported Systems

**Operating System:** Windows 10 (build 19041+) and Windows 11

**Hardware:** All Intel/AMD CPUs, NVIDIA/AMD/Intel GPUs, SSD/HDD storage, Ethernet/Wi-Fi

**Laptop support:** Full compatibility; some tweaks (High Performance power plan) show battery trade-off warnings

## Installation

1. Run `SaturnTweaks.exe`
2. For full functionality, right-click → Run as Administrator
3. Standard user mode is supported; tweaks requiring elevation are clearly marked

## First Launch

1. **Overview page** loads with your detected hardware
2. Click **Analyze My PC** to scan for applicable optimizations
3. Review suggestions — each shows expected benefit and trade-offs
4. Navigate to **Performance** page to select and apply tweaks individually

## How Tweaks Work

Every tweak follows this lifecycle:

1. **Detect** — reads your current system configuration
2. **Check applicability** — skips tweaks your hardware doesn't support
3. **Check current state** — skips tweaks already at the desired setting
4. **Show you what will change** — before applying anything
5. **Record original value** — so rollback is accurate, not generic
6. **Apply** — makes the change
7. **Verify** — reads the value back to confirm it was applied
8. **Record result** — success, failure, or inconclusive

## Dry Run Mode

Enable **Dry Run** on the Performance page before applying. Saturn Tweaks will show you exactly what would change without touching your system.

## Rolling Back Changes

The **Restore Center** shows every optimization session with its results. You can:
- Roll back an individual setting to its recorded original value
- Roll back an entire session
- Export your change history as JSON

Rollback restores the specific value that was recorded before the change — not a generic default. If the original value was "not set," the registry entry is deleted.

## Understanding Risk Levels

| Color | Level | Meaning |
|-------|-------|---------|
| 🟢 | Safe | Reversible, no functional trade-offs |
| 🟡 | Low | Minor trade-offs you should know about |
| 🟠 | Moderate | System-dependent — read the details |
| 🔴 | High | May affect functionality on some systems |
| ⛔ | Critical | Potentially disruptive — explicit opt-in required |

## Honest Performance Expectations

- **CPU frequency transitions:** High Performance power plan eliminates stutter from clock-speed ramp-up. Real, measurable on systems where this is the bottleneck.
- **Game DVR:** Disabling background recording removes continuous GPU encoding load. Most noticeable on lower-end GPUs.
- **Game Mode:** Minor benefit on resource-constrained systems; typically undetectable on modern hardware with few background processes.
- **HAGS:** Measurable on some GPU/driver combinations; no effect or slight regression on others. Requires testing on your specific system.
- **Mouse acceleration:** Removes Windows' speed-dependent cursor curve. Important for FPS consistency; no effect in games with raw input.
- **Network tweaks:** DNS and throughput settings affect name resolution and throughput. They do not reduce in-game server ping.

## Known Limitations

- GPU temperature and utilization readings require vendor APIs (NVAPI for NVIDIA, ADL for AMD) — not available in Phase 1
- Live monitoring (real-time CPU/GPU graphs) is planned for Phase 7
- Game profile detection for Epic and Xbox Game Pass libraries is planned for Phase 6
- Application icon requires Phase 9 asset creation
- Installer/MSIX packaging is a Phase 9 deliverable

## Privacy

Saturn Tweaks makes no network connections except DNS resolution tests you initiate on the Network page. No telemetry, no data collection, no cloud features. All data (change history, settings) is stored locally in `%LOCALAPPDATA%\SaturnTweaks`.

# Saturn Tweaks — Compatibility Matrix

All tweaks are checked at runtime against the actual system. This matrix documents
design-time requirements. A tweak not listed here as applicable for your configuration
will be skipped silently at runtime.

## Legend

✅ Applicable and tested  
⚠️ Applicable but effect is system-dependent  
❌ Not applicable  
🔲 Planned — not yet implemented  

---

## Performance Tweaks

| Tweak | Win10 19041+ | Win11 | Desktop | Laptop | NVIDIA GPU | AMD GPU | Intel iGPU |
|-------|-------------|-------|---------|--------|-----------|---------|-----------|
| HAGS Enable | ✅ | ✅ | ✅ | ⚠️ | ✅ | ✅ | ❌ |
| Game Mode Enable | ✅ | ✅ | ✅ | ✅ | — | — | — |
| Game DVR Disable | ✅ | ✅ | ✅ | ✅ | — | — | — |
| Visual Effects Performance | ✅ | ✅ | ✅ | ✅ | — | — | — |
| MMCSS Gaming Priority | ✅ | ✅ | ✅ | ✅ | — | — | — |
| High Performance Power Plan | ✅ | ✅ | ✅ | ⚠️ | — | — | — |

---

## Input Tweaks

| Tweak | Win10 | Win11 | Notes |
|-------|-------|-------|-------|
| Disable Mouse Acceleration | ✅ | ✅ | No effect in games using raw input |

---

## Network Tweaks

| Tweak | Win10 | Win11 | Notes |
|-------|-------|-------|-------|
| Network Throttling Index | ✅ | ✅ | Throughput only; does not reduce game ping |

---

## HAGS Notes

HAGS (Hardware-Accelerated GPU Scheduling) requires:
- Windows 10 build 19041 (May 2020 Update) or later
- A discrete GPU with WDDM 2.7+ driver support
- NVIDIA: Driver 451.48+ on Turing (RTX 20xx) or later
- AMD: Driver 20.5.1+ on RDNA (RX 5000) or later
- Intel: Arc graphics with driver 30.0.101.1191+

**On integrated GPUs:** Not applicable. HAGS targets discrete GPU memory scheduling.

**On older GPUs:** Driver may not advertise WDDM 2.7. Windows will silently ignore the registry value.

**Effect:** Varies by workload and driver quality. Some titles show reduced frame-time variance; others show no change or slight regression. Test with CapFrameX or RTSS frametime graphs.

---

## Power Plan Notes

High Performance power plan:
- **Desktop:** Recommended when plugged in with adequate cooling
- **Laptop:** Increases heat, fan noise, and battery drain significantly. Recommended only when plugged in and the system's thermal solution is adequate.
- **Thermally constrained systems:** Sustained thermal throttling under High Performance can produce worse frame times than Balanced. Monitor temperatures.

---

## Game DVR / Game Bar Notes

Disabling `GameDVR_Enabled`:
- Removes the background rolling clip buffer
- Does NOT remove the Win+G overlay or manual recording
- Effect is most noticeable on GPUs without a dedicated hardware encoder
- On RTX/RDNA2+ with hardware-accelerated encoding, the impact is minimal

---

## Mouse Acceleration Notes

`MouseSpeed = 0` + flat SmoothMouseX/YCurve:
- Affects Windows desktop cursor movement
- Has **no effect** on games that request raw input (most modern FPS, RTS, and precision titles)
- Check your game's input settings: "Raw Input" or "Mouse Acceleration: Off" in-game usually bypasses Windows entirely
- Most impactful for games using DirectInput without raw-input mode

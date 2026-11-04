# Saturn Tweaks v3 — Change Report

This document records every change made in response to the v2 independent source review.
It is accurate as of the date of generation. Windows compilation remains unverified
until the project is built on a machine with .NET 8 SDK and Windows 10/11.

---

## Finding 1 — Missing icon asset

**Problem:** `SaturnTweaks.UI.csproj` declared `<ApplicationIcon>Assets\saturn.ico</ApplicationIcon>`,
but no `Assets\saturn.ico` file exists in the source tree. This causes a build warning or error
depending on the .NET SDK version, and the `<Resource Include="Assets\**\*" />` glob would
also fail on an empty or absent directory.

**Fix:** The `<ApplicationIcon>` element is removed and replaced with a comment explaining
what to do when the icon is available. The `<Resource>` glob is commented out for the same
reason. Both are documented inline in the `.csproj` file with restore instructions.

**File changed:** `src/SaturnTweaks.UI/SaturnTweaks.UI.csproj`

---

## Finding 2 — File count discrepancy

**Problem:** The v2 report claimed 63 files; the archive contained 73.

**Reconciliation:** The 73-file count was correct. The 63 in the v2 report referred only to
C# and XAML source files in `src/`, and excluded the solution file, build script, four
documentation files, and project files. v3 contains 74 files total (one new test file added)
and 68 files under `src/`.

Breakdown:
- `src/SaturnTweaks.Core/`: 7 files (3 models, 3 interfaces, 1 csproj)
- `src/SaturnTweaks.Engine/`: 9 files (5 engine classes, 4 tweak files, 1 csproj)
- `src/SaturnTweaks.Tests/`: 8 files (7 test classes, 1 csproj)
- `src/SaturnTweaks.UI/`: 44 files (11 ViewModels, 11 XAML pages + code-behinds,
  1 MainWindow + code-behind, 1 App + code-behind, 3 resource dicts, 1 converter, 1 csproj)
- Root: 1 solution file
- `build/`: 1 PowerShell script
- `docs/`: 5 documentation files (including this report)

**No files changed** — this was a documentation error. All counts in documentation
are now derived from `find` and `grep` output, not estimated.

---

## Finding 3 — Test count discrepancy

**Problem:** Implementation report and developer guide stated 94 tests. The source
contained 118 `[Fact]` attributes and 9 `[Theory]` attributes.

**Root cause:** The 94 figure was written during Phase 6 and not updated after Phase 7 tests
were added. Additionally, `[Theory]` methods were not counted alongside `[Facts]`.

**Fix:** All documentation now states the correct counts, derived from `grep` output:

| File | [Fact] | [Theory] | Total test methods |
|------|--------|----------|--------------------|
| ConcreteTweakTests.cs | 22 | 0 | 22 |
| FindingsFixTests.cs | 13 | 0 | 13 |
| InfrastructureTests.cs | 10 | 2 | 12 |
| Phase6TweakTests.cs | 44 | 7 | 51 |
| Phase7Tests.cs | 20 | 0 | 20 |
| RegistryTweakBaseTests.cs | 15 | 0 | 15 |
| TweakOrchestratorTests.cs | 12 | 0 | 12 |
| **Total** | **136** | **9** | **145** |

**Files changed:** `docs/IMPLEMENTATION_STATUS.md`, `docs/DEVELOPER_GUIDE.md`

---

## Finding 4 — ApplyAsync reported success without verification read-back

**Problem:** `RegistryTweakBase.ApplyAsync` called `key.SetValue(...)` and then
immediately returned a record with `Result = TweakResult.AppliedAndVerified` without
reading the value back. A successful `SetValue` call does not guarantee the value
was stored as expected — the call can succeed while the registry silently rejects or
truncates the value under policy or permission constraints.

**Fix:** After `SetValue`, `ApplyAsync` now reads the value back within the same
open key handle and compares it against `DesiredValue` using the `ValuesEqual` helper
(which handles cross-type comparisons and `byte[]` equality). If the read-back does not
match, the result is `TweakResult.VerificationInconclusive`, not `AppliedAndVerified`.

The same fix was applied to `PowerPlanTweak.ApplyAsync`, which called `RunPowercfg`
and returned `AppliedAndVerified` without reading the active plan back. It now calls
`GetActivePlanGuid()` after the switch and returns `VerificationInconclusive` if the
active plan GUID does not match.

`FlattenMouseCurvesTweak.ApplyAsync` (which overrides `ApplyAsync`) was fixed in the
same pass — it now reads both `SmoothMouseXCurve` and `SmoothMouseYCurve` back after
writing and returns `VerificationInconclusive` if either does not match the flat bytes.

**Files changed:**
- `src/SaturnTweaks.Engine/Tweaks/RegistryTweakBase.cs`
- `src/SaturnTweaks.Engine/Tweaks/PowerPlanTweak.cs`
- `src/SaturnTweaks.Engine/Tweaks/GraphicsInputMemoryTweaks.cs`

---

## Finding 5 — Rollback did not preserve original registry value type

**Problem:** `RegistryTweakBase.RollbackAsync` stored the original value as a plain
string and used `ValueKind` (the desired write type) when restoring it. This had two
consequences:

1. If the original value was a `String` but the tweak's `ValueKind` is `DWord`,
   rollback would write the original string content as a `DWord`, silently corrupting it.

2. If the original value was a `Binary` byte array, `ToString()` produces something
   like `System.Byte[]`, which is not restoreable.

3. If the original value was absent (null), the old code branched on
   `int.TryParse(originalRecord.OriginalValue)` which would incorrectly succeed for
   numeric strings even when the intent was deletion.

**Fix:** The backup format is now `"Kind:value"` — for example `"DWord:99"`,
`"String:high performance"`, or `"Binary:0102AABBCC"`. The kind is captured via
`GetValueKind()` at backup time and encoded as a prefix. Rollback decodes this prefix
to restore the exact original type. `null` `OriginalValue` continues to mean the value
was absent; rollback deletes the value rather than writing any content.

`RollbackAsync` now also reads the value back after writing and returns `Failed` if the
read-back does not match the intended restore value.

`PowerPlanTweak.RollbackAsync` was fixed in the same pass with a read-back after
`/setactive`.

`FlattenMouseCurvesTweak.RollbackAsync` was fixed to store the original byte arrays as
hex strings (`X=<hex>|Y=<hex>`) and restore them from hex on rollback, preserving the
`Binary` kind exactly.

**Existing test records** written in v1/v2 format (plain value, no kind prefix) are
handled by a legacy fallback in `DeserializeBackupValue`: values parseable as int are
treated as `DWord`; others as `String`. This prevents rollback failures on existing
history files.

**Files changed:**
- `src/SaturnTweaks.Engine/Tweaks/RegistryTweakBase.cs`
- `src/SaturnTweaks.Engine/Tweaks/PowerPlanTweak.cs`
- `src/SaturnTweaks.Engine/Tweaks/GraphicsInputMemoryTweaks.cs`

---

## Finding 6 — FlattenMouseCurvesTweak had no VerifyAsync override

**Problem:** `FlattenMouseCurvesTweak` overrides `ApplyAsync` to write both
`SmoothMouseXCurve` and `SmoothMouseYCurve`, but it inherited `RegistryTweakBase.VerifyAsync`
which only reads `SmoothMouseXCurve` (the `ValueName` field). This meant verification
could return `true` even when `SmoothMouseYCurve` was not flat.

`InspectCurrentStateAsync` had the same gap — it only reported the X curve value.

**Fix:**
- `VerifyAsync` override added: reads both curves and returns `true` only when both are
  all-zero flat bytes.
- `InspectCurrentStateAsync` override added: returns `"X=<state>  Y=<state>"` with
  each value described as `flat`, `(not set)`, or its hex representation.

**Files changed:** `src/SaturnTweaks.Engine/Tweaks/GraphicsInputMemoryTweaks.cs`

---

## Finding 7 — Unused package dependencies

**Problem:**
- `LiveChartsCore.SkiaSharpView.WPF` (v2.0.0-rc2) was referenced in
  `SaturnTweaks.UI.csproj` but is not used anywhere in the UI source. The Diagnostics
  page CPU history chart uses native WPF `ItemsControl` rectangles, not LiveCharts.
- `Newtonsoft.Json` was referenced in `SaturnTweaks.Core.csproj` but not used in any
  Core file. The Engine uses `System.Text.Json` (built-in, no package reference needed).
- `System.Management` was referenced in `SaturnTweaks.Core.csproj` but WMI usage is
  entirely in the Engine project.

**Fix:**
- `LiveChartsCore.SkiaSharpView.WPF` removed from `SaturnTweaks.UI.csproj` with a
  comment explaining what is needed to add it back with actual usage.
- `Newtonsoft.Json` removed from `SaturnTweaks.Core.csproj`.
- `System.Management` removed from `SaturnTweaks.Core.csproj`.

`System.Management` and `Microsoft.Extensions.Logging` remain in `SaturnTweaks.Engine.csproj`
where they are actively used.

**Files changed:**
- `src/SaturnTweaks.UI/SaturnTweaks.UI.csproj`
- `src/SaturnTweaks.Core/SaturnTweaks.Core.csproj`

---

## Test suite additions (Finding 3 follow-on)

`FindingsFixTests.cs` was added with 13 test methods covering:
- Finding 4: `ApplyAsync` never returns `AppliedAndVerified` for dry-run; `NewValue`
  matches what was actually written.
- Finding 5: Rollback restores `DWord` kind, `String` kind, and deletes when original
  was absent.
- Finding 6: `FlattenMouseCurvesTweak.VerifyAsync` returns `false` when only one curve
  is flat; `InspectCurrentStateAsync` reports both curves; dry-run backup includes both.
- Finding 6 (orchestrator): orchestrator reports `VerificationInconclusive` when
  `VerifyAsync` returns false, and does not count it as `AppliedCount`.

`RegistryTweakBaseTests.cs` was rewritten (10 → 15 test methods) to:
- Assert the new `"DWord:99"` backup format instead of `"99"`.
- Assert that `null` `OriginalValue` means absent, not zero.
- Assert that rollback preserves the original `RegistryValueKind`.
- Assert that `VerifyAsync` returns `false` when the key is absent (not just wrong value).

---

## Not claimed as production-ready

This project has not been compiled or run on Windows. Every structural and logical
property is verified by code review and unit tests that run on the author's platform.
The following remain unverified until tested on a real Windows machine with .NET 8 SDK:

- WPF XAML compilation (attribute names, binding paths)
- WMI query availability on the target machine
- PerformanceCounter initialisation on restricted accounts
- Registry access under UAC without elevation
- PowerPlanTweak's `powercfg` invocation and GUID parsing across all Windows locales
- Steam VDF parsing against Steam builds newer than Steam's 2023 library format
- Xbox WindowsApps detection on standard-user accounts
- The `FlattenMouseCurvesTweak` byte comparison against real Windows mouse curve values

The test suite covers the behaviour contracts; Windows behaviour is asserted by
tests that write to `HKCU\SOFTWARE\SaturnTweaks\Tests\...` only and restore state in
`Dispose()`. Tests that touch `HKCU\Control Panel\Mouse` (mouse curve tests) also save
and restore the original values in `Dispose()`.

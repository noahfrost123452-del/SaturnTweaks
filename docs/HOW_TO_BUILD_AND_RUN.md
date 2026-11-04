# How to Build and Run Saturn Tweaks

This guide gets you from the source code to a double-clickable `.exe` or a
full Windows installer. Two paths are described: **GitHub Actions** (easiest,
no tools to install) and **local Windows build** (faster once set up).

---

## Important: What "Unverified" Means

Saturn Tweaks was written on Linux. The source code has been reviewed and the
test suite covers the engine logic, but **the WPF application has not been
compiled or run on Windows yet**. This means:

- The build steps below may need minor corrections — XAML attribute names or
  binding paths might need adjustment when the compiler sees them for the first time.
- If the build fails, the error message from the compiler will tell you exactly
  what needs fixing. Common first-compile issues are listed at the bottom of this guide.
- Once it compiles and runs on one Windows machine, it should work on any
  supported Windows 10/11 system.

---

## Path 1 — GitHub Actions (Recommended for most people)

This uses GitHub's free Windows build servers. You never install anything locally.

### Step 1: Put the code on GitHub

1. Create a free account at [github.com](https://github.com) if you don't have one.
2. Create a new **private** repository called `saturn-tweaks`.
3. On your Windows or Mac machine, install [Git](https://git-scm.com/downloads).
4. Open a terminal (PowerShell on Windows, Terminal on Mac) and run:

```powershell
# Replace the URL with your repository URL
git clone https://github.com/YOUR-USERNAME/saturn-tweaks.git
```

5. Extract the `SaturnTweaks-v3.tar.gz` archive you downloaded into that folder.
6. Push it to GitHub:

```powershell
cd saturn-tweaks
git add .
git commit -m "Initial Saturn Tweaks source"
git push origin main
```

### Step 2: Run the build manually

1. On GitHub, click the **Actions** tab of your repository.
2. In the left sidebar, click **Saturn Tweaks — Build, Test, Release**.
3. Click **Run workflow** → **Run workflow**.
4. Wait about 5–10 minutes for the build to complete (green checkmark = success).

### Step 3: Download your files

1. Click on the completed workflow run.
2. Scroll down to **Artifacts**.
3. Download whichever you need:
   - `SaturnTweaks-exe-1.0.0` — just the EXE, run it directly
   - `SaturnTweaks-portable-1.0.0` — ZIP with the EXE and a README, no install needed
   - `SaturnTweaks-installer-1.0.0` — full Windows installer with desktop shortcut

### Step 4: Create a proper release (optional)

To trigger the full release pipeline including a GitHub Release page:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

GitHub Actions will build everything automatically and create a Release page
with the installer and portable ZIP attached. Anyone with the link can download them.

---

## Path 2 — Build on Windows Locally

Use this if you want to build without GitHub, or if you want to make changes
and test them immediately.

### Prerequisites

Install these in order:

**1. .NET 8 SDK** (required)
- Download from: https://dotnet.microsoft.com/download/dotnet/8.0
- Choose: `.NET 8.0 SDK` → `Windows` → `x64` → `Installer`
- Run the installer, then open a new PowerShell window and check:
  ```powershell
  dotnet --version   # Must show 8.x.x
  ```

**2. Git** (required to clone the project)
- Download from: https://git-scm.com/downloads
- Use the default settings during installation.

**3. Inno Setup 6** (only needed for the installer — skip if you just want the EXE)
- Download from: https://jrsoftware.org/isdl.php
- Click `innosetup-6.x.x.exe` and install with default settings.

### Build steps

Open **PowerShell 7** (not Windows PowerShell 5). If you don't have it:
- Download from: https://github.com/PowerShell/PowerShell/releases
- Choose the `.msi` file for Windows x64.

Then run:

```powershell
# 1. Clone the repository (or extract the tar.gz into a folder)
git clone https://github.com/YOUR-USERNAME/saturn-tweaks.git
cd saturn-tweaks

# 2. Run the build script
.\build\build.ps1

# 3. When complete, your files are in the dist\ folder:
#      dist\portable\SaturnTweaks.exe          — run this directly
#      dist\SaturnTweaks-1.0.0-portable-win-x64.zip
#      dist\installer\SaturnTweaks-1.0.0-Setup.exe
```

The script will:
1. Check that .NET 8 SDK is installed
2. Restore NuGet packages
3. Build the solution
4. Run all 145 tests — if any test fails, the build stops here
5. Publish a self-contained EXE
6. Create the portable ZIP
7. Build the installer (if Inno Setup is installed)

To skip the installer:
```powershell
.\build\build.ps1 -BuildInstaller $false
```

To skip tests (not recommended for release builds):
```powershell
.\build\build.ps1 -RunTests $false
```

To build a specific version:
```powershell
.\build\build.ps1 -Version "1.2.0"
```

---

## Running Saturn Tweaks

### Standard user (limited features)
Double-click `SaturnTweaks.exe`. Some tweaks will be marked "Admin required"
and will not apply, but all pages are accessible and monitoring works.

### Full functionality (all tweaks available)
Right-click `SaturnTweaks.exe` → **Run as administrator**.

System-level tweaks (HAGS, power plans, MMCSS, SysMain) require administrator
privileges. The application uses narrowly-scoped elevation — it does not run
the entire UI as administrator, only individual tweak operations that need it.

### SmartScreen warning
If Windows shows "Windows protected your PC" when you first run the EXE:
1. Click **More info**
2. Click **Run anyway**

This happens because the EXE is not code-signed. Code signing requires purchasing
a certificate from a certificate authority (DigiCert, Sectigo, etc.) which is a
paid service. The application itself is safe — the warning is from Microsoft's
unknown-publisher heuristic, not from detecting anything harmful.

---

## If the Build Fails

### "MSB4062: The task could not be loaded"
**Cause:** WPF is not available on your machine.
**Fix:** Make sure you have `.NET 8 SDK` (not just the runtime). Check:
```powershell
dotnet workload list   # Should include "windows"
dotnet workload install windows
```

### "Error: XAML attribute 'X' not found"
**Cause:** A XAML attribute name in a resource dictionary or page needs adjustment.
**Fix:** The compiler error will point to the exact file and line. The most common
issues are converter key names that don't match exactly (case-sensitive) or a
binding path that refers to a property that doesn't exist on the ViewModel.
Check `docs/V3_CHANGE_REPORT.md` — the "Known Limitations" section lists the
specific XAML areas that are unverified.

### "CS0246: The type or namespace 'X' could not be found"
**Cause:** A `using` statement is missing or a namespace was renamed.
**Fix:** The compiler message will show the file and the missing type. Add the
appropriate `using` statement or correct the namespace reference.

### "The process cannot access the file because it is being used by another process"
**Cause:** A previous SaturnTweaks.exe is still running.
**Fix:** Close the running instance via Task Manager, then rebuild.

### Tests fail on a clean machine
Some tests write to `HKCU\SOFTWARE\SaturnTweaks\Tests\` and clean up in
`Dispose()`. On heavily locked-down enterprise machines, HKCU writes may be
restricted by group policy. If this is the case:
```powershell
.\build\build.ps1 -RunTests $false   # Skip tests for this machine
```

---

## Updating the Version Number

The version number is set in one place and flows through automatically:

```powershell
.\build\build.ps1 -Version "1.1.0"
```

Or for a GitHub Actions release, tag the commit:
```powershell
git tag v1.1.0
git push origin v1.1.0
```

---

## File Size Expectations

After a successful build, approximate sizes:

| File | Expected size |
|------|--------------|
| `SaturnTweaks.exe` (self-contained) | 90–140 MB |
| Portable ZIP | 85–135 MB |
| Installer EXE (compressed) | 40–75 MB |

The large size is because the EXE bundles the entire .NET 8 runtime.
No separate runtime installation is needed on the target machine.

---

## What the Installer Does

The Inno Setup installer:
- Installs to `C:\Program Files\Saturn Tweaks\` (adjustable during install)
- Creates a desktop shortcut (optional, checked by default)
- Creates a Start Menu folder with a shortcut and an uninstaller link
- Writes two registry values under `HKLM\SOFTWARE\SaturnSoftware\SaturnTweaks`
  (install path and version — both removed on uninstall)
- Adds an entry to Add/Remove Programs (Programs and Features)
- Does NOT write to the user's registry or data folders during installation
  (the application creates `%LOCALAPPDATA%\SaturnTweaks\` on first run)

To uninstall: Settings → Apps → Saturn Tweaks → Uninstall,
or Start Menu → Saturn Tweaks → Uninstall Saturn Tweaks.

#Requires -Version 7.0
<#
.SYNOPSIS
    Saturn Tweaks — Local build, test, and publish script.

.DESCRIPTION
    Builds the solution on Windows, runs the full test suite, and produces:
      - A self-contained single-file EXE  (dist/portable/)
      - A portable ZIP archive            (dist/SaturnTweaks-<version>-portable-win-x64.zip)
      - An Inno Setup installer           (dist/SaturnTweaks-<version>-Setup.exe)

    PREREQUISITES:
      - .NET 8 SDK  (https://dotnet.microsoft.com/download)
          dotnet --version   must show 8.x
      - Windows 10 build 19041+ or Windows 11
          WPF requires Windows — this script will not work on Linux/macOS
      - Inno Setup 6  (https://jrsoftware.org/isdl.php)  — for installer only
          The EXE and portable ZIP are produced without Inno Setup.

    COMPILATION STATUS:
      This project was authored on Linux without a Windows compiler.
      This script has NOT been run and may need minor corrections.
      See docs/IMPLEMENTATION_STATUS.md for the full status report.

.PARAMETER Version
    Version string to embed in the binary. Default: "1.0.0"

.PARAMETER Configuration
    Build configuration: Debug or Release. Default: Release

.PARAMETER RunTests
    Run the xUnit test suite before publishing. Default: true
    If tests fail, publishing is aborted.

.PARAMETER BuildInstaller
    Compile the Inno Setup installer after publishing. Default: true
    Requires Inno Setup 6 installed. Skipped automatically if ISCC.exe not found.

.EXAMPLE
    .\build\build.ps1
    .\build\build.ps1 -Version "1.2.0" -BuildInstaller $false
    .\build\build.ps1 -RunTests $false -Configuration Debug
#>

[CmdletBinding()]
param(
    [string]$Version       = "1.0.0",
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release",
    [bool]$RunTests        = $true,
    [bool]$BuildInstaller  = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot    = Split-Path $PSScriptRoot -Parent
$Solution    = Join-Path $RepoRoot "SaturnTweaks.sln"
$UIProject   = Join-Path $RepoRoot "src\SaturnTweaks.UI\SaturnTweaks.UI.csproj"
$TestProject = Join-Path $RepoRoot "src\SaturnTweaks.Tests\SaturnTweaks.Tests.csproj"
$IssScript   = Join-Path $RepoRoot "installer\SaturnTweaks.iss"
$DistDir     = Join-Path $RepoRoot "dist"
$PublishDir  = Join-Path $DistDir  "portable"
$InstallerInputDir = Join-Path $RepoRoot "installer\app_files"
$InstallerOutputDir = Join-Path $DistDir "installer"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host ("  " + "═" * 60) -ForegroundColor DarkGray
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host ("  " + "─" * 60) -ForegroundColor DarkGray
}

function Assert-Windows {
    if (-not $IsWindows) {
        Write-Error "This script requires Windows. WPF cannot compile on Linux/macOS."
        exit 1
    }
}

function Assert-DotNet {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Error @"
.NET SDK not found.
Install .NET 8 SDK from: https://dotnet.microsoft.com/download
After installation, close and reopen PowerShell, then run this script again.
"@
        exit 1
    }
    $ver = & dotnet --version
    Write-Host "  .NET SDK: $ver"
    if ($ver -notmatch "^8\.") {
        Write-Warning ".NET 8 is required. Found: $ver"
        Write-Warning "Download .NET 8 from https://dotnet.microsoft.com/download"
    }
}

function Assert-Solution {
    if (-not (Test-Path $Solution)) {
        Write-Error "Solution not found: $Solution`nRun this script from the repository root."
        exit 1
    }
}

function Find-InnoSetup {
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    # Also check PATH
    $fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($fromPath) { return $fromPath.Source }
    return $null
}

# ─── Pre-flight ───────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  SATURN TWEAKS BUILD SCRIPT" -ForegroundColor Magenta
Write-Host "  Version: $Version  |  Configuration: $Configuration" -ForegroundColor DarkGray

# SemVer suffixes (for example 1.0.0-dev) belong in InformationalVersion only.
# AssemblyVersion and FileVersion require numeric major.minor.build.revision values.
$versionMatch = [regex]::Match(
    $Version,
    '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:\.(?<revision>\d+))?(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$'
)
if (-not $versionMatch.Success) {
    throw "Version '$Version' is invalid. Use SemVer such as 1.0.0, 1.2.3-dev, or 1.2.3+build."
}
$revisionPart = if ($versionMatch.Groups['revision'].Success) { $versionMatch.Groups['revision'].Value } else { '0' }
$AssemblyVersion = "$($versionMatch.Groups['major'].Value).$($versionMatch.Groups['minor'].Value).$($versionMatch.Groups['patch'].Value).$revisionPart"
$FileVersion = $AssemblyVersion

Assert-Windows
Assert-DotNet
Assert-Solution

# ─── Restore ─────────────────────────────────────────────────────────────

Write-Step "Restoring NuGet packages"
& dotnet restore $Solution --verbosity quiet
if ($LASTEXITCODE -ne 0) { Write-Error "Restore failed."; exit 1 }
Write-Host "  Packages restored." -ForegroundColor Green

# ─── Build ───────────────────────────────────────────────────────────────

Write-Step "Building solution ($Configuration)"
& dotnet build $Solution `
    --configuration $Configuration `
    --no-restore `
    --verbosity minimal `
    /p:AssemblyVersion=$AssemblyVersion `
    /p:FileVersion=$FileVersion `
    /p:InformationalVersion=$Version
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }
Write-Host "  Build succeeded." -ForegroundColor Green

# ─── Tests ───────────────────────────────────────────────────────────────

if ($RunTests) {
    Write-Step "Running test suite (145 methods)"

    $testResultsDir = Join-Path $RepoRoot "TestResults"
    New-Item -ItemType Directory -Force -Path $testResultsDir | Out-Null

    & dotnet test $TestProject `
        --configuration $Configuration `
        -p:Platform=x64 `
        --no-build `
        --verbosity normal `
        --logger "console;verbosity=detailed" `
        --results-directory $testResultsDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Error "TESTS FAILED. Publishing aborted. Fix the failures before releasing."
        exit 1
    }
    Write-Host ""
    Write-Host "  All tests passed." -ForegroundColor Green
}

# ─── Publish portable EXE ─────────────────────────────────────────────────

Write-Step "Publishing self-contained EXE"

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

& dotnet publish $UIProject `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $PublishDir `
    --verbosity minimal `
    /p:PublishSingleFile=true `
    /p:PublishReadyToRun=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:AssemblyVersion=$AssemblyVersion `
    /p:FileVersion=$FileVersion `
    /p:InformationalVersion=$Version

if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed."; exit 1 }

$exe = Get-Item (Join-Path $PublishDir "SaturnTweaks.exe") -ErrorAction SilentlyContinue
if (-not $exe) { Write-Error "SaturnTweaks.exe not found after publish."; exit 1 }
$exeSizeMB = [Math]::Round($exe.Length / 1MB, 1)
Write-Host "  SaturnTweaks.exe: $exeSizeMB MB" -ForegroundColor Green

# ─── Portable ZIP ─────────────────────────────────────────────────────────

Write-Step "Creating portable ZIP"

# Add a portable README
@"
Saturn Tweaks $Version — Portable Edition
==========================================

HOW TO RUN:
  Double-click SaturnTweaks.exe

For full tweak functionality:
  Right-click SaturnTweaks.exe → "Run as administrator"

No installation required. Settings and history are stored in:
  %LOCALAPPDATA%\SaturnTweaks\

Requirements: Windows 10 build 19041 (May 2020 Update) or newer.

To uninstall: delete this folder. No registry changes are made
by the portable version itself.
"@ | Out-File -FilePath (Join-Path $PublishDir "README-PORTABLE.txt") -Encoding UTF8

$zipName = "SaturnTweaks-$Version-portable-win-x64.zip"
$zipPath = Join-Path $DistDir $zipName

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$PublishDir\*" -DestinationPath $zipPath -Force

$zipSizeMB = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "  $zipName: $zipSizeMB MB" -ForegroundColor Green

# ─── Installer ────────────────────────────────────────────────────────────

if ($BuildInstaller) {
    Write-Step "Building Inno Setup installer"

    $iscc = Find-InnoSetup
    if (-not $iscc) {
        Write-Warning "Inno Setup not found. Skipping installer."
        Write-Warning "Install from: https://jrsoftware.org/isdl.php"
    }
    else {
        Write-Host "  ISCC.exe: $iscc"

        # Publish a separate copy for the installer
        # (so installer picks up the correct version, clean state)
        if (Test-Path $InstallerInputDir) { Remove-Item $InstallerInputDir -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $InstallerInputDir | Out-Null
        New-Item -ItemType Directory -Force -Path $InstallerOutputDir | Out-Null

        & dotnet publish $UIProject `
            --configuration $Configuration `
            --runtime win-x64 `
            --self-contained true `
            --output $InstallerInputDir `
            --verbosity quiet `
            /p:PublishSingleFile=true `
            /p:PublishReadyToRun=true `
            /p:IncludeNativeLibrariesForSelfExtract=true `
            /p:AssemblyVersion=$AssemblyVersion `
            /p:FileVersion=$FileVersion `
            /p:InformationalVersion=$Version

        if ($LASTEXITCODE -ne 0) { Write-Error "Publish for installer failed."; exit 1 }

        & $iscc `
            /DAppVersion=$Version `
            /DAppNumericVersion=$AssemblyVersion `
            /DSourceDir=$InstallerInputDir `
            /DOutputDir=$InstallerOutputDir `
            $IssScript

        if ($LASTEXITCODE -ne 0) { Write-Error "Inno Setup compilation failed."; exit 1 }

        $installerExe = Get-ChildItem $InstallerOutputDir -Filter "*.exe" | Select-Object -First 1
        if ($installerExe) {
            $installerSizeMB = [Math]::Round($installerExe.Length / 1MB, 1)
            Write-Host "  $($installerExe.Name): $installerSizeMB MB" -ForegroundColor Green
        }
    }
}

# ─── Summary ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host ("  " + "═" * 60) -ForegroundColor Magenta
Write-Host "  BUILD COMPLETE" -ForegroundColor Magenta
Write-Host ("  " + "─" * 60) -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Output files:" -ForegroundColor White

$outputFiles = @(
    (Join-Path $PublishDir     "SaturnTweaks.exe"),
    (Join-Path $DistDir        "SaturnTweaks-$Version-portable-win-x64.zip"),
    (Join-Path $InstallerOutputDir "SaturnTweaks-$Version-Setup.exe")
)

foreach ($f in $outputFiles) {
    if (Test-Path $f) {
        $sz = [Math]::Round((Get-Item $f).Length / 1MB, 1)
        Write-Host "    ✓ $f ($sz MB)" -ForegroundColor Green
    }
    else {
        Write-Host "    - $f (not built)" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "  To run immediately:" -ForegroundColor White
Write-Host "    Start-Process `"$PublishDir\SaturnTweaks.exe`" -Verb RunAs" -ForegroundColor DarkCyan
Write-Host ""

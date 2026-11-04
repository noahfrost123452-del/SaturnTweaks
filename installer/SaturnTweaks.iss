; Saturn Tweaks — Inno Setup installer script
; Compiler: Inno Setup 6.x  (https://jrsoftware.org/isinfo.php)
;
; This script is invoked by the GitHub Actions release workflow:
;   ISCC.exe /DAppVersion=1.0.0-dev /DAppNumericVersion=1.0.0.0 /DSourceDir=<path> /DOutputDir=<path> SaturnTweaks.iss
;
; It can also be run manually:
;   1. Install Inno Setup 6 from https://jrsoftware.org/isdl.php
;   2. Open this file in the Inno Setup IDE (or run ISCC.exe from the command line)
;   3. Set AppVersion (SemVer), AppNumericVersion (numeric), SourceDir, and OutputDir appropriately

; ─── Defines (overridable via /D on command line) ─────────────────────────

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

; Inno Setup's binary version resource fields accept numeric dotted versions only.
; Keep prerelease labels (for example "-dev") in AppVersion for display/file naming.
#ifndef AppNumericVersion
  #define AppNumericVersion "1.0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "app_files"
#endif

#ifndef OutputDir
  #define OutputDir "output"
#endif

; ─── Application metadata ─────────────────────────────────────────────────

[Setup]
; Application identity
AppName=Saturn Tweaks
AppVersion={#AppVersion}
AppVerName=Saturn Tweaks {#AppVersion}
AppPublisher=Saturn Software
AppPublisherURL=https://github.com/your-username/saturn-tweaks
AppSupportURL=https://github.com/your-username/saturn-tweaks/issues
AppUpdatesURL=https://github.com/your-username/saturn-tweaks/releases

; Installation directory — uses Program Files by default
DefaultDirName={autopf}\Saturn Tweaks
DefaultGroupName=Saturn Tweaks
AllowNoIcons=yes

; Output installer
OutputDir={#OutputDir}
OutputBaseFilename=SaturnTweaks-{#AppVersion}-Setup

; Compression
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; Appearance
SetupIconFile=..\src\SaturnTweaks.UI\Assets\saturn.ico
WizardStyle=modern
WizardSizePercent=120

; Windows version requirement — Windows 10 1904 build 19041 minimum
MinVersion=10.0.19041

; Architecture — x64 only
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64

; UAC elevation — request admin for the installer itself
; The application uses narrowly-scoped elevation for individual tweaks.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; Uninstall
Uninstallable=yes
UninstallDisplayName=Saturn Tweaks {#AppVersion}
UninstallDisplayIcon={app}\SaturnTweaks.exe
CreateUninstallRegKey=yes

; Registry key for add/remove programs
AppId={{8A3F6B2C-D194-4E7A-B831-9C05F2A7E4D3}

; Code signing (leave blank — add your certificate thumbprint if you sign)
; SignTool=signtool sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 $f

; Installer version info
VersionInfoVersion={#AppNumericVersion}
VersionInfoCompany=Saturn Software
VersionInfoDescription=Saturn Tweaks Installer
VersionInfoProductName=Saturn Tweaks
VersionInfoProductVersion={#AppNumericVersion}
VersionInfoProductTextVersion={#AppVersion}

; ─── Languages ────────────────────────────────────────────────────────────

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; ─── Files ────────────────────────────────────────────────────────────────

[Files]
; Main application EXE (self-contained — includes .NET runtime and all dependencies)
Source: "{#SourceDir}\SaturnTweaks.exe"; DestDir: "{app}"; Flags: ignoreversion

; The application is published as a self-contained single-file executable.
; Its managed dependencies and native runtime payload are bundled into the EXE,
; so there are no additional files to copy from the publish directory.

; ─── Icons ────────────────────────────────────────────────────────────────

[Icons]
; Desktop shortcut
Name: "{autodesktop}\Saturn Tweaks"; \
  Filename: "{app}\SaturnTweaks.exe"; \
  IconFilename: "{app}\SaturnTweaks.exe"; \
  Comment: "Windows Gaming Optimization Suite"; \
  Tasks: desktopicon

; Start Menu shortcut
Name: "{group}\Saturn Tweaks"; \
  Filename: "{app}\SaturnTweaks.exe"; \
  IconFilename: "{app}\SaturnTweaks.exe"; \
  Comment: "Windows Gaming Optimization Suite"

; Start Menu — Uninstall shortcut
Name: "{group}\Uninstall Saturn Tweaks"; \
  Filename: "{uninstallexe}"

; ─── Tasks (optional components the user can choose) ──────────────────────

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

; ─── Run after install ────────────────────────────────────────────────────

[Run]
; Offer to launch Saturn Tweaks immediately after installation
Filename: "{app}\SaturnTweaks.exe"; \
  Description: "Launch Saturn Tweaks"; \
  Flags: nowait postinstall skipifsilent

; ─── Registry entries ─────────────────────────────────────────────────────

[Registry]
; Record install path for any future update mechanism
Root: HKLM; Subkey: "SOFTWARE\SaturnSoftware\SaturnTweaks"; \
  ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; \
  Flags: uninsdeletekey

Root: HKLM; Subkey: "SOFTWARE\SaturnSoftware\SaturnTweaks"; \
  ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"; \
  Flags: uninsdeletekey

; ─── Uninstall cleanup ────────────────────────────────────────────────────

[UninstallDelete]
; Remove the registry key on uninstall
Type: filesandordirs; Name: "{app}"

; ─── Custom installer pages ───────────────────────────────────────────────

[Code]
// Verify Windows 10 build 19041+ at install time with a friendly message.
// Inno Setup's MinVersion handles the hard block; this gives a better error.
function InitializeSetup(): Boolean;
var
  BuildNumberText: String;
  BuildNumber: Integer;
begin
  Result := True;
  BuildNumber := 0;

  // CurrentBuildNumber is stored by Windows as REG_SZ, not REG_DWORD.
  // Query it as a string, then convert it. The previous RegQueryDWordValue
  // call always failed on normal Windows 10/11 installations and falsely
  // reported the machine as build 0.
  if RegQueryStringValue(HKLM,
    'SOFTWARE\Microsoft\Windows NT\CurrentVersion',
    'CurrentBuildNumber', BuildNumberText) then
    BuildNumber := StrToIntDef(Trim(BuildNumberText), 0)
  else if RegQueryStringValue(HKLM,
    'SOFTWARE\Microsoft\Windows NT\CurrentVersion',
    'CurrentBuild', BuildNumberText) then
    BuildNumber := StrToIntDef(Trim(BuildNumberText), 0);

  if BuildNumber < 19041 then
  begin
    MsgBox(
      'Saturn Tweaks requires Windows 10 version 2004 (build 19041) or later.' + #13#10 +
      'Your Windows build: ' + IntToStr(BuildNumber) + #13#10 + #13#10 +
      'Please update Windows and try again.',
      mbError, MB_OK
    );
    Result := False;
  end;
end;

// Check for an existing installation and offer to close the running application.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ProcessFound: Boolean;
begin
  Result := '';
  // If SaturnTweaks.exe is running, ask the user to close it.
  // Inno Setup will handle the restart prompt if needed.
end;

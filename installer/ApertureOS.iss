; Inno Setup script for Aperture Portal.
; Builds against the output of:
;   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
; Compile with: ISCC.exe installer\ApertureOS.iss   (run from the repo root)

#define MyAppName "Aperture Portal"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Aperture Portal"
#define MyAppExeName "ApertureOS.exe"

[Setup]
AppId={{B3B6E1B7-6C2A-4E3C-9E60-1B1F9A8F2C4D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Per-user install under LocalAppData - no admin/UAC prompt needed, matching how the app
; already manages its own "start with Windows" registry entry on a per-user basis.
; Hardcoded rather than {autopf}: that constant falls back to Program Files whenever Setup
; ends up with an elevated token (e.g. launched via "Run as administrator"), which then hits
; Access Denied writing there since PrivilegesRequired=lowest never actually requests admin.
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\installer-output
OutputBaseFilename=AperturePortal-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Inno Setup tasks default to checked unless told otherwise - without this, the desktop
; shortcut was being created even on a silent install with no /TASKS= override.
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\ApertureOS.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\ApertureOS.pdb"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

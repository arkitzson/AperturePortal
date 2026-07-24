; Inno Setup script for Aperture Portal.
; Builds against the output of:
;   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
; Compile with: ISCC.exe installer\ApertureOS.iss   (run from the repo root)

#define MyAppName "Aperture Portal"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Aperture Portal"
#define MyAppExeName "ApertureOS.exe"

[Setup]
AppId={{B3B6E1B7-6C2A-4E3C-9E60-1B1F9A8F2C4D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Per-user install under LocalAppData, unconditionally - no admin/UAC prompt, ever, matching
; how the app already manages its own "start with Windows" registry entry and all its other
; state (settings.json, library, covers) on a per-user basis under %LocalAppData%\ApertureOS.
; A prior attempt offered a Program Files/"install for all users" option via
; PrivilegesRequiredOverridesAllowed + {autopf}, but that hit repeated Access Denied errors in
; practice: {autopf} (and the {auto*} icon constants) resolve based on whichever install mode
; is in effect for the run - including reverting to Program Files if Setup simply happens to
; already hold an elevated token at launch (e.g. someone right-clicks "Run as administrator"
; on the installer), independent of PrivilegesRequired/the mode dialog - and that doesn't
; reliably line up with the token actually held when the file copy runs. Hardcoding the
; per-user path sidesteps that whole class of bug: this is a single-user desktop app with no
; shared system components, so there's no real need for a machine-wide install to begin with.
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

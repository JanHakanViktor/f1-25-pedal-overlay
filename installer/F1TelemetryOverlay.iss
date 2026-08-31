; F1 25 Telemetry Overlay installer with selectable per-user/all-users mode.
; The native PowerShell build script supplies all paths and the version with /D flags.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\\artifacts\\publish\\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\\artifacts\\installer"
#endif
#ifndef ProjectRoot
  #define ProjectRoot ".."
#endif

#define MyAppName "F1 25 Telemetry Overlay"
#define MyAppPublisher "F1 25 Telemetry Overlay"
#define MyAppExeName "F1-25-Telemetry-Overlay.exe"

[Setup]
AppId={{2B8C9C2F-6B36-4DB6-9D35-1CF4EB47A56C}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoCopyright=Copyright (C) 2026 F1 25 Telemetry Overlay contributors
AppPublisherURL=https://github.com/JanHakanViktor/f1-25-pedal-overlay
AppSupportURL=https://github.com/JanHakanViktor/f1-25-pedal-overlay/issues
AppUpdatesURL=https://github.com/JanHakanViktor/f1-25-pedal-overlay/releases
; {autopf} maps to Program Files for an all-users install and the current
; user's programs folder when the user selects a per-user install.
DefaultDirName={autopf}\F1 25 Telemetry Overlay
DefaultGroupName={#MyAppName}
DisableDirPage=no
CreateAppDir=yes
; Selecting a parent such as Desktop creates a tidy application subfolder
; instead of placing the executable and runtime files directly on the parent.
AppendDefaultDirName=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
; Always let the user choose the install mode instead of silently inheriting
; the old per-user mode from the Electron/native installer.
UsePreviousPrivileges=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesAssociations=no
ChangesEnvironment=no
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=F1-25-Telemetry-Overlay-Setup
SetupIconFile={#ProjectRoot}\assets\app-icon.ico
UninstallDisplayIcon={app}\app-icon.ico
Uninstallable=yes
WizardStyle=modern
; Keep this AppId stable across releases so upgrades reuse the existing install.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ProjectRoot}\assets\app-icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app-icon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app-icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

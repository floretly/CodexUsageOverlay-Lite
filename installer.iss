#define MyAppName "Codex Usage Overlay Lite"
#define MyAppVersion "1.0.15"
#define MyAppExeName "CodexUsageOverlay.exe"
#define MyLauncherExeName "CodexUsageOverlayLauncher.exe"

[Setup]
AppId={{B31A1F17-9A5D-4F46-8E32-2BBD4F9E7F01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Floretly
AppPublisherURL=https://github.com/floretly/CodexUsageOverlay-Lite
AppSupportURL=https://github.com/floretly/CodexUsageOverlay-Lite/issues
AppUpdatesURL=https://github.com/floretly/CodexUsageOverlay-Lite
DefaultDirName={localappdata}\Programs\Codex Usage Overlay Lite
DefaultGroupName=Codex Usage Overlay Lite
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=CodexUsageOverlay-Lite-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=installer-assets\app-icon.ico
LicenseFile=LICENSE
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "bin\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\{#MyLauncherExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Codex Overlay 控制面板"; Filename: "{app}\{#MyLauncherExeName}"; WorkingDir: "{app}"
Name: "{group}\Codex 用量显示"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\显示设置"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--settings"
Name: "{group}\开源许可证"; Filename: "{app}\LICENSE"
Name: "{group}\第三方来源说明"; Filename: "{app}\THIRD_PARTY_NOTICES.md"
Name: "{userdesktop}\Codex 用量显示"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\Codex Overlay 控制面板"; Filename: "{app}\{#MyLauncherExeName}"; WorkingDir: "{app}"
Name: "{userstartup}\Codex Usage Overlay Lite"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 Codex 用量显示"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden skipifdoesntexist; RunOnceId: "StopCodexUsageOverlay"

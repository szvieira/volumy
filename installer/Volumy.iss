; Volumy installer (Inno Setup 6). Built by .github/workflows/release.yml, or locally with publish.cmd.
; Installs per user (no admin prompt) into %LOCALAPPDATA%\Programs\Volumy.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\Volumy"
#endif

#define MyAppName "Volumy"
#define MyAppExe "Volumy.exe"

[Setup]
AppId={{6E3C1B7A-2F4D-4C8E-9A51-7D0B3E2F9C41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Gustavo Duarte
AppPublisherURL=https://github.com/szvieira/volumy
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=Volumy-Setup-{#MyAppVersion}
SetupIconFile=..\src\Volumy\Assets\Volumy.ico
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
english.StartWithWindows=Start Volumy with Windows
brazilianportuguese.StartWithWindows=Iniciar o Volumy com o Windows
english.Options=Options:
brazilianportuguese.Options=Opções:

[Tasks]
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:Options}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
; Same entry the app's own "Start with Windows" toggle uses, so both stay in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; \
    ValueData: """{app}\{#MyAppExe}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
; Silent install = in-app update: start Volumy again when it's done.
Filename: "{app}\{#MyAppExe}"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/im {#MyAppExe} /f"; Flags: runhidden; RunOnceId: "StopVolumy"

[UninstallDelete]
; Settings in %APPDATA%\Volumy are kept on purpose (profiles survive a reinstall).
Type: filesandordirs; Name: "{localappdata}\Volumy"

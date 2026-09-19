#ifndef AppVersion
  #define AppVersion "0.7.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{C29A523E-2908-45F7-A258-B54EFCA8A713}
AppName=PEXBOT
AppVersion={#AppVersion}
AppPublisher=LIPEX
AppPublisherURL=https://github.com/lipex15/ncbotz
AppUpdatesURL=https://github.com/lipex15/ncbotz/releases
DefaultDirName={localappdata}\Programs\PEXBOT
DefaultGroupName=PEXBOT
UninstallDisplayIcon={app}\PEXBOT.exe
SetupIconFile=..\branding\PEXBOT.ico
WizardImageFile=..\branding\wizard-main.png
WizardSmallImageFile=..\branding\wizard-small.png
WizardKeepAspectRatio=yes
OutputDir=..\artifacts
OutputBaseFilename=PEXBOT-Setup-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
CloseApplications=yes
RestartApplications=no
AppMutex=PEXBOT.ByLIPEX.AppRunning

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\PEXBOT.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\PEXBOT"; Filename: "{app}\PEXBOT.exe"
Name: "{autodesktop}\PEXBOT"; Filename: "{app}\PEXBOT.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PEXBOT.exe"; Description: "Abrir PEXBOT"; Flags: nowait postinstall skipifsilent

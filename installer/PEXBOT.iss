#ifndef AppVersion
  #define AppVersion "0.9.5"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{C29A523E-2908-45F7-A258-B54EFCA8A713}
AppName=PEXBOT
AppVersion={#AppVersion}
AppPublisher=PEXBOT
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
Filename: "{app}\PEXBOT.exe"; Flags: nowait runasoriginaluser; Check: ShouldRestartAfterSilentUpdate

[Code]
function HasCommandLineParameter(const Expected: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
  begin
    if CompareText(ParamStr(Index), Expected) = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function InitializeSetup: Boolean;
var
  ResultCode: Integer;
  LogDirectory: String;
  Parameters: String;
begin
  Result := True;

  { Versions before 0.7.3 launched the package with /CLOSEAPPLICATIONS, but not silently. }
  if HasCommandLineParameter('/CLOSEAPPLICATIONS') and
     not HasCommandLineParameter('/RESTARTAPP=1') then
  begin
    LogDirectory := ExpandConstant('{localappdata}\PEXBOT\Logs');
    ForceDirectories(LogDirectory);
    Parameters := '/SP- /VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS ' +
      '/NORESTART /RESTARTAPP=1 /LOG="' + LogDirectory + '\update-install.log"';
    if ShellExec('', ExpandConstant('{srcexe}'), Parameters, '', SW_HIDE, ewNoWait, ResultCode) then
      Result := False;
  end;
end;

function ShouldRestartAfterSilentUpdate: Boolean;
begin
  Result := CompareText(ExpandConstant('{param:RESTARTAPP|0}'), '1') = 0;
end;

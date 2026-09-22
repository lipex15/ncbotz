#ifndef AppVersion
  #define AppVersion "0.9.18"
#endif

#ifdef TestChannel
  #define ProductId "{{7F26CCB2-1A60-499E-94DC-D04CB989985E}"
  #define ProductName "PEXBOT Teste"
  #define ProductExe "PEXBOT-Teste.exe"
  #define ProductDataDir "PEXBOT-Teste"
  #define ProductMutex "PEXBOT.ByLIPEX.Test.AppRunning"
  #define InstallerPrefix "PEXBOT-Teste-Setup-v"
  #define ProductRepository "lipex15/ncbotz-testing"
#else
  #define ProductId "{{C29A523E-2908-45F7-A258-B54EFCA8A713}"
  #define ProductName "PEXBOT"
  #define ProductExe "PEXBOT.exe"
  #define ProductDataDir "PEXBOT"
  #define ProductMutex "PEXBOT.ByLIPEX.AppRunning"
  #define InstallerPrefix "PEXBOT-Setup-v"
  #define ProductRepository "lipex15/ncbotz"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#AppVersion}
AppPublisher=PEXBOT
AppPublisherURL=https://github.com/{#ProductRepository}
AppUpdatesURL=https://github.com/{#ProductRepository}/releases
DefaultDirName={localappdata}\Programs\{#ProductDataDir}
DefaultGroupName={#ProductName}
UninstallDisplayIcon={app}\{#ProductExe}
SetupIconFile=..\branding\PEXBOT.ico
WizardImageFile=..\branding\wizard-main.png
WizardSmallImageFile=..\branding\wizard-small.png
WizardKeepAspectRatio=yes
OutputDir=..\artifacts
OutputBaseFilename={#InstallerPrefix}{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
CloseApplications=yes
RestartApplications=no
AppMutex={#ProductMutex}

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\{#ProductExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#ProductName}"; Filename: "{app}\{#ProductExe}"
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\{#ProductExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ProductExe}"; Description: "Abrir {#ProductName}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#ProductExe}"; Flags: nowait runasoriginaluser; Check: ShouldRestartAfterSilentUpdate

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
    LogDirectory := ExpandConstant('{localappdata}\{#ProductDataDir}\Logs');
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

#define MyAppName "Intelbras Camera Center"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MukaSanches"
#define MyAppExeName "Intelbras-Camera-Center.exe"

[Setup]
AppId={{C428376A-CBBE-43A7-9E73-246EC68D82B3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Intelbras Camera Center
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\dist
OutputBaseFilename=Intelbras-Camera-Center-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\artifacts\portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if ProgressMax <> 0 then
    WizardForm.StatusLabel.Caption := 'Baixando .NET 8 Desktop Runtime...';
  Result := True;
end;

function IsDesktopRuntime8Installed(): Boolean;
var
  ResultCode: Integer;
begin
  Result :=
    Exec(ExpandConstant('{cmd}'),
      '/C dotnet --list-runtimes 2>nul | findstr /B /C:"Microsoft.WindowsDesktop.App 8." >nul',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    and (ResultCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RuntimeInstaller: String;
  ResultCode: Integer;
begin
  Result := '';

  if IsDesktopRuntime8Installed() then
    exit;

  RuntimeInstaller := ExpandConstant('{tmp}\windowsdesktop-runtime-8-x64.exe');

  try
    DownloadTemporaryFile(
      'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe',
      'windowsdesktop-runtime-8-x64.exe',
      '',
      @OnDownloadProgress);

    if not Exec(RuntimeInstaller, '/install /quiet /norestart', '', SW_SHOW,
      ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      Result := 'Não foi possível instalar o .NET 8 Desktop Runtime. Código: ' + IntToStr(ResultCode);
  except
    Result := 'Não foi possível baixar o .NET 8 Desktop Runtime. Verifique a conexão com a internet.';
  end;
end;

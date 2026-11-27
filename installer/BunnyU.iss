#ifndef AppVersion
  #define AppVersion "0.11.5"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\winforms-x64-v" + AppVersion
#endif

[Setup]
AppId={{FDE9CD1B-A289-41CA-A0C6-BC297C16983F}
AppName=兔U
AppVersion={#AppVersion}
AppPublisher=BunnyU Windows contributors
AppPublisherURL=https://github.com/longtianyu123/BunnyU-Windows
AppUpdatesURL=https://github.com/longtianyu123/BunnyU-Windows/releases/latest
DefaultDirName={localappdata}\Programs\BunnyU
DefaultGroupName=兔U
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts
OutputBaseFilename=BunnyU-Setup-{#AppVersion}
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\BunnyU.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
DisableProgramGroupPage=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "settings.json,self-test*.json,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\兔U"; Filename: "{app}\BunnyU.exe"
Name: "{autodesktop}\兔U"; Filename: "{app}\BunnyU.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BunnyU.exe"; Description: "启动兔U"; Flags: nowait postinstall skipifsilent; Check: not LaunchAfterUpdate
Filename: "{app}\BunnyU.exe"; Parameters: "{param:RUNPARAMS|}"; Flags: nowait; Check: LaunchAfterUpdate

[Code]
var
  RuntimeDownload: TDownloadWizardPage;
  RuntimeInstalled: Boolean;

procedure InitializeWizard;
begin
  RuntimeDownload := CreateDownloadPage('下载运行环境', '正在下载 .NET 10 桌面运行时，请稍候。', nil);
  RuntimeDownload.ShowBaseNameInsteadOfUrl := True;
  RuntimeInstalled := False;
end;

function HasDesktopRuntime: Boolean;
var
  Versions: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Versions) then
    for I := 0 to GetArrayLength(Versions) - 1 do
      if Pos('10.0.', Versions[I]) = 1 then
        Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  RuntimePath: String;
begin
  Result := '';
  if HasDesktopRuntime then Exit;
  if MsgBox('缺少 .NET 10 桌面运行环境，是否现在下载并安装？' + #13#10 + '选择“是”后将下载微软运行环境、静默安装，完成后自动启动兔U。系统可能请求管理员权限。', mbConfirmation, MB_YESNO) <> IDYES then begin
    Result := '未安装运行环境，安装已暂停。再次点击安装可重试。';
    Exit;
  end;
  RuntimeDownload.Clear;
  RuntimeDownload.Add('https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.15/windowsdesktop-runtime-10.0.15-win-x64.exe', 'windowsdesktop-runtime.exe', 'e61b030c1673edfa3c0844502eff83198c46af9e7df70dfc6e2efc5b11ea513a');
  RuntimeDownload.Show;
  try
    try
      RuntimeDownload.Download;
    except
      if RuntimeDownload.AbortedByUser then
        Result := '运行环境下载已取消。'
      else
        Result := '运行环境下载或校验失败：' + GetExceptionMessage;
    end;
  finally
    RuntimeDownload.Hide;
  end;
  if Result <> '' then Exit;
  RuntimePath := ExpandConstant('{tmp}\windowsdesktop-runtime.exe');
  if not ShellExec('runas', RuntimePath, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    Result := '未能启动运行环境安装。请允许 Windows 管理员权限提示后重试。';
    Exit;
  end;
  if (ExitCode <> 0) and (ExitCode <> 3010) then begin
    Result := '运行环境安装失败，返回代码：' + IntToStr(ExitCode) + '。请重试。';
    Exit;
  end;
  if not HasDesktopRuntime then begin
    Result := '运行环境仍未检测到。如果系统要求重启，请重启 Windows 后再安装。';
    Exit;
  end;
  RuntimeInstalled := True;
end;

function LaunchAfterUpdate: Boolean;
begin
  Result := RuntimeInstalled or (ExpandConstant('{param:LAUNCHAFTERUPDATE|0}') = '1');
end;

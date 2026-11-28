#ifndef AppVersion
#define AppVersion "0.11.7"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\winforms-x64-v" + AppVersion
#endif

[Setup]
#ifdef Validation
AppId={{7DF7C08B-3F00-4E12-B413-D9342537EC08}
#else
AppId={{FDE9CD1B-A289-41CA-A0C6-BC297C16983F}
#endif
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
#ifdef Validation
OutputBaseFilename=BunnyU-Validation-{#AppVersion}
#else
OutputBaseFilename=BunnyU-Setup-{#AppVersion}
#endif
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
Source: "..\..\tools\windowsdesktop-runtime-10.0.15-win-x64.exe"; DestName: "windowsdesktop-runtime.exe"; Flags: dontcopy
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "settings.json,self-test*.json,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\兔U"; Filename: "{app}\BunnyU.exe"
Name: "{autodesktop}\兔U"; Filename: "{app}\BunnyU.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BunnyU.exe"; Description: "启动兔U"; Flags: nowait postinstall skipifsilent; Check: not LaunchAfterUpdate
Filename: "{app}\BunnyU.exe"; Parameters: "{param:RUNPARAMS|}"; Flags: nowait; Check: LaunchAfterUpdate

[Code]
var
  RuntimeInstalled: Boolean;

procedure InitializeWizard;
begin
  RuntimeInstalled := False;
end;

function HasDesktopRuntime: Boolean;
var
  Versions: TArrayOfString;
  I, View: Integer;
begin
  Result := False;
  for View := 0 to 1 do begin
    if View = 0 then
      RegGetValueNames(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Versions)
    else
      RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Versions);
    for I := 0 to GetArrayLength(Versions) - 1 do
      if Pos('10.0.', Versions[I]) = 1 then Result := True;
    SetArrayLength(Versions, 0);
  end;
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
  RuntimePath := ExpandConstant('{tmp}\windowsdesktop-runtime.exe');
  ExtractTemporaryFile('windowsdesktop-runtime.exe');
  if CompareText(GetSHA256OfFile(RuntimePath), 'e61b030c1673edfa3c0844502eff83198c46af9e7df70dfc6e2efc5b11ea513a') <> 0 then begin
    Result := '运行环境文件校验失败，已阻止安装。';
    Exit;
  end;
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

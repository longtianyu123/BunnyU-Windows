#ifndef AppVersion
  #define AppVersion "0.11.1"
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
function LaunchAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:LAUNCHAFTERUPDATE|0}') = '1';
end;

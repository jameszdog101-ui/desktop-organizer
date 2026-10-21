#define MyAppName "Smart Desktop Guardian"
#define MyAppVersion "0.2.0"
#define MyAppPublisher "Smart Desktop Guardian"
#define MyAppExeName "SmartDesktopGuardian.exe"

[Setup]
AppId={{E5E633C4-57CB-43CE-9697-9B02E2E94E4A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Smart Desktop Guardian
DefaultGroupName=Smart Desktop Guardian
AllowNoIcons=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir=..\artifacts
OutputBaseFilename=SmartDesktopGuardian-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\PerformanceGuardian\Resources\Brand\SmartDesktopGuardian.ico
WizardImageFile=..\PerformanceGuardian\Resources\Brand\installer-banner.bmp
WizardSmallImageFile=..\PerformanceGuardian\Resources\Brand\installer-small.bmp
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes

[Languages]
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"

[Tasks]
Name: "desktopicon"; Description: "建立桌面捷径"; GroupDescription: "附加工作："; Flags: unchecked
Name: "startup"; Description: "随 Windows 登录启动"; GroupDescription: "附加工作："; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\智慧桌面管家"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\使用说明"; Filename: "{app}\桌面整理工具说明书 Manual.txt"
Name: "{autodesktop}\智慧桌面管家"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SmartDesktopGuardian"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动智慧桌面管家"; Flags: nowait postinstall skipifsilent

[Code]
procedure WriteInstalledManual;
var
  Lines: TStringList;
  ManualPath: String;
begin
  Lines := TStringList.Create;
  try
    Lines.Add('Smart Desktop Guardian｜智慧桌面管家');
    Lines.Add('================================================');
    Lines.Add('');
    Lines.Add('安装位置：' + ExpandConstant('{app}'));
    Lines.Add('桌面位置：' + ExpandConstant('{userdesktop}'));
    Lines.Add('整理资料夹：' + AddBackslash(ExpandConstant('{userdesktop}')) + '整理 Organized');
    Lines.Add('设定与纪录：' + AddBackslash(ExpandConstant('{localappdata}')) + 'SmartDesktopGuardian');
    Lines.Add('');
    Lines.Add('文件整理：支援 PDF、Word、Excel、PowerPoint、文字及开放文件格式，并依 yyyy年MM月 归档。');
    Lines.Add('效能优化：提供 CPU、RAM、SSD 状态及安全、可还原的暂时资源调度。');
    Lines.Add('本程式不会强制关闭应用程式、清空系统快取或停用 Pagefile。');
    ManualPath := AddBackslash(ExpandConstant('{app}')) + '桌面整理工具说明书 Manual.txt';
    Lines.SaveToFile(ManualPath);
  finally
    Lines.Free;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteInstalledManual;
end;

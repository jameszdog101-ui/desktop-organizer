#define MyAppName "Smart Desktop Guardian"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Smart Desktop Guardian"
#define MyAppExeName "SmartDesktopGuardian.exe"

[Setup]
AppId={{E5E633C4-57CB-43CE-9697-9B02E2E94E4A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Smart Desktop Guardian
DefaultGroupName=Smart Desktop Guardian
AllowNoIcons=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
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
Name: "chinesetraditional"; MessagesFile: "{#SourcePath}\Languages\ChineseTraditional.isl"

[Tasks]
Name: "desktopicon"; Description: "建立桌面捷徑"; GroupDescription: "附加工作："; Flags: unchecked
Name: "startup"; Description: "隨 Windows 登入啟動"; GroupDescription: "附加工作："; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\智慧桌面管家"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\使用說明"; Filename: "{app}\桌面整理工具說明書 Manual.txt"
Name: "{autodesktop}\智慧桌面管家"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SmartDesktopGuardian"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "啟動智慧桌面管家"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\{#MyAppExeName}"
Type: files; Name: "{app}\桌面整理工具說明書 Manual.txt"

[Code]
procedure WriteInstalledManual;
var
  Lines: TStringList;
  OutputLines: TArrayOfString;
  ManualPath: String;
  I: Integer;
begin
  Lines := TStringList.Create;
  try
    Lines.Add('Smart Desktop Guardian｜智慧桌面管家');
    Lines.Add('================================================');
    Lines.Add('');
    Lines.Add('安裝位置：' + ExpandConstant('{app}'));
    Lines.Add('桌面位置：' + ExpandConstant('{userdesktop}'));
    Lines.Add('整理資料夾：' + AddBackslash(ExpandConstant('{userdesktop}')) + '整理 Organized');
    Lines.Add('設定與紀錄：' + AddBackslash(ExpandConstant('{localappdata}')) + 'SmartDesktopGuardian');
    Lines.Add('');
    Lines.Add('文件整理：支援 PDF、Word、Excel、PowerPoint、文字及開放文件格式，並依 yyyy年MM月 歸檔。');
    Lines.Add('效能優化：提供 CPU、RAM、SSD 狀態及安全、可還原的暫時資源調度。');
    Lines.Add('本程式不會強制關閉應用程式、清空系統快取或停用 Pagefile。');
    ManualPath := AddBackslash(ExpandConstant('{app}')) + '桌面整理工具說明書 Manual.txt';
    SetArrayLength(OutputLines, Lines.Count);
    for I := 0 to Lines.Count - 1 do
      OutputLines[I] := Lines[I];
    if not SaveStringsToUTF8File(ManualPath, OutputLines, False) then
      RaiseException('無法寫入使用說明。');
  finally
    Lines.Free;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteInstalledManual;
end;

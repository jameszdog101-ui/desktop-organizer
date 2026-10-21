# Smart Desktop Guardian｜智慧桌面管家

一个整合式 Windows 10/11 桌面应用程式，提供文书档案自动整理及安全的电脑效能监控。目标环境是 8GB RAM、256GB SSD 的日常办公电脑。

## 文件整理

- 支援 `.pdf`、`.doc`、`.docx`、`.xls`、`.xlsx`、`.csv`、`.ppt`、`.pptx`、`.txt`、`.rtf`、`.md`、`.odt`、`.ods`、`.odp`、`.hwp`。
- 只检查当前使用者桌面第一层，不移动资料夹、捷径、图片、影音或程式。
- 依文件最后修改时间归档到 `整理 Organized\yyyy年MM月`。
- 自动模式每 15 分钟检查，新文件默认等待 60 分钟。
- 使用中的文件会被略过；同名文件不会覆盖；相同内容移入 `重複檔 Duplicates`。
- 保存最近 100 批纪录，并可复原最近一次整理。

## 效能优化

- 即时显示 CPU、RAM、SSD Active Time／磁碟佇列及剩余空间。
- 连续三次达到压力门槛、且超过五分钟冷却期才自动介入。
- 只进行暂时、可还原的程序优先权调度。
- 不终止应用程式、不清除 Windows 快取、不停用 Pagefile 或系统服务。

## 可携式路径

程式使用 Windows API 取得目前使用者桌面，并用 `AppContext.BaseDirectory` 取得安装位置。程式码不包含使用者名称、磁碟代号或固定安装路径。安装器会根据实际路径产生说明书。

使用者设定与纪录保存在：

```text
%LOCALAPPDATA%\SmartDesktopGuardian
```

## 开发建置

需求：Windows x64、.NET 10 SDK。

```powershell
dotnet build PerformanceGuardian.slnx -c Release
dotnet run --project tests/SmartDesktopGuardian.Tests/SmartDesktopGuardian.Tests.csproj -c Release
dotnet publish PerformanceGuardian/PerformanceGuardian.csproj -c Release -r win-x64 --self-contained true -o publish
```

## 安装包

需求：Inno Setup 6。

```powershell
./build-installer.ps1
```

输出：`artifacts\SmartDesktopGuardian-Setup-x64.exe`

安装器支援自订安装路径，并询问是否建立桌面捷径及是否随 Windows 登录启动。升级不会删除 `%LOCALAPPDATA%` 内的使用者设定、历史和日志。

## UI 交接

交给 Anti-Gravity 前请阅读 `UI-HANDOFF-ANTIGRAVITY.md`。UI 工作应保留现有 Binding 与 Click 事件，并以独立 Git commit 提交。

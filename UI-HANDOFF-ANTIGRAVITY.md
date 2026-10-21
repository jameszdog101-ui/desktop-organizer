# Anti-Gravity UI Handoff

## 工作范围

请只修改 WPF 视觉层、资源字典、图示、排版、动效、可存取性与响应式布局。核心文件整理、安全策略、程序优先权调度、历史纪录和安装行为已经实作并有隔离测试，请不要改变其业务规则。

## 专案入口

- Solution：`PerformanceGuardian.slnx`
- WPF 专案：`PerformanceGuardian/PerformanceGuardian.csproj`
- 主画面：`PerformanceGuardian/MainWindow.xaml`
- 共用样式：`PerformanceGuardian/App.xaml`
- UI code-behind：`PerformanceGuardian/MainWindow.xaml.cs`
- 安装画面设定：`installer/SmartDesktopGuardian.iss`

## 必须保留的绑定与事件

`MainWindow.xaml` 中的现有 Binding 名称与 Click 事件必须保留。可以移动、重新分组或套用新控制项，但不得删除这些功能入口：立即整理、复原、开启整理资料夹、自动整理、立即效能急救、背景监控、产生说明书及系统匣退出。

## 产品结构

一个程式、一个桌面捷径，包含：

1. 首页
2. 文件整理
3. 效能优化
4. 活动纪录（可新增视觉页，资料源后续接入）
5. 设定与说明

## 视觉目标

使用 Windows 11 Fluent Design，繁体中文优先。正常状态使用青绿色、警告使用橙色、错误使用红色。支援 100%、125%、150% 显示缩放，最低视窗尺寸 860 × 640。避免「一键加速百分比」「清空 RAM」或任何保证提升速度的文字。

## 禁止事项

- 不要执行安装程序。
- 不要在真实桌面执行整理。
- 不要新增强制结束程序、清除 Standby List、关闭 Pagefile 或 Windows 服务的功能。
- 不要重新加入桌面图示锁定或自动排版。
- 不要写死使用者名称、磁碟代号或安装路径。

## 验证

UI 完成后执行：

```powershell
dotnet build PerformanceGuardian.slnx -c Release
dotnet run --project tests/SmartDesktopGuardian.Tests/SmartDesktopGuardian.Tests.csproj -c Release
```

请将 UI 工作作为单独 Git commit 提交，并在讯息中列出修改的画面与资源。

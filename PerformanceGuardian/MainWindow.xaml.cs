using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using Forms = System.Windows.Forms;
using PerformanceGuardian.Models;
using PerformanceGuardian.Services;

namespace PerformanceGuardian;

public partial class MainWindow : INotifyPropertyChanged
{
    private readonly SystemMetricsService _metrics = new();
    private readonly ProcessSafetyService _safety = new();
    private readonly ActivityLog _log = new();
    private readonly SettingsService _settingsService = new();
    private readonly OrganizerSettings _organizerSettings;
    private readonly OrganizerService _organizer;
    private readonly DocumentationService _documentation;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly CancellationTokenSource _applicationCancellation = new();
    private CancellationTokenSource? _monitorCancellation;
    private bool _allowClose;
    private bool _isBusy;
    private bool _isOrganizing;
    private int _pressureSamples;
    private DateTimeOffset _lastIntervention = DateTimeOffset.MinValue;
    private double _cpuPercent, _memoryPercent, _diskPercent;
    private ulong _availableMemoryMb;
    private string _diskProfile = "256 GB SSD 最佳化";
    private string _statusText = "正在建立效能基准…";
    private string _foregroundText = "前景程式：侦测中";
    private string _organizerStatus = "尚未执行整理。";

    public MainWindow()
    {
        _organizerSettings = _settingsService.Load();
        _organizer = new OrganizerService(_organizerSettings, _settingsService, _log);
        _documentation = new DocumentationService(_settingsService, _organizer);
        InitializeComponent();
        DataContext = this;
        _trayIcon = CreateTrayIcon();
        Loaded += async (_, _) =>
        {
            await RefreshLoopOnceAsync();
            _ = AutoOrganizerLoopAsync(_applicationCancellation.Token);
        };
        Closing += OnClosing;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string DesktopPath => _organizer.DesktopPath;
    public string OrganizedPath => _organizer.OrganizedPath;
    public string PathSummary => $"程序：{AppContext.BaseDirectory}\n桌面：{DesktopPath}\n整理资料夹：{OrganizedPath}\n设定与纪录：{_settingsService.DataDirectory}";
    public bool AutoOrganizeEnabled
    {
        get => _organizerSettings.AutoOrganizeEnabled;
        set
        {
            if (_organizerSettings.AutoOrganizeEnabled == value) return;
            _organizerSettings.AutoOrganizeEnabled = value;
            _settingsService.Save(_organizerSettings);
            Notify(); Notify(nameof(OrganizerSummary));
        }
    }
    public string OrganizerSummary => AutoOrganizeEnabled
        ? $"已开启，每 {_organizerSettings.ScanIntervalMinutes} 分钟检查；依 yyyy年MM月 归档。"
        : "自动整理已暂停，可随时手动执行。";
    public string OrganizerStatus { get => _organizerStatus; private set => Set(ref _organizerStatus, value); }
    public bool CanOrganize => !_isOrganizing;
    public double CpuPercent { get => _cpuPercent; private set => Set(ref _cpuPercent, value, nameof(CpuDisplay)); }
    public double MemoryPercent { get => _memoryPercent; private set => Set(ref _memoryPercent, value, nameof(MemoryDisplay)); }
    public double DiskPercent { get => _diskPercent; private set => Set(ref _diskPercent, value, nameof(DiskDisplay)); }
    public ulong AvailableMemoryMb { get => _availableMemoryMb; private set => Set(ref _availableMemoryMb, value, nameof(AvailableMemoryDisplay)); }
    public string DiskProfile { get => _diskProfile; private set => Set(ref _diskProfile, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string ForegroundText { get => _foregroundText; private set => Set(ref _foregroundText, value); }
    public string CpuDisplay => $"{CpuPercent:0}%";
    public string MemoryDisplay => $"{MemoryPercent:0}%";
    public string DiskDisplay => $"{DiskPercent:0}%";
    public string AvailableMemoryDisplay => $"可用 {AvailableMemoryMb:N0} MB";
    public bool IsMonitoring => _monitorCancellation is not null;
    public string MonitorButtonText => IsMonitoring ? "停止背景监控" : "开始背景监控";
    public bool CanRescue => !_isBusy;

    private async void OrganizeNowButton_Click(object sender, RoutedEventArgs e) => await RunOrganizerAsync(true);
    private async void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isOrganizing) return;
        SetOrganizerBusy(true);
        try
        {
            var result = await _organizer.UndoLastAsync(CancellationToken.None);
            OrganizerStatus = result.Moved == 0 ? result.Messages.FirstOrDefault() ?? "没有可复原项目。" : $"已复原 {result.Moved} 个文件。\n{string.Join("\n", result.Messages.Take(5))}";
        }
        catch (Exception ex) { OrganizerStatus = $"复原失败：{ex.Message}"; await _log.WriteAsync("错误", ex.ToString()); }
        finally { SetOrganizerBusy(false); }
    }

    private async Task RunOrganizerAsync(bool ignoreWaitingPeriod)
    {
        if (_isOrganizing) return;
        SetOrganizerBusy(true);
        OrganizerStatus = "正在检查桌面文件…";
        try
        {
            var result = await _organizer.OrganizeAsync(ignoreWaitingPeriod, CancellationToken.None);
            OrganizerStatus = $"整理完成：移动 {result.Moved} 个，略过 {result.Skipped} 个。" +
                              (result.Messages.Count > 0 ? $"\n{string.Join("\n", result.Messages.Take(6))}" : "");
        }
        catch (Exception ex) { OrganizerStatus = $"整理失败：{ex.Message}"; await _log.WriteAsync("错误", ex.ToString()); }
        finally { SetOrganizerBusy(false); }
    }

    private async Task AutoOrganizerLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _organizerSettings.ScanIntervalMinutes)), cancellationToken);
                if (AutoOrganizeEnabled) await Dispatcher.InvokeAsync(async () => await RunOrganizerAsync(false));
            }
        }
        catch (OperationCanceledException) { }
    }

    private void SetOrganizerBusy(bool value) { _isOrganizing = value; Notify(nameof(CanOrganize)); }

    private void OpenOrganizedFolder_Click(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(OrganizedPath);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{OrganizedPath}\"") { UseShellExecute = true });
    }

    private void GenerateManualButton_Click(object sender, RoutedEventArgs e)
    {
        try { var path = _documentation.Generate(); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { OrganizerStatus = $"说明书产生失败：{ex.Message}"; }
    }

    private async void RescueButton_Click(object sender, RoutedEventArgs e) => await RunRescueAsync(false);
    private async Task RunRescueAsync(bool automatic)
    {
        if (_isBusy) return;
        _isBusy = true; Notify(nameof(CanRescue));
        StatusText = automatic ? "侦测到持续资源压力，正在安全调度…" : "正在诊断资源瓶颈并执行安全调度…";
        try
        {
            var report = await new RescueService(_metrics, _safety, _log).RunAsync(CancellationToken.None);
            ApplySnapshot(report.After);
            StatusText = FormatReport(report);
            _lastIntervention = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) { StatusText = $"急救未完成：{ex.Message}"; await _log.WriteAsync("错误", ex.ToString()); }
        finally { _isBusy = false; Notify(nameof(CanRescue)); }
    }

    private void MonitorButton_Click(object sender, RoutedEventArgs e) { if (IsMonitoring) StopMonitoring(); else StartMonitoring(); }
    private void StartMonitoring()
    {
        _monitorCancellation = new CancellationTokenSource(); NotifyMonitoring();
        StatusText = "背景监控已启动；每 5 秒低频取样，连续压力达到门槛才会介入。";
        _ = MonitorLoopAsync(_monitorCancellation.Token);
    }
    private void StopMonitoring()
    {
        _monitorCancellation?.Cancel(); _monitorCancellation?.Dispose(); _monitorCancellation = null; _pressureSamples = 0;
        var restored = _safety.RestoreAll();
        StatusText = restored.Count == 0 ? "背景监控已停止。" : $"背景监控已停止，并还原 {restored.Count} 个暂时设定。";
        NotifyMonitoring();
    }
    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var snapshot = _metrics.Capture(); await Dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
                _pressureSamples = snapshot.IsUnderPressure ? _pressureSamples + 1 : 0;
                if (_pressureSamples >= 3 && DateTimeOffset.UtcNow - _lastIntervention >= TimeSpan.FromMinutes(5))
                { _pressureSamples = 0; await Dispatcher.InvokeAsync(async () => await RunRescueAsync(true)); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await _log.WriteAsync("监控错误", ex.ToString()); await Dispatcher.InvokeAsync(() => StatusText = $"背景监控已暂停：{ex.Message}"); }
    }
    private async Task RefreshLoopOnceAsync() { ApplySnapshot(_metrics.Capture()); await Task.Delay(750); ApplySnapshot(_metrics.Capture()); }
    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        CpuPercent = snapshot.CpuPercent; MemoryPercent = snapshot.MemoryPercent; DiskPercent = snapshot.DiskBusyPercent;
        DiskProfile = snapshot.DiskProfile; AvailableMemoryMb = snapshot.AvailableMemoryMb;
        ForegroundText = $"前景程式：{snapshot.ForegroundProcess}　｜　{Environment.OSVersion.VersionString}";
        if (!_isBusy) StatusText = snapshot.PressureReason;
    }
    private static string FormatReport(RescueReport report)
    {
        var actions = report.Actions.Count == 0 ? "未变更设定" : string.Join("；", report.Actions);
        return $"{report.Summary}\n{actions}\nCPU {report.Before.CpuPercent:0}% → {report.After.CpuPercent:0}%　RAM {report.Before.MemoryPercent:0}% → {report.After.MemoryPercent:0}%　SSD {report.Before.DiskBusyPercent:0}% → {report.After.DiskBusyPercent:0}%";
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("开启智慧桌面管家", null, (_, _) => ShowFromTray()); menu.Items.Add("完全退出", null, (_, _) => ExitApplication());
        var icon = new Forms.NotifyIcon { Text = "Smart Desktop Guardian", Icon = System.Drawing.SystemIcons.Shield, Visible = true, ContextMenuStrip = menu };
        icon.DoubleClick += (_, _) => ShowFromTray(); return icon;
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (_allowClose) return; e.Cancel = true; Hide(); _trayIcon.ShowBalloonTip(1500, "Smart Desktop Guardian", "程式仍在系统匣执行。", Forms.ToolTipIcon.Info); }
    private void ShowFromTray() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void ExitApplication()
    {
        _applicationCancellation.Cancel(); StopMonitoring(); _allowClose = true; _trayIcon.Visible = false; _trayIcon.Dispose(); Close(); System.Windows.Application.Current.Shutdown();
    }
    private void NotifyMonitoring() { Notify(nameof(IsMonitoring)); Notify(nameof(MonitorButtonText)); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null, params string[] related)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; Notify(property); foreach (var name in related) Notify(name); }
    private void Notify([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}

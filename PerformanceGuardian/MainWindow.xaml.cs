using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using PerformanceGuardian.Models;
using PerformanceGuardian.Services;
using PerformanceGuardian.ViewModels;

namespace PerformanceGuardian;

public partial class MainWindow : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SystemMetricsService _metrics = new();
    private readonly ProcessSafetyService _safety = new();
    private readonly ActivityLog _log = new();
    private readonly SettingsService _settingsService = new();
    private readonly OrganizerSettings _organizerSettings;
    private readonly OrganizerService _organizer;
    private readonly DocumentationService _documentation;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly CancellationTokenSource _applicationCancellation = new();
    private readonly UiPreferences _uiPrefs;

    private CancellationTokenSource? _monitorCancellation;
    private bool _allowClose;
    private bool _isBusy;
    private bool _isOrganizing;
    private bool _hasMetricsSample;
    private bool _hasOrganizerError;
    private bool _hasPerformanceError;
    private int _pressureSamples;
    private DateTimeOffset _lastIntervention = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastScanTime;
    private double _cpuPercent, _memoryPercent, _diskPercent;
    private double _diskFreePercent = 100;
    private ulong _availableMemoryMb;
    private string _diskProfile = "256 GB SSD 最佳化";
    private string _statusText = "正在建立效能基准…";
    private string _foregroundText = "前景程式：侦测中";
    private string _foregroundProcessName = "侦测中";
    private string _organizerStatus = "尚未执行整理。";

    // Navigation & interactive state fields
    private string _selectedPage = "Home";
    private string _selectedHomeStatePreview = "Auto";
    private string _organizerInteractiveStateBadge = "尚无资料状态";
    private string _organizerInteractiveStateTone = "Neutral";
    private string _organizerInteractiveStateDetail = "待命就绪：点击「立即整理」或开启自动整理以扫描桌面文书档案。";
    private int _undoableBatchCount;
    private int _undoableFileCount;
    private int _todayOrganizedCount;

    // Rescue comparison fields
    private bool _hasRescueReport;
    private string _rescueCpuComparison = "尚未执行效能急救（点击上方「立即效能急救」产生介入前后对比）";
    private string _rescueRamComparison = "尚未执行效能急救";
    private string _rescueSsdComparison = "尚未执行效能急救";
    private string _rescueActionsSummary = "尚无介入纪录；本工具仅在资源竞争时暂时调整非系统背景程式优先权。";
    private string _rescueImprovementBadge = "等待诊断";
    private string _rescueImprovementTone = "Neutral";
    private string _rescueNoImprovementReason = "若介入后未观察到明显下降，通常代表负载来自您正在操作的前景应用程式或 Windows 核心磁碟作业；为确保工作资料安全，本程式绝不强制关闭应用程式或清空记忆体。";

    // Activity log fields
    private string _selectedActivityFilter = "全部";
    private string _activitySearchText = string.Empty;
    private readonly List<ActivityLogItemViewModel> _allActivityItems = [];

    public MainWindow()
    {
        _organizerSettings = _settingsService.Load();
        _uiPrefs = LoadUiPreferences();
        _organizer = new OrganizerService(_organizerSettings, _settingsService, _log);
        _documentation = new DocumentationService(_settingsService, _organizer);

        InitializeFormatChips();
        InitializeComponent();
        DataContext = this;
        _trayIcon = CreateTrayIcon();

        ApplyThemeResources(_uiPrefs.ThemeMode);

        if (_uiPrefs.StartMinimizedPreference)
        {
            WindowState = WindowState.Minimized;
        }

        Loaded += async (_, _) =>
        {
            await LoadOrganizerHistoryAndLogsAsync();
            await RefreshLoopOnceAsync();
            _ = AutoOrganizerLoopAsync(_applicationCancellation.Token);
        };
        Closing += OnClosing;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // =========================================================
    // Preserved Core Properties & Bindings
    // =========================================================
    public string DesktopPath => _organizer.DesktopPath;
    public string OrganizedPath => _organizer.OrganizedPath;
    public string InstallPathDisplay => AppContext.BaseDirectory;
    public string SettingsFilePathDisplay => _settingsService.SettingsPath;
    public string LogFilePathDisplay => _log.LogPath;
    public string DataDirectoryDisplay => _settingsService.DataDirectory;

    public string PathSummary =>
        $"程序：{AppContext.BaseDirectory}\n桌面：{DesktopPath}\n整理资料夹：{OrganizedPath}\n设定档：{SettingsFilePathDisplay}\n日志路径：{LogFilePathDisplay}";

    public bool AutoOrganizeEnabled
    {
        get => _organizerSettings.AutoOrganizeEnabled;
        set
        {
            if (_organizerSettings.AutoOrganizeEnabled == value) return;
            _organizerSettings.AutoOrganizeEnabled = value;
            _settingsService.Save(_organizerSettings);
            Notify();
            Notify(nameof(OrganizerSummary));
            Notify(nameof(OrganizerAutoStateBadge));
            Notify(nameof(OrganizerAutoStateTone));
            NotifyHomeStates();
        }
    }

    public string OrganizerSummary => AutoOrganizeEnabled
        ? $"已开启，每 {_organizerSettings.ScanIntervalMinutes} 分钟检查；新文件等待 {_organizerSettings.NewFileWaitMinutes} 分钟后依 yyyy年MM月 归档。"
        : "自动整理已暂停，可随时手动执行立即整理。";

    public string OrganizerStatus
    {
        get => _organizerStatus;
        private set => Set(ref _organizerStatus, value);
    }

    public bool CanOrganize => !_isOrganizing;

    public double CpuPercent
    {
        get => _cpuPercent;
        private set => Set(ref _cpuPercent, value, nameof(CpuDisplay), nameof(CpuStateLabel), nameof(CpuStateTone));
    }

    public double MemoryPercent
    {
        get => _memoryPercent;
        private set => Set(ref _memoryPercent, value, nameof(MemoryDisplay), nameof(MemoryStateLabel), nameof(MemoryStateTone));
    }

    public double DiskPercent
    {
        get => _diskPercent;
        private set => Set(ref _diskPercent, value, nameof(DiskDisplay), nameof(DiskStateLabel), nameof(DiskStateTone));
    }

    public double DiskFreePercent
    {
        get => _diskFreePercent;
        private set => Set(ref _diskFreePercent, value, nameof(DiskFreeDisplay), nameof(DiskStateLabel), nameof(DiskStateTone));
    }

    public ulong AvailableMemoryMb
    {
        get => _availableMemoryMb;
        private set => Set(ref _availableMemoryMb, value, nameof(AvailableMemoryDisplay));
    }

    public string DiskProfile
    {
        get => _diskProfile;
        private set => Set(ref _diskProfile, value, nameof(DiskFreeDisplay));
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string ForegroundText
    {
        get => _foregroundText;
        private set => Set(ref _foregroundText, value);
    }

    public string ForegroundProcessName
    {
        get => _foregroundProcessName;
        private set => Set(ref _foregroundProcessName, value);
    }

    public string CpuDisplay => $"{CpuPercent:0}%";
    public string MemoryDisplay => $"{MemoryPercent:0}%";
    public string DiskDisplay => $"{DiskPercent:0}%";
    public string AvailableMemoryDisplay => $"可用 {AvailableMemoryMb:N0} MB";
    public string DiskFreeDisplay => $"可用空间 {DiskFreePercent:0}%（{DiskProfile}）";

    public bool IsMonitoring => _monitorCancellation is not null;

    public bool BackgroundMonitoringEnabled
    {
        get => IsMonitoring;
        set
        {
            if (IsMonitoring == value) return;
            if (value) StartMonitoring();
            else StopMonitoring();
        }
    }

    public string MonitorButtonText => IsMonitoring ? "停止背景监控" : "开始背景监控";
    public string MonitorBadgeText => IsMonitoring ? "背景智慧监控运作中" : "背景监控未启动";
    public string MonitorBadgeTone => IsMonitoring ? "Normal" : "Neutral";
    public bool CanRescue => !_isBusy;

    // =========================================================
    // Navigation & Page Selection
    // =========================================================
    public string SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (Set(ref _selectedPage, value))
            {
                if (value == "Activity")
                {
                    _ = ReloadActivityLogOnlyAsync();
                }
            }
        }
    }

    // =========================================================
    // Home Page Properties & 6 Required States
    // =========================================================
    public string OrganizerAutoStateBadge => AutoOrganizeEnabled ? "自动整理已开启" : "自动整理已暂停";
    public string OrganizerAutoStateTone => AutoOrganizeEnabled ? "Normal" : "Warning";

    public string LastScanTimeDisplay => _lastScanTime.HasValue
        ? _lastScanTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : "尚无扫描纪录";

    public string TodayOrganizedCountDisplay => $"{_todayOrganizedCount} 个文件";
    public string OrganizedFolderNameDisplay => _organizerSettings.OrganizedFolderName;

    public int[] ScanIntervalOptions { get; } = [5, 10, 15, 30, 60];
    public int[] NewFileWaitOptions { get; } = [15, 30, 60, 120];
    public int[] MonitorIntervalOptions { get; } = [3, 5, 10, 15];
    public int[] PressureTriggerOptions { get; } = [2, 3, 4, 5];
    public int[] CooldownMinuteOptions { get; } = [3, 5, 10, 15];
    public int[] SsdThresholdOptions { get; } = [10, 15, 20, 25];
    public string[] ThemeOptions { get; } = ["Light", "Dark"];
    public string[] LanguageOptions { get; } = ["繁體中文", "简体中文"];

    public string SelectedHomeStatePreview
    {
        get => _selectedHomeStatePreview;
        set
        {
            if (Set(ref _selectedHomeStatePreview, value))
            {
                NotifyHomeStates();
            }
        }
    }

    public string OverallSystemStateBadge
    {
        get
        {
            if (_selectedHomeStatePreview != "Auto") return _selectedHomeStatePreview;
            if (_isBusy || _isOrganizing) return "正在处理状态";
            if (_hasPerformanceError || _hasOrganizerError) return "错误状态";
            if (!_hasMetricsSample) return "尚无资料状态";
            if (CpuPercent >= 90 || MemoryPercent >= 85 || DiskPercent >= 90) return "高负载状态";
            if (CpuPercent >= 75 || MemoryPercent >= 75 || DiskPercent >= 75 || DiskFreePercent < _uiPrefs.SsdWarningThresholdPercent || !AutoOrganizeEnabled)
                return "注意状态";
            return "正常状态";
        }
    }

    public string OverallSystemStateTone => OverallSystemStateBadge switch
    {
        "正常状态" => "Normal",
        "注意状态" => "Warning",
        "高负载状态" => "Error",
        "正在处理状态" => "Processing",
        "错误状态" => "Error",
        _ => "Neutral"
    };

    public string OverallSystemStateDescription => OverallSystemStateBadge switch
    {
        "正常状态" => "系统资源充裕，CPU、RAM 与 SSD I/O 皆在健康范围，文件整理待命正常。",
        "注意状态" => !AutoOrganizeEnabled
            ? "自动整理目前已暂停；或部分资源接近注意门槛，建议留意可用空间与背景程式。"
            : "部分指标偏高或 SSD 剩余空间接近警告门槛，建议保持背景智慧监控开启。",
        "高负载状态" => "侦测到 CPU、RAM 或 SSD 处于高负载压力，建议点击「立即效能急救」执行安全优先权调度。",
        "正在处理状态" => "正在执行桌面文件整理或安全效能调度作业，请稍候…",
        "错误状态" => "最近一次操作发生异常，请查看「活动纪录」了解详细原因。",
        _ => "正在初始化感测器与桌面整理状态，尚未产生历史纪录。"
    };

    public string HomeOrganizerCardStateBadge
    {
        get
        {
            if (_selectedHomeStatePreview != "Auto") return _selectedHomeStatePreview;
            if (_isOrganizing) return "正在处理状态";
            if (_hasOrganizerError) return "错误状态";
            if (!AutoOrganizeEnabled) return "注意状态";
            if (_lastScanTime is null && RecentOrganizedItems.Count == 0) return "尚无资料状态";
            return "正常状态";
        }
    }

    public string HomeOrganizerCardStateTone => HomeOrganizerCardStateBadge switch
    {
        "正常状态" => "Normal",
        "注意状态" => "Warning",
        "高负载状态" => "Error",
        "正在处理状态" => "Processing",
        "错误状态" => "Error",
        _ => "Neutral"
    };

    // =========================================================
    // Document Organizer Page Properties & 10 Interactive States
    // =========================================================
    public ObservableCollection<RecentOrganizedItemViewModel> RecentOrganizedItems { get; } = [];
    public ObservableCollection<FormatChipViewModel> FormatChips { get; } = [];
    public ObservableCollection<string> RescueActionItems { get; } = [];
    public ObservableCollection<ActivityLogItemViewModel> FilteredActivityItems { get; } = [];

    public bool HasRecentOrganizedItems => RecentOrganizedItems.Count > 0;
    public bool CanUndo => !_isOrganizing && _undoableBatchCount > 0;

    public string UndoStatusBadge => _undoableBatchCount > 0
        ? $"可以复原（最近一批 {_undoableFileCount} 个文件，共保存 {_undoableBatchCount} 批纪录）"
        : "没有可复原纪录";

    public string UndoStatusTone => _undoableBatchCount > 0 ? "Normal" : "Neutral";

    public string OrganizerInteractiveStateBadge
    {
        get => _organizerInteractiveStateBadge;
        private set => Set(ref _organizerInteractiveStateBadge, value);
    }

    public string OrganizerInteractiveStateTone
    {
        get => _organizerInteractiveStateTone;
        private set => Set(ref _organizerInteractiveStateTone, value);
    }

    public string OrganizerInteractiveStateDetail
    {
        get => _organizerInteractiveStateDetail;
        private set => Set(ref _organizerInteractiveStateDetail, value);
    }

    public int ScanIntervalMinutes
    {
        get => _organizerSettings.ScanIntervalMinutes;
        set
        {
            var clamped = Math.Clamp(value, 1, 240);
            if (_organizerSettings.ScanIntervalMinutes == clamped) return;
            _organizerSettings.ScanIntervalMinutes = clamped;
            _settingsService.Save(_organizerSettings);
            Notify();
            Notify(nameof(ScanIntervalDisplay));
            Notify(nameof(OrganizerSummary));
        }
    }

    public int NewFileWaitMinutes
    {
        get => _organizerSettings.NewFileWaitMinutes;
        set
        {
            var clamped = Math.Clamp(value, 0, 1440);
            if (_organizerSettings.NewFileWaitMinutes == clamped) return;
            _organizerSettings.NewFileWaitMinutes = clamped;
            _settingsService.Save(_organizerSettings);
            Notify();
            Notify(nameof(NewFileWaitDisplay));
            Notify(nameof(OrganizerSummary));
        }
    }

    public string OrganizedFolderName
    {
        get => _organizerSettings.OrganizedFolderName;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || _organizerSettings.OrganizedFolderName == value.Trim()) return;
            _organizerSettings.OrganizedFolderName = value.Trim();
            _settingsService.Save(_organizerSettings);
            Notify();
            Notify(nameof(OrganizedPath));
            Notify(nameof(OrganizedFolderNameDisplay));
            Notify(nameof(PathSummary));
        }
    }

    public string ScanIntervalDisplay => $"每 {ScanIntervalMinutes} 分钟";
    public string NewFileWaitDisplay => $"{NewFileWaitMinutes} 分钟";
    public string SupportedFormatsSummary => "PDF、DOC、DOCX、XLS、XLSX、CSV、PPT、PPTX、TXT、RTF、MD、ODT、ODS、ODP、HWP";

    // =========================================================
    // Performance Page Properties & Before/After Comparison
    // =========================================================
    public string CpuStateLabel => CpuPercent >= 90 ? "高负载状态" : CpuPercent >= 75 ? "注意状态" : "正常状态";
    public string CpuStateTone => CpuPercent >= 90 ? "Error" : CpuPercent >= 75 ? "Warning" : "Normal";

    public string MemoryStateLabel => MemoryPercent >= 85 ? "高负载状态" : MemoryPercent >= 75 ? "注意状态" : "正常状态";
    public string MemoryStateTone => MemoryPercent >= 85 ? "Error" : MemoryPercent >= 75 ? "Warning" : "Normal";

    public string DiskStateLabel => DiskPercent >= 90 ? "高负载状态" : (DiskPercent >= 75 || DiskFreePercent < _uiPrefs.SsdWarningThresholdPercent) ? "注意状态" : "正常状态";
    public string DiskStateTone => DiskPercent >= 90 ? "Error" : (DiskPercent >= 75 || DiskFreePercent < _uiPrefs.SsdWarningThresholdPercent) ? "Warning" : "Normal";

    public string LastInterventionDisplay => _lastIntervention == DateTimeOffset.MinValue
        ? "尚无介入纪录"
        : _lastIntervention.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string CooldownDisplay
    {
        get
        {
            if (_lastIntervention == DateTimeOffset.MinValue)
                return $"已就绪（冷却时间设定 {_uiPrefs.CooldownMinutes} 分钟）";
            var elapsed = DateTimeOffset.UtcNow - _lastIntervention;
            var cooldown = TimeSpan.FromMinutes(_uiPrefs.CooldownMinutes);
            if (elapsed >= cooldown)
                return $"已就绪（距上次介入已逾 {_uiPrefs.CooldownMinutes} 分钟）";
            var remaining = cooldown - elapsed;
            return $"冷却保护中（剩余 {remaining.Minutes} 分 {remaining.Seconds:00} 秒）";
        }
    }

    public string PressureSampleDisplay => $"连续压力计数：{_pressureSamples} / {_uiPrefs.PressureTriggerCount} 次";

    public bool HasRescueReport
    {
        get => _hasRescueReport;
        private set => Set(ref _hasRescueReport, value);
    }

    public string RescueCpuComparison
    {
        get => _rescueCpuComparison;
        private set => Set(ref _rescueCpuComparison, value);
    }

    public string RescueRamComparison
    {
        get => _rescueRamComparison;
        private set => Set(ref _rescueRamComparison, value);
    }

    public string RescueSsdComparison
    {
        get => _rescueSsdComparison;
        private set => Set(ref _rescueSsdComparison, value);
    }

    public string RescueActionsSummary
    {
        get => _rescueActionsSummary;
        private set => Set(ref _rescueActionsSummary, value);
    }

    public string RescueImprovementBadge
    {
        get => _rescueImprovementBadge;
        private set => Set(ref _rescueImprovementBadge, value);
    }

    public string RescueImprovementTone
    {
        get => _rescueImprovementTone;
        private set => Set(ref _rescueImprovementTone, value);
    }

    public string RescueNoImprovementReason
    {
        get => _rescueNoImprovementReason;
        private set => Set(ref _rescueNoImprovementReason, value);
    }

    // =========================================================
    // Activity Log Page Properties
    // =========================================================
    public string SelectedActivityFilter
    {
        get => _selectedActivityFilter;
        set
        {
            if (Set(ref _selectedActivityFilter, value))
            {
                ApplyActivityLogFilter();
            }
        }
    }

    public string ActivitySearchText
    {
        get => _activitySearchText;
        set
        {
            if (Set(ref _activitySearchText, value))
            {
                ApplyActivityLogFilter();
            }
        }
    }

    public bool HasFilteredActivityItems => FilteredActivityItems.Count > 0;
    public string ActivityCountSummary => $"共显示 {FilteredActivityItems.Count} 笔纪录（总计 {_allActivityItems.Count} 笔）";
    public string PrivacyNoticeText => "“活动纪录只保存档案名称、路径、操作结果及效能指标，不读取或上传文件内容。”";

    // =========================================================
    // Settings Page Properties
    // =========================================================
    public int MonitorIntervalSeconds
    {
        get => _uiPrefs.MonitorIntervalSeconds;
        set
        {
            if (_uiPrefs.MonitorIntervalSeconds == value) return;
            _uiPrefs.MonitorIntervalSeconds = Math.Clamp(value, 2, 60);
            SaveUiPreferences();
            Notify();
        }
    }

    public int PressureTriggerCount
    {
        get => _uiPrefs.PressureTriggerCount;
        set
        {
            if (_uiPrefs.PressureTriggerCount == value) return;
            _uiPrefs.PressureTriggerCount = Math.Clamp(value, 1, 10);
            SaveUiPreferences();
            Notify();
            Notify(nameof(PressureSampleDisplay));
        }
    }

    public int CooldownMinutes
    {
        get => _uiPrefs.CooldownMinutes;
        set
        {
            if (_uiPrefs.CooldownMinutes == value) return;
            _uiPrefs.CooldownMinutes = Math.Clamp(value, 1, 60);
            SaveUiPreferences();
            Notify();
            Notify(nameof(CooldownDisplay));
        }
    }

    public bool AutoRestoreOnStop
    {
        get => _uiPrefs.AutoRestoreOnStop;
        set
        {
            if (_uiPrefs.AutoRestoreOnStop == value) return;
            _uiPrefs.AutoRestoreOnStop = value;
            SaveUiPreferences();
            Notify();
        }
    }

    public int SsdWarningThresholdPercent
    {
        get => _uiPrefs.SsdWarningThresholdPercent;
        set
        {
            if (_uiPrefs.SsdWarningThresholdPercent == value) return;
            _uiPrefs.SsdWarningThresholdPercent = Math.Clamp(value, 5, 50);
            SaveUiPreferences();
            Notify();
            Notify(nameof(DiskStateLabel));
            Notify(nameof(DiskStateTone));
            NotifyHomeStates();
        }
    }

    public bool LaunchAtLoginPreference
    {
        get => _uiPrefs.LaunchAtLoginPreference;
        set
        {
            if (_uiPrefs.LaunchAtLoginPreference == value) return;
            _uiPrefs.LaunchAtLoginPreference = value;
            SaveUiPreferences();
            Notify();
        }
    }

    public bool StartMinimizedPreference
    {
        get => _uiPrefs.StartMinimizedPreference;
        set
        {
            if (_uiPrefs.StartMinimizedPreference == value) return;
            _uiPrefs.StartMinimizedPreference = value;
            SaveUiPreferences();
            Notify();
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _uiPrefs.MinimizeToTrayOnClose;
        set
        {
            if (_uiPrefs.MinimizeToTrayOnClose == value) return;
            _uiPrefs.MinimizeToTrayOnClose = value;
            SaveUiPreferences();
            Notify();
        }
    }

    public bool DuplicateCheckEnabled
    {
        get => _uiPrefs.DuplicateCheckEnabled;
        set
        {
            if (_uiPrefs.DuplicateCheckEnabled == value) return;
            _uiPrefs.DuplicateCheckEnabled = value;
            SaveUiPreferences();
            Notify();
        }
    }

    public string SelectedThemeMode
    {
        get => _uiPrefs.ThemeMode;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || _uiPrefs.ThemeMode == value) return;
            _uiPrefs.ThemeMode = value;
            SaveUiPreferences();
            ApplyThemeResources(value);
            Notify();
        }
    }

    public string SelectedLanguageMode
    {
        get => _uiPrefs.LanguageMode;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || _uiPrefs.LanguageMode == value) return;
            _uiPrefs.LanguageMode = value;
            SaveUiPreferences();
            Notify();
        }
    }

    public string AppVersionDisplay => "Smart Desktop Guardian v0.2.0｜Windows 10/11 (x64)";

    // =========================================================
    // Event Handlers (Preserving All Required Signatures)
    // =========================================================
    private async void OrganizeNowButton_Click(object sender, RoutedEventArgs e) => await RunOrganizerAsync(true);

    private async void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isOrganizing) return;
        SetOrganizerBusy(true);
        OrganizerInteractiveStateBadge = "正在搬移";
        OrganizerInteractiveStateTone = "Processing";
        OrganizerInteractiveStateDetail = "正在将最近一批整理的文件安全复原回桌面原始位置…";
        try
        {
            var result = await _organizer.UndoLastAsync(CancellationToken.None);
            _hasOrganizerError = false;
            if (result.Moved == 0)
            {
                OrganizerStatus = result.Messages.FirstOrDefault() ?? "没有可复原项目。";
                OrganizerInteractiveStateBadge = "没有可复原纪录";
                OrganizerInteractiveStateTone = "Neutral";
                OrganizerInteractiveStateDetail = OrganizerStatus;
            }
            else
            {
                OrganizerStatus = $"已复原 {result.Moved} 个文件。\n{string.Join("\n", result.Messages.Take(5))}";
                OrganizerInteractiveStateBadge = "整理完成";
                OrganizerInteractiveStateTone = "Normal";
                OrganizerInteractiveStateDetail = $"成功复原 {result.Moved} 个文件回到桌面。";
            }
            await LoadOrganizerHistoryAndLogsAsync();
        }
        catch (Exception ex)
        {
            _hasOrganizerError = true;
            OrganizerStatus = $"复原失败：{ex.Message}";
            OrganizerInteractiveStateBadge = "整理失败";
            OrganizerInteractiveStateTone = "Error";
            OrganizerInteractiveStateDetail = OrganizerStatus;
            await _log.WriteAsync("错误", ex.ToString());
            await ReloadActivityLogOnlyAsync();
        }
        finally
        {
            SetOrganizerBusy(false);
            NotifyHomeStates();
        }
    }

    private async Task RunOrganizerAsync(bool ignoreWaitingPeriod)
    {
        if (_isOrganizing) return;
        SetOrganizerBusy(true);
        _hasOrganizerError = false;
        OrganizerStatus = "正在检查桌面文件…";
        OrganizerInteractiveStateBadge = "正在扫描";
        OrganizerInteractiveStateTone = "Processing";
        OrganizerInteractiveStateDetail = "正在扫描桌面第一层文书档案，并检查写入与使用状态…";
        NotifyHomeStates();

        try
        {
            await Task.Delay(120);
            OrganizerInteractiveStateBadge = "正在搬移";
            OrganizerInteractiveStateDetail = "正在依文件最后修改月份归档，并进行 SHA-256 重复档比对…";

            var result = await _organizer.OrganizeAsync(ignoreWaitingPeriod, CancellationToken.None);
            _lastScanTime = DateTimeOffset.Now;
            Notify(nameof(LastScanTimeDisplay));

            OrganizerStatus = $"整理完成：移动 {result.Moved} 个，略过 {result.Skipped} 个。" +
                              (result.Messages.Count > 0 ? $"\n{string.Join("\n", result.Messages.Take(6))}" : "");

            await LoadOrganizerHistoryAndLogsAsync(result);
            UpdateOrganizerInteractiveStateFromResult(result);
        }
        catch (Exception ex)
        {
            _hasOrganizerError = true;
            OrganizerStatus = $"整理失败：{ex.Message}";
            OrganizerInteractiveStateBadge = "整理失败";
            OrganizerInteractiveStateTone = "Error";
            OrganizerInteractiveStateDetail = $"整理过程中发生错误：{ex.Message}";
            await _log.WriteAsync("错误", ex.ToString());
            await ReloadActivityLogOnlyAsync();
        }
        finally
        {
            SetOrganizerBusy(false);
            NotifyHomeStates();
        }
    }

    private void UpdateOrganizerInteractiveStateFromResult(OrganizerResult result)
    {
        var hasInUseSkip = result.Messages.Any(m => m.Contains("使用中", StringComparison.OrdinalIgnoreCase));
        if (result.Moved == 0 && result.Skipped == 0)
        {
            OrganizerInteractiveStateBadge = "没有可整理文件";
            OrganizerInteractiveStateTone = "Neutral";
            OrganizerInteractiveStateDetail = "目前桌面第一层没有符合条件的待整理文书档案。";
            return;
        }

        if (result.Moved == 0 && hasInUseSkip)
        {
            OrganizerInteractiveStateBadge = "文件正在使用，因此略过";
            OrganizerInteractiveStateTone = "Warning";
            OrganizerInteractiveStateDetail = $"侦测到 {result.Skipped} 个文件正在开启或编辑中，已安全略过不移动。";
            return;
        }

        var topItem = RecentOrganizedItems.FirstOrDefault();
        if (topItem is not null && topItem.StatusLabel.Contains("重复", StringComparison.OrdinalIgnoreCase))
        {
            OrganizerInteractiveStateBadge = "相同内容移入重复档";
            OrganizerInteractiveStateTone = "Warning";
            OrganizerInteractiveStateDetail = $"已整理 {result.Moved} 个文件；侦测到相同 SHA-256 内容，已安全移入「重複檔 Duplicates」。";
        }
        else if (topItem is not null && topItem.StatusLabel.Contains("重新命名", StringComparison.OrdinalIgnoreCase))
        {
            OrganizerInteractiveStateBadge = "同名文件已重新命名";
            OrganizerInteractiveStateTone = "Normal";
            OrganizerInteractiveStateDetail = $"已整理 {result.Moved} 个文件；同名但内容不同的文件已自动加上序号保留。";
        }
        else if (hasInUseSkip)
        {
            OrganizerInteractiveStateBadge = "文件正在使用，因此略过";
            OrganizerInteractiveStateTone = "Warning";
            OrganizerInteractiveStateDetail = $"已移动 {result.Moved} 个文件，另有 {result.Skipped} 个使用中或等待期文件已安全略过。";
        }
        else
        {
            OrganizerInteractiveStateBadge = "整理完成";
            OrganizerInteractiveStateTone = "Normal";
            OrganizerInteractiveStateDetail = $"成功归档 {result.Moved} 个文件（略过 {result.Skipped} 个），可随时一键复原。";
        }
    }

    private async Task AutoOrganizerLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _organizerSettings.ScanIntervalMinutes)), cancellationToken);
                if (AutoOrganizeEnabled)
                    await Dispatcher.InvokeAsync(async () => await RunOrganizerAsync(false));
            }
        }
        catch (OperationCanceledException) { }
    }

    private void SetOrganizerBusy(bool value)
    {
        _isOrganizing = value;
        Notify(nameof(CanOrganize));
        Notify(nameof(CanUndo));
        NotifyHomeStates();
    }

    private void OpenOrganizedFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(OrganizedPath);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{OrganizedPath}\"") { UseShellExecute = true });
    }

    private void OpenDesktopFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(DesktopPath))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DesktopPath}\"") { UseShellExecute = true });
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_settingsService.DataDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settingsService.DataDirectory}\"") { UseShellExecute = true });
    }

    private void GenerateManualButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _documentation.Generate();
            OrganizerStatus = $"已重新产生并开启说明书：{path}";
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            OrganizerStatus = $"说明书产生失败：{ex.Message}";
        }
    }

    private async void RescueButton_Click(object sender, RoutedEventArgs e) => await RunRescueAsync(false);

    private async Task RunRescueAsync(bool automatic)
    {
        if (_isBusy) return;
        _isBusy = true;
        _hasPerformanceError = false;
        Notify(nameof(CanRescue));
        NotifyHomeStates();

        StatusText = automatic
            ? "侦测到持续资源压力，正在执行安全优先权调度…"
            : "正在诊断 CPU / RAM / SSD 资源瓶颈并执行安全调度…";

        try
        {
            var report = await new RescueService(_metrics, _safety, _log).RunAsync(CancellationToken.None);
            ApplySnapshot(report.After);
            StatusText = FormatReport(report);
            _lastIntervention = DateTimeOffset.UtcNow;
            Notify(nameof(LastInterventionDisplay));
            Notify(nameof(CooldownDisplay));

            UpdateRescueComparison(report);
            await ReloadActivityLogOnlyAsync();
        }
        catch (Exception ex)
        {
            _hasPerformanceError = true;
            StatusText = $"急救未完成：{ex.Message}";
            await _log.WriteAsync("错误", ex.ToString());
            await ReloadActivityLogOnlyAsync();
        }
        finally
        {
            _isBusy = false;
            Notify(nameof(CanRescue));
            NotifyHomeStates();
        }
    }

    private void UpdateRescueComparison(RescueReport report)
    {
        HasRescueReport = true;
        var cpuDelta = report.After.CpuPercent - report.Before.CpuPercent;
        var ramDelta = report.After.MemoryPercent - report.Before.MemoryPercent;
        var ssdDelta = report.After.DiskBusyPercent - report.Before.DiskBusyPercent;

        RescueCpuComparison = $"{report.Before.CpuPercent:0}% → {report.After.CpuPercent:0}%（{(cpuDelta <= 0 ? $"变化 {cpuDelta:0}%" : $"+{cpuDelta:0}%")}）";
        RescueRamComparison = $"{report.Before.MemoryPercent:0}%（可用 {report.Before.AvailableMemoryMb:N0} MB） → {report.After.MemoryPercent:0}%（可用 {report.After.AvailableMemoryMb:N0} MB）";
        RescueSsdComparison = $"{report.Before.DiskBusyPercent:0}% → {report.After.DiskBusyPercent:0}%（{(ssdDelta <= 0 ? $"变化 {ssdDelta:0}%" : $"+{ssdDelta:0}%")}）";

        RescueActionItems.Clear();
        if (report.Actions.Count == 0)
        {
            RescueActionItems.Add("未变更任何程序设定（目前无符合安全降级门槛的高耗能背景程式）");
            RescueActionsSummary = "未变更设定：所有背景程式皆在安全范围内，或负载来自受保护的系统与前景程式。";
        }
        else
        {
            foreach (var act in report.Actions)
                RescueActionItems.Add(act);
            RescueActionsSummary = string.Join("；", report.Actions);
        }

        var improved = report.After.CpuPercent < report.Before.CpuPercent - 3 ||
                       report.After.DiskBusyPercent < report.Before.DiskBusyPercent - 5 ||
                       report.After.AvailableMemoryMb > report.Before.AvailableMemoryMb + 64;

        if (improved)
        {
            RescueImprovementBadge = "已观察到改善";
            RescueImprovementTone = "Normal";
            RescueNoImprovementReason = "资源竞争已缓解；所有优先权调整皆为暂时性，可随时点击「还原暂时设定」恢复默认值。";
        }
        else
        {
            RescueImprovementBadge = report.Actions.Count == 0 ? "无需介入（保持原状）" : "短时间内尚未观察到明显下降";
            RescueImprovementTone = "Warning";
            RescueNoImprovementReason = report.Actions.Count == 0
                ? "原因说明：未发现 CPU 累积逾 20 秒且记忆体逾 350 MB 的非前景背景程式，系统设定保持不变。"
                : "原因说明：目前资源使用主要来自您正在操作的前景应用程式或系统核心 I/O；为保障资料安全，本工具不强制关闭应用程式或清空系统快取。";
        }
    }

    private async void RestoreSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var restored = _safety.RestoreAll();
        if (restored.Count == 0)
        {
            StatusText = "目前已是原始排程优先权设定，无需还原。";
        }
        else
        {
            var msg = $"已还原 {restored.Count} 个程式的排程优先权：{string.Join("、", restored)}";
            StatusText = msg;
            await _log.WriteAsync("效能优化", msg);
            await ReloadActivityLogOnlyAsync();
        }
    }

    private void MonitorButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsMonitoring) StopMonitoring();
        else StartMonitoring();
    }

    private void StartMonitoring()
    {
        if (_monitorCancellation is not null) return;
        _monitorCancellation = new CancellationTokenSource();
        NotifyMonitoring();
        StatusText = $"背景智慧监控已启动；每 {_uiPrefs.MonitorIntervalSeconds} 秒低频取样，连续 {_uiPrefs.PressureTriggerCount} 次压力达标且逾 {_uiPrefs.CooldownMinutes} 分钟冷却期才介入。";
        _ = MonitorLoopAsync(_monitorCancellation.Token);
    }

    private void StopMonitoring()
    {
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
        _monitorCancellation = null;
        _pressureSamples = 0;
        Notify(nameof(PressureSampleDisplay));

        if (_uiPrefs.AutoRestoreOnStop)
        {
            var restored = _safety.RestoreAll();
            StatusText = restored.Count == 0
                ? "背景监控已停止。"
                : $"背景监控已停止，并自动还原 {restored.Count} 个暂时优先权设定。";
        }
        else
        {
            StatusText = "背景监控已停止（保留目前优先权设定）。";
        }
        NotifyMonitoring();
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(2, _uiPrefs.MonitorIntervalSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var snapshot = _metrics.Capture();
                await Dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
                _pressureSamples = snapshot.IsUnderPressure ? _pressureSamples + 1 : 0;
                await Dispatcher.InvokeAsync(() =>
                {
                    Notify(nameof(PressureSampleDisplay));
                    Notify(nameof(CooldownDisplay));
                });

                if (_pressureSamples >= _uiPrefs.PressureTriggerCount &&
                    DateTimeOffset.UtcNow - _lastIntervention >= TimeSpan.FromMinutes(_uiPrefs.CooldownMinutes))
                {
                    _pressureSamples = 0;
                    await Dispatcher.InvokeAsync(async () => await RunRescueAsync(true));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _hasPerformanceError = true;
            await _log.WriteAsync("监控错误", ex.ToString());
            await Dispatcher.InvokeAsync(() =>
            {
                StatusText = $"背景监控已暂停：{ex.Message}";
                NotifyHomeStates();
            });
        }
    }

    private async Task RefreshLoopOnceAsync()
    {
        ApplySnapshot(_metrics.Capture());
        await Task.Delay(750);
        ApplySnapshot(_metrics.Capture());
    }

    private void ApplySnapshot(SystemSnapshot snapshot)
    {
        _hasMetricsSample = true;
        CpuPercent = snapshot.CpuPercent;
        MemoryPercent = snapshot.MemoryPercent;
        DiskPercent = snapshot.DiskBusyPercent;
        DiskFreePercent = snapshot.DiskFreePercent;
        DiskProfile = snapshot.DiskProfile;
        AvailableMemoryMb = snapshot.AvailableMemoryMb;
        ForegroundProcessName = snapshot.ForegroundProcess;
        ForegroundText = $"前景程式：{snapshot.ForegroundProcess}　｜　{Environment.OSVersion.VersionString}";
        if (!_isBusy) StatusText = snapshot.PressureReason;
        NotifyHomeStates();
    }

    private static string FormatReport(RescueReport report)
    {
        var actions = report.Actions.Count == 0 ? "未变更设定" : string.Join("；", report.Actions);
        return $"{report.Summary}\n{actions}\nCPU {report.Before.CpuPercent:0}% → {report.After.CpuPercent:0}%　RAM {report.Before.MemoryPercent:0}% → {report.After.MemoryPercent:0}%　SSD {report.Before.DiskBusyPercent:0}% → {report.After.DiskBusyPercent:0}%";
    }

    // =========================================================
    // History & Activity Log Loading
    // =========================================================
    private async void RefreshActivityLogButton_Click(object sender, RoutedEventArgs e)
    {
        await ReloadActivityLogOnlyAsync();
    }

    private void ResetDefaultFormatsButton_Click(object sender, RoutedEventArgs e)
    {
        string[] defaults =
        [
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".csv", ".ppt", ".pptx",
            ".txt", ".rtf", ".md", ".odt", ".ods", ".odp", ".hwp"
        ];
        _organizerSettings.Extensions = defaults;
        _settingsService.Save(_organizerSettings);
        foreach (var chip in FormatChips)
            chip.IsEnabled = true;
    }

    private void InitializeFormatChips()
    {
        FormatChips.Clear();
        var enabledSet = _organizerSettings.Extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        (string Ext, string Group)[] allFormats =
        [
            (".pdf", "文書與簡報"), (".doc", "文書與簡報"), (".docx", "文書與簡報"),
            (".xls", "試算表與資料"), (".xlsx", "試算表與資料"), (".csv", "試算表與資料"),
            (".ppt", "文書與簡報"), (".pptx", "文書與簡報"),
            (".txt", "文字與筆記"), (".rtf", "文字與筆記"), (".md", "文字與筆記"),
            (".odt", "開放文件與其他"), (".ods", "開放文件與其他"), (".odp", "開放文件與其他"), (".hwp", "開放文件與其他")
        ];

        foreach (var (ext, group) in allFormats)
        {
            FormatChips.Add(new FormatChipViewModel(ext, group, enabledSet.Contains(ext), OnFormatChipsUpdated));
        }
    }

    private void OnFormatChipsUpdated()
    {
        var active = FormatChips.Where(c => c.IsEnabled).Select(c => c.Extension).ToArray();
        if (active.Length == 0) return;
        _organizerSettings.Extensions = active;
        _settingsService.Save(_organizerSettings);
    }

    private async Task LoadOrganizerHistoryAndLogsAsync(OrganizerResult? latestResult = null)
    {
        try
        {
            var historyPath = Path.Combine(_settingsService.DataDirectory, "organizer-history.json");
            List<OrganizerBatch> batches = [];
            if (File.Exists(historyPath))
            {
                await using var stream = File.OpenRead(historyPath);
                batches = await JsonSerializer.DeserializeAsync<List<OrganizerBatch>>(stream, JsonOptions) ?? [];
            }

            _undoableBatchCount = batches.Count;
            _undoableFileCount = batches.Count > 0 ? batches[^1].Moves.Count : 0;

            var today = DateTimeOffset.Now.Date;
            _todayOrganizedCount = batches
                .Where(b => b.Timestamp.ToLocalTime().Date == today)
                .Sum(b => b.Moves.Count);

            if (batches.Count > 0 && _lastScanTime is null)
                _lastScanTime = batches[^1].Timestamp;

            RecentOrganizedItems.Clear();

            // Add any skipped in-use items from the latest interactive run first so the user sees them immediately
            if (latestResult is not null)
            {
                foreach (var msg in latestResult.Messages)
                {
                    if (msg.StartsWith("使用中，已略过：", StringComparison.OrdinalIgnoreCase) ||
                        msg.StartsWith("使用中，已略過：", StringComparison.OrdinalIgnoreCase))
                    {
                        var fileName = msg.Split('：').LastOrDefault()?.Trim() ?? msg;
                        var (badge, glyph) = RecentOrganizedItemViewModel.DescribeExtension(fileName);
                        RecentOrganizedItems.Add(new RecentOrganizedItemViewModel
                        {
                            FileTypeBadge = badge,
                            FileTypeIconGlyph = glyph,
                            FileName = fileName,
                            OriginalLocation = DesktopPath,
                            TargetMonth = "—（保留于桌面）",
                            OrganizedTime = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"),
                            StatusLabel = "文件使用中，已略过",
                            StatusTone = "Warning"
                        });
                    }
                }
            }

            // Populate from history batches (newest first)
            foreach (var batch in batches.AsEnumerable().Reverse().Take(15))
            {
                foreach (var move in batch.Moves.Reverse())
                {
                    var fromName = Path.GetFileName(move.From);
                    var toName = Path.GetFileName(move.To);
                    var parentDir = Path.GetFileName(Path.GetDirectoryName(move.To) ?? string.Empty);
                    var (badge, glyph) = RecentOrganizedItemViewModel.DescribeExtension(fromName);

                    var isDuplicate = parentDir.Contains("Duplicates", StringComparison.OrdinalIgnoreCase) ||
                                      parentDir.Contains("重複檔", StringComparison.OrdinalIgnoreCase);
                    var isRenamed = !string.Equals(fromName, toName, StringComparison.OrdinalIgnoreCase);

                    var statusLabel = isDuplicate
                        ? "相同内容移入重复档"
                        : isRenamed
                            ? $"同名已重新命名 ({toName})"
                            : "成功";
                    var tone = isDuplicate ? "Warning" : "Normal";

                    RecentOrganizedItems.Add(new RecentOrganizedItemViewModel
                    {
                        FileTypeBadge = badge,
                        FileTypeIconGlyph = glyph,
                        FileName = fromName,
                        OriginalLocation = Path.GetDirectoryName(move.From) ?? DesktopPath,
                        TargetMonth = string.IsNullOrEmpty(parentDir) ? "整理 Organized" : parentDir,
                        OrganizedTime = batch.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                        StatusLabel = statusLabel,
                        StatusTone = tone
                    });

                    if (RecentOrganizedItems.Count >= 30) break;
                }
                if (RecentOrganizedItems.Count >= 30) break;
            }

            if (latestResult is null)
            {
                if (_undoableBatchCount > 0)
                {
                    OrganizerInteractiveStateBadge = "可以复原";
                    OrganizerInteractiveStateTone = "Normal";
                    OrganizerInteractiveStateDetail = $"目前已保存 {_undoableBatchCount} 批整理纪录，最近一批含 {_undoableFileCount} 个文件可一键复原。";
                }
                else
                {
                    OrganizerInteractiveStateBadge = "没有可复原纪录";
                    OrganizerInteractiveStateTone = "Neutral";
                    OrganizerInteractiveStateDetail = "尚无历史整理批次；执行整理后将自动保存最近 100 批复原纪录。";
                }
            }

            Notify(nameof(CanUndo));
            Notify(nameof(UndoStatusBadge));
            Notify(nameof(UndoStatusTone));
            Notify(nameof(TodayOrganizedCountDisplay));
            Notify(nameof(LastScanTimeDisplay));
            Notify(nameof(HasRecentOrganizedItems));
        }
        catch { }

        await ReloadActivityLogOnlyAsync();
    }

    private async Task ReloadActivityLogOnlyAsync()
    {
        try
        {
            _allActivityItems.Clear();
            if (File.Exists(_log.LogPath))
            {
                var lines = await File.ReadAllLinesAsync(_log.LogPath);
                foreach (var line in lines.Reverse().Take(250))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        var ts = root.TryGetProperty("timestamp", out var tsProp) && tsProp.TryGetDateTimeOffset(out var dt)
                            ? dt
                            : DateTimeOffset.Now;
                        var category = root.TryGetProperty("category", out var catProp)
                            ? catProp.GetString() ?? "一般"
                            : "一般";
                        var message = root.TryGetProperty("message", out var msgProp)
                            ? msgProp.GetString() ?? string.Empty
                            : string.Empty;

                        _allActivityItems.Add(ParseActivityEntry(ts, category, message));
                    }
                    catch { }
                }
            }
            ApplyActivityLogFilter();
        }
        catch { }
    }

    private static ActivityLogItemViewModel ParseActivityEntry(DateTimeOffset ts, string rawCategory, string message)
    {
        var filterGroup = "文件整理";
        var tone = "Normal";
        var action = rawCategory;
        var target = "系统";
        var result = message;

        if (rawCategory.Contains("错误") || rawCategory.Contains("錯誤"))
        {
            filterGroup = "错误";
            tone = "Error";
            action = rawCategory;
            target = "应用程式运作";
            result = message.Split('\n').FirstOrDefault() ?? message;
        }
        else if (rawCategory.Contains("警告"))
        {
            filterGroup = "警告";
            tone = "Warning";
        }
        else if (rawCategory is "介入" or "結果" or "结果" or "效能优化" or "效能優化")
        {
            filterGroup = "效能优化";
            if (message.Contains("未找到") || message.Contains("尚未觀察"))
            {
                tone = "Warning";
            }
            action = rawCategory == "介入" ? "安全优先权调度" : rawCategory == "結果" || rawCategory == "结果" ? "效能急救评估" : rawCategory;
            target = "背景与前景行程";
            result = message;
        }
        else if (rawCategory.Contains("整理") || rawCategory.Contains("復原") || rawCategory.Contains("复原"))
        {
            filterGroup = "文件整理";
            var parts = message.Split(" → ", StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                var fileName = Path.GetFileName(parts[0]);
                var destFolder = Path.GetFileName(Path.GetDirectoryName(parts[1]) ?? string.Empty);
                var isDup = destFolder.Contains("Duplicates", StringComparison.OrdinalIgnoreCase) ||
                            destFolder.Contains("重複檔", StringComparison.OrdinalIgnoreCase);
                action = rawCategory.Contains("復原") || rawCategory.Contains("复原") ? "复原上次整理" : "自动/手动月份归档";
                target = fileName;
                result = isDup ? $"移入重复档资料夹（{destFolder}）" : $"归档至 {destFolder}\\{Path.GetFileName(parts[1])}";
                if (isDup) tone = "Warning";
            }
        }

        return new ActivityLogItemViewModel
        {
            RawTimestamp = ts,
            TimeDisplay = ts.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            CategoryDisplay = filterGroup,
            FilterGroup = filterGroup,
            ActionDisplay = action,
            TargetDisplay = target,
            ResultDisplay = result,
            StatusTone = tone
        };
    }

    private void ApplyActivityLogFilter()
    {
        FilteredActivityItems.Clear();
        foreach (var item in _allActivityItems)
        {
            var matchGroup = _selectedActivityFilter == "全部" ||
                             string.Equals(item.FilterGroup, _selectedActivityFilter, StringComparison.OrdinalIgnoreCase) ||
                             (_selectedActivityFilter == "警告" && item.StatusTone == "Warning");
            if (!matchGroup) continue;

            if (!string.IsNullOrWhiteSpace(_activitySearchText))
            {
                var q = _activitySearchText.Trim();
                var hit = item.TargetDisplay.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                          item.ResultDisplay.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                          item.ActionDisplay.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                          item.CategoryDisplay.Contains(q, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
            }

            FilteredActivityItems.Add(item);
        }

        Notify(nameof(HasFilteredActivityItems));
        Notify(nameof(ActivityCountSummary));
    }

    // =========================================================
    // UI Preferences & Theme Switching
    // =========================================================
    private string UiPreferencesPath => Path.Combine(_settingsService.DataDirectory, "ui-preferences.json");

    private UiPreferences LoadUiPreferences()
    {
        try
        {
            if (File.Exists(UiPreferencesPath))
                return JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(UiPreferencesPath), JsonOptions) ?? new();
        }
        catch { }
        return new UiPreferences();
    }

    private void SaveUiPreferences()
    {
        try
        {
            Directory.CreateDirectory(_settingsService.DataDirectory);
            File.WriteAllText(UiPreferencesPath, JsonSerializer.Serialize(_uiPrefs, JsonOptions));
        }
        catch { }
    }

    private void ApplyThemeResources(string themeMode)
    {
        var isDark = string.Equals(themeMode, "Dark", StringComparison.OrdinalIgnoreCase);
        var res = System.Windows.Application.Current.Resources;

        if (isDark)
        {
            res["WindowSurfaceBrush"] = new SolidColorBrush(MediaColor.FromRgb(15, 23, 42));      // #0F172A
            res["SidebarSurfaceBrush"] = new SolidColorBrush(MediaColor.FromRgb(17, 24, 39));     // #111827
            res["CardBrush"] = new SolidColorBrush(MediaColor.FromRgb(30, 41, 59));               // #1E293B
            res["ElevatedCardBrush"] = new SolidColorBrush(MediaColor.FromRgb(30, 41, 59));       // #1E293B
            res["SubtleSurfaceBrush"] = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85));      // #334155
            res["CardBorderBrush"] = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85));         // #334155
            res["DividerBrush"] = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85));            // #334155
            res["TextPrimaryBrush"] = new SolidColorBrush(MediaColor.FromRgb(248, 250, 252));     // #F8FAFC
            res["TextSecondaryBrush"] = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184));   // #94A3B8
            res["TextMutedBrush"] = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139));       // #64748B
            res["NavActiveBgBrush"] = new SolidColorBrush(MediaColor.FromRgb(19, 78, 74));        // #134E4A
            res["BrandSurfaceTintBrush"] = new SolidColorBrush(MediaColor.FromRgb(19, 78, 74));   // #134E4A
            res["BrandLightBgBrush"] = new SolidColorBrush(MediaColor.FromRgb(15, 118, 110));     // #0F766E
        }
        else
        {
            res["WindowSurfaceBrush"] = new SolidColorBrush(MediaColor.FromRgb(241, 245, 249));    // #F1F5F9
            res["SidebarSurfaceBrush"] = new SolidColorBrush(MediaColor.FromRgb(248, 250, 252));   // #F8FAFC
            res["CardBrush"] = new SolidColorBrush(MediaColor.FromRgb(248, 250, 252));             // #F8FAFC
            res["ElevatedCardBrush"] = new SolidColorBrush(MediaColor.FromRgb(255, 255, 255));     // #FFFFFF
            res["SubtleSurfaceBrush"] = new SolidColorBrush(MediaColor.FromRgb(241, 245, 249));    // #F1F5F9
            res["CardBorderBrush"] = new SolidColorBrush(MediaColor.FromRgb(226, 232, 240));       // #E2E8F0
            res["DividerBrush"] = new SolidColorBrush(MediaColor.FromRgb(234, 236, 240));          // #EAECF0
            res["TextPrimaryBrush"] = new SolidColorBrush(MediaColor.FromRgb(31, 41, 55));         // #1F2937
            res["TextSecondaryBrush"] = new SolidColorBrush(MediaColor.FromRgb(102, 112, 133));    // #667085
            res["TextMutedBrush"] = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184));        // #94A3B8
            res["NavActiveBgBrush"] = new SolidColorBrush(MediaColor.FromRgb(230, 255, 250));      // #E6FFFA
            res["BrandSurfaceTintBrush"] = new SolidColorBrush(MediaColor.FromRgb(240, 253, 250)); // #F0FDFA
            res["BrandLightBgBrush"] = new SolidColorBrush(MediaColor.FromRgb(204, 251, 241));     // #CCFBF1
        }
    }

    // =========================================================
    // System Tray & Lifecycle
    // =========================================================
    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("开启智慧桌面管家", null, (_, _) => ShowFromTray());
        menu.Items.Add("立即整理桌面文件", null, async (_, _) => await Dispatcher.InvokeAsync(async () => await RunOrganizerAsync(true)));
        menu.Items.Add("立即效能急救", null, async (_, _) => await Dispatcher.InvokeAsync(async () => await RunRescueAsync(false)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("完全退出", null, (_, _) => ExitApplication());

        System.Drawing.Icon appIcon = System.Drawing.SystemIcons.Shield;
        try
        {
            var streamInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/SmartDesktopGuardian;component/Resources/Brand/SmartDesktopGuardian.ico"));
            if (streamInfo?.Stream is not null)
            {
                appIcon = new System.Drawing.Icon(streamInfo.Stream);
            }
        }
        catch { }

        var icon = new Forms.NotifyIcon
        {
            Text = "Smart Desktop Guardian｜智慧桌面管家",
            Icon = appIcon,
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _trayIcon.ShowBalloonTip(1500, "Smart Desktop Guardian", "程式已最小化至系统匣继续守护。", Forms.ToolTipIcon.Info);
    }

    private void ExitAppButton_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_uiPrefs.MinimizeToTrayOnClose)
        {
            _applicationCancellation.Cancel();
            StopMonitoring();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            System.Windows.Application.Current.Shutdown();
            return;
        }
        e.Cancel = true;
        Hide();
        _trayIcon.ShowBalloonTip(1500, "Smart Desktop Guardian", "程式仍在系统匣执行。", Forms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _applicationCancellation.Cancel();
        StopMonitoring();
        _allowClose = true;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void NotifyMonitoring()
    {
        Notify(nameof(IsMonitoring));
        Notify(nameof(BackgroundMonitoringEnabled));
        Notify(nameof(MonitorButtonText));
        Notify(nameof(MonitorBadgeText));
        Notify(nameof(MonitorBadgeTone));
    }

    private void NotifyHomeStates()
    {
        Notify(nameof(OverallSystemStateBadge));
        Notify(nameof(OverallSystemStateTone));
        Notify(nameof(OverallSystemStateDescription));
        Notify(nameof(HomeOrganizerCardStateBadge));
        Notify(nameof(HomeOrganizerCardStateTone));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null, params string[] related)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(property);
        foreach (var name in related) Notify(name);
        return true;
    }

    private void Notify([CallerMemberName] string? property = null)
        => PropertyChanged?.Invoke(this, new(property));
}

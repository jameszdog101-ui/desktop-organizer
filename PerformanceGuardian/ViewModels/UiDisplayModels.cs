using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace PerformanceGuardian.ViewModels;

public sealed class RecentOrganizedItemViewModel
{
    public string FileTypeBadge { get; init; } = "FILE";
    public string FileTypeIconGlyph { get; init; } = "📄";
    public string FileName { get; init; } = string.Empty;
    public string OriginalLocation { get; init; } = string.Empty;
    public string TargetMonth { get; init; } = string.Empty;
    public string OrganizedTime { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = "成功";
    public string StatusTone { get; init; } = "Normal";

    public static (string Badge, string Glyph) DescribeExtension(string fileNameOrPath)
    {
        var ext = Path.GetExtension(fileNameOrPath).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => ("PDF", "📕"),
            ".doc" or ".docx" or ".odt" or ".rtf" or ".hwp" => (ext.TrimStart('.').ToUpperInvariant(), "📘"),
            ".xls" or ".xlsx" or ".csv" or ".ods" => (ext.TrimStart('.').ToUpperInvariant(), "📗"),
            ".ppt" or ".pptx" or ".odp" => (ext.TrimStart('.').ToUpperInvariant(), "📙"),
            ".txt" or ".md" => (ext.TrimStart('.').ToUpperInvariant(), "📄"),
            _ => ("DOC", "📄")
        };
    }
}

public sealed class ActivityLogItemViewModel
{
    public DateTimeOffset RawTimestamp { get; init; }
    public string TimeDisplay { get; init; } = string.Empty;
    public string CategoryDisplay { get; init; } = string.Empty;
    public string FilterGroup { get; init; } = "全部";
    public string ActionDisplay { get; init; } = string.Empty;
    public string TargetDisplay { get; init; } = string.Empty;
    public string ResultDisplay { get; init; } = string.Empty;
    public string StatusTone { get; init; } = "Normal";
}

public sealed class FormatChipViewModel : INotifyPropertyChanged
{
    private bool _isEnabled = true;
    private readonly Action? _onChanged;

    public FormatChipViewModel(string extension, string groupLabel, bool isEnabled, Action? onChanged = null)
    {
        Extension = extension;
        DisplayLabel = extension.TrimStart('.').ToUpperInvariant();
        GroupLabel = groupLabel;
        _isEnabled = isEnabled;
        _onChanged = onChanged;
    }

    public string Extension { get; }
    public string DisplayLabel { get; }
    public string GroupLabel { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            _onChanged?.Invoke();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class UiPreferences
{
    public int MonitorIntervalSeconds { get; set; } = 5;
    public int PressureTriggerCount { get; set; } = 3;
    public int CooldownMinutes { get; set; } = 5;
    public bool AutoRestoreOnStop { get; set; } = true;
    public int SsdWarningThresholdPercent { get; set; } = 15;
    public bool LaunchAtLoginPreference { get; set; } = false;
    public bool StartMinimizedPreference { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public string ThemeMode { get; set; } = "Light";
    public string LanguageMode { get; set; } = "繁體中文";
    public bool DuplicateCheckEnabled { get; set; } = true;
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using WpfBinding = System.Windows.Data.Binding;

namespace PerformanceGuardian.Converters;

public sealed class StringEqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var current = value?.ToString() ?? string.Empty;
        var target = parameter?.ToString() ?? string.Empty;
        return string.Equals(current, target, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => WpfBinding.DoNothing;
}

public sealed class StringEqualityToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var current = value?.ToString() ?? string.Empty;
        var target = parameter?.ToString() ?? string.Empty;
        return string.Equals(current, target, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null)
            return parameter.ToString()!;
        return WpfBinding.DoNothing;
    }
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        return flag ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => WpfBinding.DoNothing;
}

public sealed class ToneToBackgroundBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NormalBrush = Freeze(new(MediaColor.FromRgb(204, 251, 241)));     // #CCFBF1
    private static readonly SolidColorBrush WarningBrush = Freeze(new(MediaColor.FromRgb(254, 243, 199)));    // #FEF3C7
    private static readonly SolidColorBrush ErrorBrush = Freeze(new(MediaColor.FromRgb(254, 226, 226)));      // #FEE2E2
    private static readonly SolidColorBrush ProcessingBrush = Freeze(new(MediaColor.FromRgb(224, 242, 254))); // #E0F2FE
    private static readonly SolidColorBrush NeutralBrush = Freeze(new(MediaColor.FromRgb(241, 245, 249)));    // #F1F5F9

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value?.ToString()?.ToLowerInvariant()) switch
        {
            "normal" or "success" => NormalBrush,
            "warning" or "attention" => WarningBrush,
            "error" or "highload" or "danger" => ErrorBrush,
            "processing" or "busy" => ProcessingBrush,
            _ => NeutralBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => WpfBinding.DoNothing;

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}

public sealed class ToneToForegroundBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NormalBrush = Freeze(new(MediaColor.FromRgb(15, 118, 110)));     // #0F766E
    private static readonly SolidColorBrush WarningBrush = Freeze(new(MediaColor.FromRgb(180, 83, 9)));      // #B45309
    private static readonly SolidColorBrush ErrorBrush = Freeze(new(MediaColor.FromRgb(185, 28, 28)));       // #B91C1C
    private static readonly SolidColorBrush ProcessingBrush = Freeze(new(MediaColor.FromRgb(3, 105, 161)));  // #0369A1
    private static readonly SolidColorBrush NeutralBrush = Freeze(new(MediaColor.FromRgb(71, 84, 103)));     // #475467

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value?.ToString()?.ToLowerInvariant()) switch
        {
            "normal" or "success" => NormalBrush,
            "warning" or "attention" => WarningBrush,
            "error" or "highload" or "danger" => ErrorBrush,
            "processing" or "busy" => ProcessingBrush,
            _ => NeutralBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => WpfBinding.DoNothing;

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}

public sealed class MetricPercentToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NormalBrush = Freeze(new(MediaColor.FromRgb(13, 148, 136)));  // #0D9488
    private static readonly SolidColorBrush WarningBrush = Freeze(new(MediaColor.FromRgb(217, 119, 6)));  // #D97706
    private static readonly SolidColorBrush ErrorBrush = Freeze(new(MediaColor.FromRgb(220, 38, 38)));    // #DC2626

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var pct = value is double d ? d : 0d;
        if (pct >= 88) return ErrorBrush;
        if (pct >= 75) return WarningBrush;
        return NormalBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => WpfBinding.DoNothing;

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}

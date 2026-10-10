using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DiskLocker.Views;

/// <summary>
/// 状态胶囊取色：把 DiskState.StatusBrushKey（Accent/Warn/Neutral/…）映射到对应令牌画刷。
/// parameter 取 bg / border / text。
/// </summary>
public class PillBrushConverter : IValueConverter
{
    public static readonly PillBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        var part = parameter as string;
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(part)) return Brushes.Transparent;

        var resourceName = part switch
        {
            "bg" => $"Pill{key}BgBrush",
            "border" => $"Pill{key}BorderBrush",
            "text" => $"Pill{key}TextBrush",
            _ => $"Pill{key}BgBrush"
        };

        return Application.Current?.TryFindResource(resourceName) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>字符串非空 → Visible，用于可选文案的显隐</summary>
public class StringToVisibilityConverter : IValueConverter
{
    public static readonly StringToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>true → Visible（parameter 传 "invert" 时反向）</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if ((parameter as string) == "invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

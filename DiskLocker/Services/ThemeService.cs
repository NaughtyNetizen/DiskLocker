using System.Windows;
using Microsoft.Win32;

namespace DiskLocker.Services;

public enum AppThemeMode
{
    /// <summary>跟随系统的浅色/深色设置</summary>
    System,
    Dark,
    Light
}

/// <summary>
/// 主题服务：在运行时替换令牌字典（Themes/Dark.xaml ⇄ Themes/Light.xaml）。
/// 控件样式全部用 DynamicResource 引用令牌键，因此换字典即全局换肤，无需重开窗口。
/// </summary>
public static class ThemeService
{
    private const string DarkSource = "Themes/Dark.xaml";
    private const string LightSource = "Themes/Light.xaml";

    public static AppThemeMode Mode { get; private set; } = AppThemeMode.System;

    /// <summary>当前实际生效是否为深色</summary>
    public static bool IsDark { get; private set; } = true;

    /// <summary>主题实际切换后触发（用于刷新非 WPF 资源，如托盘图标）</summary>
    public static event Action? ThemeChanged;

    public static void Initialize(string? mode)
    {
        Mode = Parse(mode);
        Apply(force: true);
    }

    public static void SetMode(AppThemeMode mode)
    {
        Mode = mode;
        Apply(force: true);
    }

    /// <summary>跟随系统时，检查系统主题是否已改变（在窗口激活等时机调用）</summary>
    public static void RefreshIfSystemDriven()
    {
        if (Mode != AppThemeMode.System) return;
        if (IsSystemDark() == IsDark) return;
        Apply(force: false);
    }

    public static string ToSettingKey(AppThemeMode mode) => mode switch
    {
        AppThemeMode.Dark => "dark",
        AppThemeMode.Light => "light",
        _ => "system"
    };

    public static AppThemeMode Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "dark" => AppThemeMode.Dark,
        "light" => AppThemeMode.Light,
        _ => AppThemeMode.System
    };

    private static void Apply(bool force)
    {
        var dark = Mode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => IsSystemDark()
        };

        if (!force && dark == IsDark) return;
        IsDark = dark;

        var app = Application.Current;
        if (app == null) return;

        var source = dark ? DarkSource : LightSource;
        var merged = app.Resources.MergedDictionaries;
        var current = merged.FirstOrDefault(d => IsThemeDictionary(d));

        var next = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };
        if (current != null)
            merged[merged.IndexOf(current)] = next;
        else
            merged.Insert(0, next);

        ThemeChanged?.Invoke();
    }

    private static bool IsThemeDictionary(ResourceDictionary dict)
    {
        var s = dict.Source?.OriginalString;
        return s != null && (s.EndsWith(DarkSource, StringComparison.OrdinalIgnoreCase)
                          || s.EndsWith(LightSource, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>AppsUseLightTheme：1=浅色，0=深色</summary>
    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return true;
        }
    }
}

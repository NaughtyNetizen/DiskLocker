using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using static DiskLocker.Native.WinApi;

namespace DiskLocker.Native;

/// <summary>
/// Win11 Mica（云母）背景辅助类。
/// 在 Windows 11 上启用系统 Mica 背景材质；深色/浅色非客户区跟随系统主题。
/// 窗口背景使用半透明渐变（由窗口模板提供），即使材质未生效也不会出现黑底。
/// </summary>
public static class MicaHelper
{
    /// <summary>Windows 11 的最低内部版本号 (Build 22000)</summary>
    private const int Win11Build = 22000;

    /// <summary>
    /// 尝试为指定窗口启用 Mica 云母背景。
    /// 窗口背景保持模板中的半透明渐变：
    /// 材质生效时透出系统 Mica，未生效时半透明渐变兜底（不会黑屏）。
    /// </summary>
    public static bool TryEnableAcrylic(Window window)
    {
        try
        {
            // 仅 Windows 11 及以上支持系统背景材质
            if (Environment.OSVersion.Version.Build < Win11Build)
                return false;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return false;

            // 【D-08 修复】深色/浅色非客户区跟随系统主题（原硬编码为 0 浅色）
            int useDarkMode = IsSystemDarkMode() ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

            // Win11 系统圆角
            int cornerPreference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

            // 【D-08 修复】设置系统背景材质为 Mica（云母），与 README 描述一致（原为 Acrylic）
            int backdropType = DWMSBT_MAINWINDOW;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            return hr == 0; // S_OK == 0
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检测系统是否使用深色主题
    /// </summary>
    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // AppsUseLightTheme: 1=浅色, 0=深色
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 在窗口句柄创建后应用 Acrylic。
    /// 同时挂接 SourceInitialized 与 ContentRendered，双重保证材质生效。
    /// </summary>
    public static void Apply(Window window)
    {
        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
        {
            TryEnableAcrylic(window);
        }
        else
        {
            window.SourceInitialized += (_, _) => TryEnableAcrylic(window);
        }

        // 渲染完成后再应用一次，避免设置过早导致材质未生效
        window.ContentRendered += (_, _) => TryEnableAcrylic(window);
    }
}

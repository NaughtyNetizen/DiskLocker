using System.Windows;
using System.Windows.Interop;
using static DiskLocker.Native.WinApi;

namespace DiskLocker.Native;

/// <summary>
/// Win11 Acrylic（亚克力/毛玻璃）背景辅助类。
/// 在 Windows 11 上启用类似 macOS 的毛玻璃系统背景；
/// 窗口背景使用半透明渐变（由窗口模板提供），即使材质未生效也不会出现黑底。
/// </summary>
public static class MicaHelper
{
    /// <summary>Windows 11 的最低内部版本号 (Build 22000)</summary>
    private const int Win11Build = 22000;

    /// <summary>
    /// 尝试为指定窗口启用 Acrylic 毛玻璃背景。
    /// 窗口背景保持模板中的半透明渐变：
    /// 材质生效时透出系统毛玻璃，未生效时半透明渐变兜底（不会黑屏）。
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

            // 浅色主题非客户区（配合自绘标题栏）
            int useDarkMode = 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

            // Win11 系统圆角
            int cornerPreference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

            // 设置系统背景材质为 Acrylic（毛玻璃）
            int backdropType = DWMSBT_TRANSIENTWINDOW;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            return hr == 0; // S_OK == 0
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

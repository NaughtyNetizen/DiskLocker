using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DiskLocker.Services;

namespace DiskLocker.Native;

/// <summary>
/// 浮层窗口样式：让 Toast 与托盘菜单既不抢焦点、也不出现在任务栏和 Alt+Tab 里。
/// </summary>
public static class ToastExStyle
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static void ApplyNoActivate(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var current = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            var desired = current | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(desired));
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"ToastExStyle.ApplyNoActivate failed: {ex.Message}");
        }
    }
}

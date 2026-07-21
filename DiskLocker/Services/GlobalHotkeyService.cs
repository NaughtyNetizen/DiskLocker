using System.Runtime.InteropServices;
using System.Windows.Interop;
using DiskLocker.Native;
using static DiskLocker.Native.WinApi;

namespace DiskLocker.Services;

/// <summary>
/// 全局热键服务
/// 通过创建隐藏的消息窗口接收 WM_HOTKEY 消息
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    private HwndSource? _hwndSource;
    private IntPtr _windowHandle;
    private int _currentHotKeyId = -1;
    private bool _disposed;

    /// <summary>热键按下时触发</summary>
    public event Action? HotKeyPressed;

    private const int HotKeyId = 0x9001;

    /// <summary>注册全局热键</summary>
    public bool Register(uint modifiers, uint virtualKey)
    {
        // 如果已注册，先取消
        Unregister();

        // 创建隐藏的消息窗口
        if (_hwndSource == null)
        {
            var parameters = new HwndSourceParameters("DiskLockerHotkeyWindow")
            {
                WindowStyle = 0,
                ExtendedWindowStyle = 0,
                ParentWindow = WinApi.HWND_MESSAGE
            };
            _hwndSource = new HwndSource(parameters);
            _hwndSource.AddHook(WndProc);
            _windowHandle = _hwndSource.Handle;
        }

        var success = RegisterHotKey(_windowHandle, HotKeyId, modifiers | MOD_NOREPEAT, virtualKey);
        if (success)
        {
            _currentHotKeyId = HotKeyId;
        }
        return success;
    }

    /// <summary>取消注册热键</summary>
    public void Unregister()
    {
        if (_currentHotKeyId >= 0 && _windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, _currentHotKeyId);
            _currentHotKeyId = -1;
        }
    }

    /// <summary>当前是否已注册</summary>
    public bool IsRegistered => _currentHotKeyId >= 0;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WinApi.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id == HotKeyId)
            {
                HotKeyPressed?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Unregister();
        _hwndSource?.Dispose();
    }
}

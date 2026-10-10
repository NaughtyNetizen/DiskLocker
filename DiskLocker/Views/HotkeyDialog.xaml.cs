using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DiskLocker.Native;
using DiskLocker.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DiskLocker.Views;

/// <summary>
/// 快捷键录制弹窗（设计稿 D5）：原设计只能看不能改，这里支持录制组合键并即时提示键位冲突。
/// </summary>
public partial class HotkeyDialog : Window
{
    private const uint DefaultModifiers = WinApi.MOD_CONTROL | WinApi.MOD_ALT;
    private const uint DefaultKey = 0x4C; // 'L'

    private readonly LockManager _lockManager;
    private uint _modifiers;
    private uint _virtualKey;
    private bool _recording = true;

    public HotkeyDialog(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;
        _modifiers = lockManager.Settings.Current.HotKeyModifiers;
        _virtualKey = lockManager.Settings.Current.HotKeyVirtualKey;
        Render();

        Loaded += (_, _) => Capture();
        PreviewKeyDown += HotkeyDialog_PreviewKeyDown;
    }

    private void Capture()
    {
        _recording = true;
        RecordHint.Text = "正在录制… 请按下新的组合键";
        RecordBox.BorderBrush = (Brush)FindResource("AccentBrush");
        Focus();
        Keyboard.Focus(this);
    }

    private void Render()
    {
        ComboText.Text = new Models.AppSettings
        {
            HotKeyModifiers = _modifiers,
            HotKeyVirtualKey = _virtualKey
        }.HotKeyDisplayText;
    }

    private void HotkeyDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 纯修饰键不视为组合，等真正的按键
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        if (key == Key.Escape)
        {
            DialogResult = false;
            Close();
            return;
        }

        uint modifiers = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers |= WinApi.MOD_CONTROL;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers |= WinApi.MOD_ALT;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers |= WinApi.MOD_SHIFT;
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) modifiers |= WinApi.MOD_WIN;

        if (modifiers == 0)
        {
            ShowConflict("组合键至少要包含 Ctrl、Alt、Win 或 Shift 中的一个。", occupied: false);
            return;
        }

        _modifiers = modifiers;
        _virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        Render();

        var occupied = IsOccupied(_modifiers, _virtualKey);
        if (occupied)
        {
            ShowConflict(ComboText.Text + " 已被其他程序占用，保存后可能失效，建议换一个。", occupied: true);
        }
        else
        {
            ConflictBar.Visibility = Visibility.Collapsed;
        }
        e.Handled = true;
    }

    /// <summary>用一个临时消息窗口试探注册，判断该组合是否已被系统或其他程序占用</summary>
    private bool IsOccupied(uint modifiers, uint virtualKey)
    {
        if (modifiers == _lockManager.Settings.Current.HotKeyModifiers
            && virtualKey == _lockManager.Settings.Current.HotKeyVirtualKey)
        {
            return false; // 与当前一致，肯定可用
        }

        try
        {
            using var probe = new GlobalHotkeyService();
            return !probe.Register(modifiers, virtualKey);
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"HotkeyDialog probe failed: {ex.Message}");
            return false;
        }
    }

    private void ShowConflict(string message, bool occupied)
    {
        ConflictText.Text = message;
        ConflictBar.Visibility = Visibility.Visible;
    }

    private void RecordBox_Click(object sender, MouseButtonEventArgs e) => Capture();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _modifiers = DefaultModifiers;
        _virtualKey = DefaultKey;
        Render();
        ConflictBar.Visibility = Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (IsOccupied(_modifiers, _virtualKey))
        {
            ShowConflict(ComboText.Text + " 已被其他程序占用，请换一个组合键。", occupied: true);
            return;
        }

        if (_lockManager.UpdateHotkey(_modifiers, _virtualKey))
        {
            DialogResult = true;
            Close();
        }
        else
        {
            ShowConflict("保存失败：系统拒绝注册该组合键，已恢复原设置。", occupied: true);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiskLocker.Services;
using Brush = System.Windows.Media.Brush;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace DiskLocker.Views.Pages;

/// <summary>
/// 设置页 - 安全与密码、锁定行为、启动项、快捷键（修改即时保存）
/// </summary>
public partial class SettingsPage : UserControl
{
    private readonly LockManager _lockManager;
    private bool _loading = true;
    private System.Windows.Threading.DispatcherTimer? _feedbackTimer;

    public SettingsPage(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        Refresh();
    }

    /// <summary>回显当前配置（页面加载与外部状态刷新时调用）</summary>
    public void Refresh()
    {
        _loading = true;
        var settings = _lockManager.Settings.Current;

        foreach (ComboBoxItem item in TimeoutComboBox.Items)
        {
            if (item.Tag != null && int.Parse(item.Tag.ToString()!) == settings.IdleLockTimeoutMinutes)
            {
                item.IsSelected = true;
                break;
            }
        }

        LockOnScreenLockCheckBox.IsChecked = settings.LockOnScreenLock;
        LockOnSleepCheckBox.IsChecked = settings.LockOnSleep;
        AutoStartCheckBox.IsChecked = _lockManager.AutoStart.IsAutoStartEnabled();
        HotKeyText.Text = settings.HotKeyDisplayText;
        _loading = false;
    }

    // ===================== 锁定行为（即时保存） =====================

    private void TimeoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        var timeoutItem = TimeoutComboBox.SelectedItem as ComboBoxItem;
        var timeout = timeoutItem?.Tag != null ? int.Parse(timeoutItem.Tag.ToString()!) : 30;
        _lockManager.UpdateAutoLockSettings(timeout,
            LockOnScreenLockCheckBox.IsChecked == true,
            LockOnSleepCheckBox.IsChecked == true);
        ShowFeedback(timeout == 0
            ? "已保存：不会自动锁定"
            : $"已保存：{timeout} 分钟不动电脑就自动锁定", true);
    }

    private void LockBehavior_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var timeoutItem = TimeoutComboBox.SelectedItem as ComboBoxItem;
        var timeout = timeoutItem?.Tag != null ? int.Parse(timeoutItem.Tag.ToString()!) : 30;
        _lockManager.UpdateAutoLockSettings(timeout,
            LockOnScreenLockCheckBox.IsChecked == true,
            LockOnSleepCheckBox.IsChecked == true);

        if (sender == LockOnScreenLockCheckBox)
            ShowFeedback(LockOnScreenLockCheckBox.IsChecked == true
                ? "已开启：锁屏时自动锁定" : "已关闭：锁屏时不再自动锁定", true);
        else
            ShowFeedback(LockOnSleepCheckBox.IsChecked == true
                ? "已开启：睡眠时自动锁定" : "已关闭：睡眠时不再自动锁定", true);
    }

    // ===================== 启动项（即时保存 + 回读验证） =====================

    private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var enabled = AutoStartCheckBox.IsChecked == true;
        _lockManager.UpdateAutoStart(enabled);

        // 回读注册表验证写入是否真正生效
        var actual = _lockManager.AutoStart.IsAutoStartEnabled();
        if (actual != enabled)
        {
            _loading = true;
            AutoStartCheckBox.IsChecked = actual;
            _loading = false;
            ShowFeedback("设置开机自启失败，请稍后再试", false);
            return;
        }

        ShowFeedback(enabled ? "已开启：开机自动启动" : "已关闭：开机不自启", true);
    }

    // ===================== 修改密码 =====================

    private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        AttemptChangePassword();
    }

    private void NewPasswordBox2_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AttemptChangePassword();
        }
    }

    private void AttemptChangePassword()
    {
        var oldPassword = OldPasswordBox.Password;
        var newPassword = NewPasswordBox1.Password;
        var confirmPassword = NewPasswordBox2.Password;

        if (!_lockManager.Password.VerifyPassword(oldPassword, _lockManager.Settings.Current.PasswordHash))
        {
            ShowFeedback("当前密码不对，请重新输入", false);
            OldPasswordBox.Clear();
            OldPasswordBox.Focus();
            return;
        }

        if (!PasswordService.ValidatePasswordStrength(newPassword, out string error))
        {
            ShowFeedback(error, false);
            return;
        }

        if (newPassword != confirmPassword)
        {
            ShowFeedback("两次输入的新密码不一样", false);
            return;
        }

        if (_lockManager.UpdatePassword(newPassword))
        {
            OldPasswordBox.Clear();
            NewPasswordBox1.Clear();
            NewPasswordBox2.Clear();
            ShowFeedback("密码修改成功，下次解锁用新密码", true);
        }
        else
        {
            ShowFeedback("密码修改失败，请稍后再试", false);
        }
    }

    // ===================== 保存反馈 =====================

    /// <summary>显示保存反馈条（3 秒后自动隐藏）</summary>
    private void ShowFeedback(string message, bool success)
    {
        FeedbackBar.Style = (Style)FindResource(success ? "InfoBarSuccess" : "InfoBarError");
        FeedbackText.Text = message;
        FeedbackText.Foreground = (Brush)FindResource(success ? "SuccessBrush" : "DangerBrush");
        FeedbackBar.Visibility = Visibility.Visible;

        _feedbackTimer?.Stop();
        _feedbackTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _feedbackTimer.Tick += (s, e) =>
        {
            FeedbackBar.Visibility = Visibility.Collapsed;
            _feedbackTimer!.Stop();
        };
        _feedbackTimer.Start();
    }
}

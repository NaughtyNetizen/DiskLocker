using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskLocker.Models;
using DiskLocker.Native;
using DiskLocker.Services;
using DiskLocker.ViewModels;
using MessageBox = System.Windows.MessageBox;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;

namespace DiskLocker.Views;

/// <summary>
/// 主窗口 - 单页仪表盘（状态 | 磁盘管理 | 设置 三列横向布局）
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly LockManager _lockManager;
    private readonly DiskService _diskService;
    private bool _loading = true;

    public SettingsWindow(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;
        _diskService = lockManager.Disk;
        MicaHelper.Apply(this);
        Loaded += SettingsWindow_Loaded;
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        ReloadDisks();
        UpdateStatus();
        _loading = false;
    }

    /// <summary>加载当前设置到 UI</summary>
    private void LoadSettings()
    {
        var settings = _lockManager.Settings.Current;

        // 超时设置
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
    }

    /// <summary>重新加载磁盘卡片（已保护 + 可添加）</summary>
    private void ReloadDisks()
    {
        LoadProtectedDisks();
        LoadAvailableDisks();
    }

    /// <summary>加载已保护的目标磁盘卡片</summary>
    private void LoadProtectedDisks()
    {
        var settings = _lockManager.Settings.Current;
        var allDisks = _diskService.GetAllPhysicalDisks();
        var isLocked = _lockManager.IsLocked;

        var protectedList = new List<DiskCardModel>();
        foreach (var target in settings.TargetDisks)
        {
            var actual = allDisks.FirstOrDefault(d =>
                string.Equals(d.SerialNumber, target.SerialNumber, StringComparison.OrdinalIgnoreCase));
            var connected = actual != null;

            string lettersText;
            if (!connected)
            {
                lettersText = "未连接";
            }
            else
            {
                var letters = _diskService.GetDriveLettersForDisk(target.SerialNumber);
                lettersText = letters.Count > 0 ? string.Join(", ", letters) : "无盘符（已隐藏）";
            }

            protectedList.Add(new DiskCardModel
            {
                Model = string.IsNullOrEmpty(target.Model) ? "未知设备" : target.Model,
                SerialNumber = target.SerialNumber,
                SizeDisplay = actual?.SizeDisplay ?? string.Empty,
                IsConnected = connected,
                IsLocked = isLocked && connected,
                DriveLettersText = lettersText,
                Disk = actual
            });
        }

        ProtectedDisksControl.ItemsSource = protectedList;
        ProtectedCountText.Text = $"{protectedList.Count} 台";
        NoProtectedText.Visibility = protectedList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>加载可添加的磁盘卡片（非目标的外接盘）</summary>
    private void LoadAvailableDisks()
    {
        var settings = _lockManager.Settings.Current;
        var disks = _diskService.GetExternalDisks();
        if (disks.Count == 0)
            disks = _diskService.GetAllPhysicalDisks();

        var existingSerials = settings.TargetDisks
            .Select(t => t.SerialNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var available = disks
            .Where(d => !existingSerials.Contains(d.SerialNumber))
            .Select(d => new DiskCardModel
            {
                Model = string.IsNullOrEmpty(d.Model) ? "未知设备" : d.Model,
                SerialNumber = d.SerialNumber,
                SizeDisplay = d.SizeDisplay,
                IsConnected = true,
                Disk = d
            })
            .ToList();

        AvailableDisksControl.ItemsSource = available;
        NoAvailableText.Visibility = available.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>更新状态总览（左列）</summary>
    public void UpdateStatus()
    {
        var settings = _lockManager.Settings.Current;
        var isConnected = _lockManager.IsTargetConnected;
        var isLocked = _lockManager.IsLocked;

        // 大图标与状态文字
        if (isLocked)
        {
            StatusIconCircle.Background = (Brush)FindResource("DangerLightBrush");
            StatusIconGlyph.Text = "\uE72E"; // 锁
            StatusIconGlyph.Foreground = (Brush)FindResource("DangerBrush");
            LockStateText.Text = "已锁定";
            LockStateSubText.Text = "盘符已隐藏，拒绝访问";
        }
        else
        {
            StatusIconCircle.Background = (Brush)FindResource("AccentLightBrush");
            StatusIconGlyph.Text = "\uE785"; // 解锁
            StatusIconGlyph.Foreground = (Brush)FindResource("AccentBrush");
            LockStateText.Text = "已解锁";
            LockStateSubText.Text = "盘符已恢复，可正常访问";
        }

        // 连接状态
        ConnectionDot.Fill = isConnected
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("TextTertiaryBrush");
        ConnectionStatusText.Text = isConnected ? "已连接" : "未连接";
        ConnectionStatusText.Foreground = isConnected
            ? (Brush)FindResource("TextPrimaryBrush")
            : (Brush)FindResource("DangerBrush");

        // 盘符信息
        if (isConnected)
        {
            var letters = _lockManager.GetAllTargetDriveLetters();
            DriveLettersText.Text = letters.Count > 0 ? string.Join(", ", letters) : "无（已隐藏）";
        }
        else
        {
            DriveLettersText.Text = "--";
        }

        // 目标盘数量
        TargetCountText.Text = $"{settings.TargetDisks.Count} 台";

        // 按钮状态：锁定时始终允许尝试解锁（即使连接状态瞬间为 false）
        LockButton.IsEnabled = isConnected && !isLocked;
        UnlockButton.IsEnabled = isLocked;
    }

    // ===================== 磁盘卡片操作 =====================

    private void AddDiskCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DiskCardModel card } || card.Disk == null)
            return;

        if (_lockManager.IsLocked)
        {
            var result = MessageBox.Show(
                "添加新硬盘后将重新锁定所有硬盘（包括新添加的）。是否继续？",
                "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;
        }

        _lockManager.AddTargetDisk(card.Disk);
        ReloadDisks();
        UpdateStatus();
        StatusText.Text = $"已添加目标硬盘：{card.Model}";
    }

    private void RemoveDiskCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DiskCardModel card })
            return;

        var result = MessageBox.Show(
            $"确定要移除该目标硬盘吗？\n\n{card.Model} [S/N: {card.SerialNumber}]",
            "确认移除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        _lockManager.RemoveTargetDisk(card.SerialNumber);
        ReloadDisks();
        UpdateStatus();
        StatusText.Text = "目标硬盘已移除";
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ReloadDisks();
        UpdateStatus();
    }

    // ===================== 锁定 / 解锁 =====================

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lockManager.LockDisk())
        {
            StatusText.Text = "硬盘已锁定";
            ReloadDisks();
            UpdateStatus();
        }
        else
        {
            StatusText.Text = "锁定失败，请检查硬盘是否连接";
        }
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordDialog(_lockManager) { Owner = this };
        dialog.ShowDialog();
        ReloadDisks();
        UpdateStatus();
    }

    // ===================== 设置 =====================

    private void SaveAutoLockButton_Click(object sender, RoutedEventArgs e)
    {
        var timeoutItem = TimeoutComboBox.SelectedItem as ComboBoxItem;
        var timeout = timeoutItem?.Tag != null ? int.Parse(timeoutItem.Tag.ToString()!) : 30;
        var lockOnScreenLock = LockOnScreenLockCheckBox.IsChecked == true;
        var lockOnSleep = LockOnSleepCheckBox.IsChecked == true;

        _lockManager.UpdateAutoLockSettings(timeout, lockOnScreenLock, lockOnSleep);
        StatusText.Text = "自动锁定设置已保存";
    }

    private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var oldPassword = OldPasswordBox.Password;
        var newPassword = NewPasswordBox1.Password;
        var confirmPassword = NewPasswordBox2.Password;

        if (!_lockManager.Password.VerifyPassword(oldPassword, _lockManager.Settings.Current.PasswordHash))
        {
            StatusText.Text = "当前密码不正确";
            return;
        }

        if (!PasswordService.ValidatePasswordStrength(newPassword, out string error))
        {
            StatusText.Text = error;
            return;
        }

        if (newPassword != confirmPassword)
        {
            StatusText.Text = "两次输入的新密码不一致";
            return;
        }

        if (_lockManager.UpdatePassword(newPassword))
        {
            StatusText.Text = "密码修改成功";
            OldPasswordBox.Clear();
            NewPasswordBox1.Clear();
            NewPasswordBox2.Clear();
        }
        else
        {
            StatusText.Text = "密码修改失败";
        }
    }

    private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var enabled = AutoStartCheckBox.IsChecked == true;
        _lockManager.UpdateAutoStart(enabled);
        StatusText.Text = enabled ? "已启用开机自启" : "已禁用开机自启";
    }

    private void RightPanelScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            var offset = scrollViewer.VerticalOffset - e.Delta;
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(offset, scrollViewer.ScrollableHeight)));
            e.Handled = true;
        }
    }
}

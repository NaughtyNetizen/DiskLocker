using System.Windows;
using System.Windows.Controls;
using DiskLocker.Models;
using DiskLocker.Native;
using DiskLocker.Services;

namespace DiskLocker.Views;

/// <summary>
/// 首次配置向导
/// </summary>
public partial class SetupWizard : Window
{
    private readonly DiskService _diskService = new();
    private readonly PasswordService _passwordService = new();
    private List<DiskInfo> _disks = new();

    /// <summary>配置完成后回调，传递选中的磁盘列表和密码</summary>
    public Action<List<DiskInfo>, string, int, bool, bool, bool>? OnConfigured { get; set; }

    public SetupWizard()
    {
        InitializeComponent();
        MicaHelper.Apply(this);
        LoadDisks();
    }

    /// <summary>加载外接磁盘列表</summary>
    private void LoadDisks()
    {
        DiskListBox.Items.Clear();
        _disks = _diskService.GetExternalDisks();

        if (_disks.Count == 0)
        {
            // 没有外接磁盘，显示所有磁盘供选择
            _disks = _diskService.GetAllPhysicalDisks();
        }

        foreach (var disk in _disks)
        {
            var item = new ListBoxItem
            {
                Content = disk.DisplayName,
                Tag = disk
            };
            DiskListBox.Items.Add(item);
        }

        if (_disks.Count == 0)
        {
            StatusText.Text = "未检测到任何磁盘，请连接外接硬盘后点击刷新列表。";
        }
        else
        {
            // 默认选中第一个
            if (DiskListBox.Items.Count > 0)
                DiskListBox.SelectedIndex = 0;
            StatusText.Text = "";
        }
    }

    private void DiskListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateStatus();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadDisks();
    }

    private void PasswordBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText.Text = "";
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        // 验证磁盘选择（至少选一个）
        var selectedDisks = DiskListBox.SelectedItems
            .Cast<ListBoxItem>()
            .Select(item => (DiskInfo)item.Tag)
            .ToList();

        if (selectedDisks.Count == 0)
        {
            StatusText.Text = "请至少选择一个目标硬盘";
            return;
        }

        // 验证密码
        var password = PasswordBox1.Password;
        var confirmPassword = PasswordBox2.Password;

        if (!PasswordService.ValidatePasswordStrength(password, out string error))
        {
            StatusText.Text = error;
            return;
        }

        if (password != confirmPassword)
        {
            StatusText.Text = "两次输入的密码不一致";
            return;
        }

        // 获取超时设置
        var timeoutItem = TimeoutComboBox.SelectedItem as ComboBoxItem;
        var timeout = timeoutItem?.Tag != null ? int.Parse(timeoutItem.Tag.ToString()!) : 15;

        var lockOnScreenLock = LockOnScreenLockCheckBox.IsChecked == true;
        var lockOnSleep = LockOnSleepCheckBox.IsChecked == true;
        var autoStart = AutoStartCheckBox.IsChecked == true;

        // 回调
        OnConfigured?.Invoke(selectedDisks, password, timeout, lockOnScreenLock, lockOnSleep, autoStart);

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskLocker.Models;
using DiskLocker.Native;
using DiskLocker.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace DiskLocker.Views;

/// <summary>
/// 首次配置向导 - 分步式（选择硬盘 → 设置密码 → 行为偏好 → 完成确认）
/// </summary>
public partial class SetupWizard : Window
{
    private const int TotalSteps = 4;

    private readonly DiskService _diskService = new();
    private readonly PasswordService _passwordService = new();
    private List<DiskInfo> _disks = new();
    private int _currentStep = 1;

    /// <summary>配置完成后回调，传递选中的磁盘列表和密码</summary>
    public Action<List<DiskInfo>, string, int, bool, bool, bool>? OnConfigured { get; set; }

    public SetupWizard()
    {
        InitializeComponent();
        MicaHelper.Apply(this);
        LoadDisks();
        GoToStep(1);
    }

    // ===================== 步骤导航 =====================

    /// <summary>切换到指定步骤（1-4），更新指示器与面板</summary>
    private void GoToStep(int step)
    {
        _currentStep = step;
        StatusText.Text = "";

        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

        PrevButton.IsEnabled = step > 1;
        NextButton.Content = step == TotalSteps ? "完成配置" : "下一步";

        UpdateStepIndicator();

        if (step == 4)
            BuildSummary();
    }

    /// <summary>更新步骤指示器外观（当前步高亮、已完成步打勾）</summary>
    private void UpdateStepIndicator()
    {
        var accentBrush = (Brush)FindResource("AccentBrush");
        var subtleBrush = (Brush)FindResource("SubtleBrush");
        var borderBrush = (Brush)FindResource("ControlBorderBrush");
        var accentText = (Brush)FindResource("AccentTextBrush");
        var secondaryText = (Brush)FindResource("TextSecondaryBrush");

        for (int i = 1; i <= TotalSteps; i++)
        {
            var circle = (Border)FindName($"Step{i}Circle")!;
            var circleText = (TextBlock)FindName($"Step{i}CircleText")!;
            var label = (TextBlock)FindName($"Step{i}Label")!;

            if (i < _currentStep)
            {
                // 已完成：强调色底 + 对勾
                circle.Background = accentBrush;
                circle.BorderThickness = new Thickness(0);
                circleText.Text = "\uE73E"; // 对勾
                circleText.Foreground = Brushes.White;
                label.Foreground = secondaryText;
            }
            else if (i == _currentStep)
            {
                // 当前步：强调色底 + 白色数字
                circle.Background = accentBrush;
                circle.BorderThickness = new Thickness(0);
                circleText.Text = i.ToString();
                circleText.Foreground = Brushes.White;
                label.Foreground = accentText;
                label.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                // 未到步骤：灰色
                circle.Background = subtleBrush;
                circle.BorderThickness = new Thickness(1);
                circle.BorderBrush = borderBrush;
                circleText.Text = i.ToString();
                circleText.Foreground = secondaryText;
                label.Foreground = secondaryText;
                label.FontWeight = FontWeights.Normal;
            }
        }
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
            GoToStep(_currentStep - 1);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        // 每步先做本步校验
        switch (_currentStep)
        {
            case 1:
                if (!ValidateStep1()) return;
                break;
            case 2:
                if (!ValidateStep2()) return;
                break;
            case 3:
                // 行为偏好无需强制校验
                break;
            case 4:
                Finish();
                return;
        }

        if (_currentStep < TotalSteps)
            GoToStep(_currentStep + 1);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ===================== 步骤 1：选择目标硬盘 =====================

    /// <summary>加载外接磁盘列表（排除系统盘）</summary>
    private void LoadDisks()
    {
        DiskListBox.Items.Clear();
        _disks = _diskService.GetExternalDisks();

        if (_disks.Count == 0)
        {
            // 没有外接磁盘，显示所有磁盘供选择——【D-02 修复】排除系统盘，防止误锁导致系统崩溃
            _disks = _diskService.GetAllPhysicalDisks()
                .Where(d => !IsSystemDisk(d))
                .ToList();
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
            StatusText.Text = "没有找到能保护的硬盘，请插上移动硬盘后点“刷新列表”。";
        }
        else
        {
            // 默认选中第一个
            if (DiskListBox.Items.Count > 0)
                DiskListBox.SelectedIndex = 0;
        }
    }

    /// <summary>系统盘所在盘符（如 "C"），用于排除系统盘</summary>
    private static string SystemDriveLetter
    {
        get
        {
            var sysRoot = System.IO.Path.GetPathRoot(
                Environment.GetFolderPath(Environment.SpecialFolder.System));
            return sysRoot?.Length > 0 ? sysRoot[0].ToString() : "C";
        }
    }

    /// <summary>判断指定磁盘是否为系统盘（包含 Windows 系统目录所在盘符）</summary>
    private static bool IsSystemDisk(DiskInfo disk)
    {
        var sysLetter = SystemDriveLetter;
        return disk.DriveLetters.Any(l =>
            l.Length > 0 && char.ToUpperInvariant(l[0]) == char.ToUpperInvariant(sysLetter[0]));
    }

    /// <summary>获取当前勾选的磁盘列表</summary>
    private List<DiskInfo> GetSelectedDisks()
        => DiskListBox.SelectedItems
            .Cast<ListBoxItem>()
            .Select(item => (DiskInfo)item.Tag)
            .ToList();

    private bool ValidateStep1()
    {
        var selectedDisks = GetSelectedDisks();

        if (selectedDisks.Count == 0)
        {
            StatusText.Text = "请至少勾选一个要保护的硬盘";
            return false;
        }

        // 【D-02 修复】防御性校验：禁止选择系统盘
        if (selectedDisks.Any(IsSystemDisk))
        {
            StatusText.Text = "不能选择系统盘，锁住它电脑会开不了机。请重新选择。";
            return false;
        }

        return true;
    }

    private void DiskListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        StatusText.Text = "";
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadDisks();
    }

    // ===================== 步骤 2：设置密码 =====================

    private bool ValidateStep2()
    {
        var password = PasswordBox1.Password;
        var confirmPassword = PasswordBox2.Password;

        if (!PasswordService.ValidatePasswordStrength(password, out string error))
        {
            StatusText.Text = error;
            return false;
        }

        if (password != confirmPassword)
        {
            StatusText.Text = "两次输入的密码不一样，请再检查一下";
            return false;
        }

        return true;
    }

    private void PasswordBox_Changed(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "";

        // 实时密码提示
        var password = PasswordBox1.Password;
        var confirm = PasswordBox2.Password;

        if (password.Length == 0)
        {
            PasswordHintText.Text = "";
            return;
        }

        string hint;
        if (!PasswordService.ValidatePasswordStrength(password, out string error))
            hint = error;
        else if (confirm.Length > 0 && password != confirm)
            hint = "两次输入的密码不一样";
        else if (confirm.Length > 0 && password == confirm)
            hint = "密码没问题，可以进入下一步";
        else
            hint = "密码强度足够";

        PasswordHintText.Text = hint;
        PasswordHintText.Foreground = hint.Contains("没问题") || hint.Contains("足够")
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("TextTertiaryBrush");
    }

    // ===================== 步骤 4：汇总与完成 =====================

    /// <summary>构建汇总确认内容</summary>
    private void BuildSummary()
    {
        var selectedDisks = GetSelectedDisks();
        SummaryDisksText.Text = selectedDisks.Count > 0
            ? string.Join("\n", selectedDisks.Select(d => $"· {d.DisplayName}"))
            : "（未选择）";

        SummaryPasswordText.Text = "已设置（就不在这里显示啦）";

        var timeoutItem = TimeoutComboBox.SelectedItem as ComboBoxItem;
        var timeout = timeoutItem?.Tag != null ? int.Parse(timeoutItem.Tag.ToString()!) : 30;
        SummaryTimeoutText.Text = timeout == 0 ? "不自动锁定" : $"{timeout} 分钟不用电脑就自动锁定";

        var behaviors = new List<string>();
        behaviors.Add(LockOnScreenLockCheckBox.IsChecked == true ? "锁屏时自动锁定" : "锁屏时不锁定");
        behaviors.Add(LockOnSleepCheckBox.IsChecked == true ? "睡眠时自动锁定" : "睡眠时不锁定");
        behaviors.Add(AutoStartCheckBox.IsChecked == true ? "开机自动启动" : "开机不自启");
        SummaryBehaviorText.Text = string.Join("；", behaviors);
    }

    /// <summary>完成配置：校验全部输入并回调</summary>
    private void Finish()
    {
        if (!ValidateStep1()) { GoToStep(1); return; }
        if (!ValidateStep2()) { GoToStep(2); return; }

        var selectedDisks = GetSelectedDisks();
        var password = PasswordBox1.Password;

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
}

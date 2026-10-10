using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DiskLocker.Models;
using DiskLocker.Services;

namespace DiskLocker.Views.Pages;

/// <summary>
/// 设置页 - 语义分组卡片，改动即时保存；改密码与改热键收敛为弹窗。
/// </summary>
public partial class SettingsPage : UserControl
{
    private readonly LockManager _lockManager;
    private bool _loading = true;
    private DispatcherTimer? _savedTimer;

    public SettingsPage(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;
        BuildOptions();
        Loaded += (_, _) => { Refresh(); };
    }

    private void BuildOptions()
    {
        TimeoutCombo.Items.Add(new ComboBoxItem { Content = "禁用", Tag = 0 });
        foreach (var m in new[] { 5, 10, 15, 30, 60 })
            TimeoutCombo.Items.Add(new ComboBoxItem { Content = $"{m} 分钟", Tag = m });

        ThemeCombo.Items.Add(new ComboBoxItem { Content = "跟随系统", Tag = "system" });
        ThemeCombo.Items.Add(new ComboBoxItem { Content = "深色", Tag = "dark" });
        ThemeCombo.Items.Add(new ComboBoxItem { Content = "浅色", Tag = "light" });
    }

    public void Refresh()
    {
        _loading = true;
        var s = _lockManager.Settings.Current;

        SelectByTag(TimeoutCombo, s.IdleLockTimeoutMinutes);
        SelectByTag(ThemeCombo, ThemeService.ToSettingKey(ThemeService.Mode));

        ScreenLockToggle.IsChecked = s.LockOnScreenLock;
        SleepToggle.IsChecked = s.LockOnSleep;
        AutoStartToggle.IsChecked = _lockManager.AutoStart.IsAutoStartEnabled();
        HotKeyText.Text = s.HotKeyDisplayText;

        VersionTitle.Text = $"DiskLocker {App.AppVersion}";
        VersionDesc.Text = "硬盘保护助手 · 已是最新版本";
        ConfigPathHint.Text = "配置文件位于 " + System.IO.Path.GetDirectoryName(SettingsService.GetSettingsPath());

        _loading = false;
    }

    private static void SelectByTag(ComboBox combo, object tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (Equals(item.Tag, tag))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = -1;
    }

    private int TimeoutMinutes =>
        (TimeoutCombo.SelectedItem as ComboBoxItem)?.Tag is int m ? m : 30;

    // ===================== 即时保存 =====================

    private void Timeout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        _lockManager.UpdateAutoLockSettings(TimeoutMinutes,
            ScreenLockToggle.IsChecked == true, SleepToggle.IsChecked == true);
        ShowSaved(TimeoutMinutes == 0 ? "已设为不自动锁定" : $"已保存：空闲 {TimeoutMinutes} 分钟自动锁定");
    }

    private void LockBehavior_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        _lockManager.UpdateAutoLockSettings(TimeoutMinutes,
            ScreenLockToggle.IsChecked == true, SleepToggle.IsChecked == true);

        if (sender == ScreenLockToggle)
            ShowSaved(ScreenLockToggle.IsChecked == true ? "已开启：锁屏时自动锁定" : "已关闭：锁屏时不再自动锁定");
        else
            ShowSaved(SleepToggle.IsChecked == true ? "已开启：睡眠时自动锁定" : "已关闭：睡眠时不再自动锁定");
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsLoaded) return;

        var enabled = AutoStartToggle.IsChecked == true;
        _lockManager.UpdateAutoStart(enabled);

        // 回读注册表确认写入真的生效，失败就把开关拨回去
        var actual = _lockManager.AutoStart.IsAutoStartEnabled();
        if (actual != enabled)
        {
            _loading = true;
            AutoStartToggle.IsChecked = actual;
            _loading = false;
            ToastService.Show(this, Models.ActivitySeverity.Error, "设置未生效", "开机自启写入失败，请稍后再试。");
            return;
        }

        ShowSaved(enabled ? "已开启：开机自动启动" : "已关闭：开机不自启");
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        var mode = (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "system";
        ThemeService.SetMode(ThemeService.Parse(mode));
        _lockManager.UpdateTheme(mode);
        ShowSaved("已切换外观主题");
    }

    // ===================== 弹窗入口 =====================

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (!_lockManager.HasProtectionConfig)
        {
            App.Instance.OpenQuickStart(Window.GetWindow(this));
            return;
        }

        if (ChangePasswordDialog.Show(Window.GetWindow(this), _lockManager))
            ShowSaved("密码已更新，下次解锁请用新密码");
    }

    private void ChangeHotkey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeyDialog(_lockManager) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
        Refresh();
    }

    private void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        ToastService.Show(this, Models.ActivitySeverity.Info, "已是最新版本",
            $"DiskLocker {App.AppVersion} · 上次检查：今天");
    }

    // ===================== 「改动已自动保存」提示 =====================

    private void ShowSaved(string detail)
    {
        SavedPill.Visibility = Visibility.Visible;
        ToastService.Show(this, Models.ActivitySeverity.Success, "改动已自动保存", detail);

        _savedTimer?.Stop();
        _savedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _savedTimer.Tick += (_, _) =>
        {
            SavedPill.Visibility = Visibility.Collapsed;
            _savedTimer!.Stop();
        };
        _savedTimer.Start();
    }
}

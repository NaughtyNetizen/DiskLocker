using System.Windows;
using System.Windows.Controls;
using DiskLocker.Native;
using DiskLocker.Services;
using DiskLocker.Views.Pages;
using UserControl = System.Windows.Controls.UserControl;

namespace DiskLocker.Views;

/// <summary>
/// 主窗口 - 左侧导航 + 右侧内容页（首页 / 磁盘管理 / 设置）
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly LockManager _lockManager;

    private readonly HomePage _homePage;
    private readonly DisksPage _disksPage;
    private readonly SettingsPage _settingsPage;

    public SettingsWindow(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;

        _homePage = new HomePage(lockManager);
        _disksPage = new DisksPage(lockManager);
        _settingsPage = new SettingsPage(lockManager);

        // 首页"去添加硬盘" → 跳转到磁盘管理页
        _homePage.NavigateRequested += pageKey =>
        {
            if (pageKey == "Disks")
                ShowPage(_disksPage, NavDisksButton);
        };

        MicaHelper.Apply(this);

        // 默认显示首页
        ShowPage(_homePage, NavHomeButton);
    }

    /// <summary>切换内容页并更新导航选中态</summary>
    private void ShowPage(UserControl page, System.Windows.Controls.Button navButton)
    {
        PageHost.Content = page;

        NavHomeButton.Tag = null;
        NavDisksButton.Tag = null;
        NavSettingsButton.Tag = null;
        navButton.Tag = "Selected";

        // 切换后刷新新页面数据
        if (page == _homePage) _homePage.Refresh();
        else if (page == _disksPage) _disksPage.Refresh();
        else if (page == _settingsPage) _settingsPage.Refresh();
    }

    /// <summary>外部状态变化（托盘/热键/设备事件）时刷新当前页</summary>
    public void UpdateStatus()
    {
        if (PageHost.Content == _homePage) _homePage.Refresh();
        else if (PageHost.Content == _disksPage) _disksPage.Refresh();
        else if (PageHost.Content == _settingsPage) _settingsPage.Refresh();
    }

    // ===================== 导航按钮 =====================

    private void NavHomeButton_Click(object sender, RoutedEventArgs e)
        => ShowPage(_homePage, NavHomeButton);

    private void NavDisksButton_Click(object sender, RoutedEventArgs e)
        => ShowPage(_disksPage, NavDisksButton);

    private void NavSettingsButton_Click(object sender, RoutedEventArgs e)
        => ShowPage(_settingsPage, NavSettingsButton);
}

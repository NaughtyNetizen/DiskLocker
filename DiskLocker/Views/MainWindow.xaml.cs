using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DiskLocker.Native;
using DiskLocker.Services;
using DiskLocker.ViewModels;
using DiskLocker.Views.Pages;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DiskLocker.Views;

/// <summary>
/// 主窗口 - 230px 左侧一级导航（含常驻状态卡）+ 右侧内容页 + 磁盘详情抽屉浮层
/// </summary>
public partial class MainWindow : Window
{
    // Segoe Fluent Icons 字形（用转义写，避免私有区字符在源码里被编辑器吞掉）
    private const string GlyphAdd = "\uE710";
    private const string GlyphRefresh = "\uE72C";
    private const string GlyphLock = "\uE72E";
    private const string GlyphUnlock = "\uE785";

    private readonly LockManager _lockManager;

    private readonly OverviewPage _overviewPage;
    private readonly DisksPage _disksPage;
    private readonly ActivityPage _activityPage;
    private readonly SettingsPage _settingsPage;

    /// <summary>导航选中态程序化变更期间的重入闸门</summary>
    private bool _navigating;

    public MainWindow(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;

        MicaHelper.Apply(this);

        _overviewPage = new OverviewPage(lockManager);
        _disksPage = new DisksPage(lockManager);
        _activityPage = new ActivityPage(lockManager);
        _settingsPage = new SettingsPage(lockManager);

        _overviewPage.NavigateRequested += ShowPage;
        _disksPage.DrawerRequested += OpenDiskDrawer;

        _lockManager.StateChanged += OnLockStateChanged;
        Closed += (_, _) => _lockManager.StateChanged -= OnLockStateChanged;
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        ShowPage("Overview");
        UpdateStatus();
    }

    /// <summary>托盘 / 热键唤起：确保窗口可见并刷新</summary>
    public void Present()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        UpdateStatus();
        RefreshCurrentPage();
    }

    private void OnLockStateChanged() =>
        Dispatcher.BeginInvoke(() =>
        {
            UpdateStatus();
            RefreshCurrentPage();
        });

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DrawerHost.Visibility == Visibility.Visible)
        {
            CloseDiskDrawer();
            e.Handled = true;
        }
    }

    // ===================== 导航 =====================

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_navigating) return;
        if (sender is RadioButton { Tag: string key }) ShowPage(key);
    }

    /// <summary>切换到指定页面（Overview / Disks / Activity / Settings）</summary>
    public void ShowPage(string key)
    {
        UserControl page = key switch
        {
            "Disks" => _disksPage,
            "Activity" => _activityPage,
            "Settings" => _settingsPage,
            _ => _overviewPage
        };

        if (PageHost.Content != page) PageHost.Content = page;

        // 以编程方式勾选导航项会再次触发 Checked，不加闸门就是无限递归
        _navigating = true;
        try
        {
            SetChecked(key, NavOverview);
            SetChecked(key, NavDisks);
            SetChecked(key, NavActivity);
            SetChecked(key, NavSettings);
        }
        finally
        {
            _navigating = false;
        }

        RefreshPage(page);
    }

    private void SetChecked(string key, RadioButton button)
    {
        var want = (string)button.Tag == key;
        if (button.IsChecked != want) button.IsChecked = want;
    }

    private void RefreshCurrentPage()
    {
        if (PageHost.Content is UserControl page) RefreshPage(page);
    }

    /// <summary>
    /// 主题切换后重跑一遍取色：胶囊等位置用转换器解析令牌画刷，不是 DynamicResource，
    /// 需要重新绑定一次才会换成新主题的颜色。
    /// </summary>
    public void RefreshAfterThemeChange()
    {
        UpdateStatus();
        RefreshCurrentPage();
    }

    private void RefreshPage(UserControl page)
    {
        switch (page)
        {
            case OverviewPage p: p.Refresh(); break;
            case DisksPage p: p.Refresh(); break;
            case ActivityPage p: p.Refresh(); break;
            case SettingsPage p: p.Refresh(); break;
        }
    }

    // ===================== 侧栏常驻状态卡 =====================

    public void UpdateStatus()
    {
        var settings = _lockManager.Settings.Current;
        var count = settings.TargetDisks.Count;
        var hotkey = settings.HotKeyDisplayText;

        SidebarCountText.Text = $"{count} 块";
        VersionText.Text = $"v{App.AppVersion} · 以管理员权限运行";

        if (count == 0)
        {
            SidebarStateText.Text = "未设置";
            SidebarDot.Fill = (Brush)FindResource("TextMutedBrush");
            SidebarHintText.Text = "还没有要保护的硬盘，去概览页一键开始。";
            SidebarActionGlyph.Text = GlyphAdd;
            SidebarActionText.Text = "开始保护";
            SidebarActionButton.IsEnabled = true;
            return;
        }

        if (!_lockManager.IsTargetConnected)
        {
            SidebarStateText.Text = "未连接";
            SidebarDot.Fill = (Brush)FindResource("TextMutedBrush");
            SidebarHintText.Text = $"硬盘未插入，插入后按 {hotkey} 可解锁。";
            SidebarActionGlyph.Text = GlyphRefresh;
            SidebarActionText.Text = "刷新状态";
            SidebarActionButton.IsEnabled = true;
            return;
        }

        if (_lockManager.IsLocked)
        {
            SidebarStateText.Text = "保护中";
            SidebarDot.Fill = (Brush)FindResource("AccentBrush");
            SidebarHintText.Text = $"按 {hotkey} 可随时解锁硬盘";
            SidebarActionGlyph.Text = GlyphUnlock;
            SidebarActionText.Text = "解锁硬盘";
        }
        else
        {
            SidebarStateText.Text = "已解锁";
            SidebarDot.Fill = (Brush)FindResource("WarnBrush");
            SidebarHintText.Text = $"按 {hotkey} 可随时锁定硬盘";
            SidebarActionGlyph.Text = GlyphLock;
            SidebarActionText.Text = "锁定硬盘";
        }
        SidebarActionButton.IsEnabled = true;
    }

    private void SidebarAction_Click(object sender, RoutedEventArgs e)
    {
        var count = _lockManager.Settings.Current.TargetDisks.Count;

        if (count == 0)
        {
            App.Instance.OpenQuickStart(this);
            return;
        }

        if (!_lockManager.IsTargetConnected)
        {
            _lockManager.RefreshConnectionState();
            UpdateStatus();
            RefreshCurrentPage();
            return;
        }

        if (_lockManager.IsLocked) App.Instance.ShowUnlockDialog(this);
        else App.Instance.RequestLock();
    }

    // ===================== 磁盘详情抽屉 =====================

    /// <summary>从右侧滑出磁盘详情抽屉</summary>
    public void OpenDiskDrawer(DiskState disk)
    {
        DrawerContent.Content = new DiskDetailPanel(_lockManager, disk) { CloseRequested = CloseDiskDrawer };
        DrawerScrim.Visibility = Visibility.Visible;
        DrawerHost.Visibility = Visibility.Visible;

        DrawerShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(440, 0,
            TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void CloseDiskDrawer()
    {
        if (DrawerHost.Visibility != Visibility.Visible) return;

        var anim = new DoubleAnimation(0, 440, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            DrawerHost.Visibility = Visibility.Collapsed;
            DrawerScrim.Visibility = Visibility.Collapsed;
            DrawerContent.Content = null;
        };
        DrawerShift.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void DrawerScrim_Click(object sender, MouseButtonEventArgs e) => CloseDiskDrawer();
}

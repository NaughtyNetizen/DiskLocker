using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using DiskLocker.Models;
using DiskLocker.Services;
using DiskLocker.ViewModels;
using DiskLocker.Views;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace DiskLocker;

/// <summary>
/// 应用入口：生命周期、系统托盘、全局协调。
/// 启动不再阻塞 —— 先把后台（设备监听 + 自动锁定 + 热键 + 托盘）跑起来，
/// 首次运行只在托盘提示一次并打开单屏「快速开始」，跳过也能正常使用。
/// </summary>
public partial class App : Application
{
    public static App Instance { get; private set; } = null!;

    public static string AppVersion { get; } =
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    private LockManager? _lockManager;
    private NotifyIcon? _notifyIcon;
    private Mutex? _singleInstanceMutex;
    private MainWindow? _mainWindow;
    private Window? _trayMenuWindow;
    private bool _isShuttingDown;

    private static readonly Icon LockedIcon = CreateLockIcon(true);
    private static readonly Icon UnlockedIcon = CreateLockIcon(false);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    // ===================== 生命周期 =====================

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        Instance = this;

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += App_DomainUnhandledException;
        TaskScheduler.UnobservedTaskException += App_UnobservedTaskException;

        _singleInstanceMutex = new Mutex(true, "DiskLocker_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            // 设计稿：重复启动只提示，不再拉起第二份后台
            MessageBox.Show("DiskLocker 已经在后台运行了，可以在系统托盘里找到它。",
                "DiskLocker", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _lockManager = new LockManager();
        _lockManager.Settings.Load();

        ThemeService.Initialize(_lockManager.Settings.Current.ThemeMode);
        ThemeService.ThemeChanged += OnThemeChanged;

        StartBackgroundMode();

        // 未配置：托盘提示一次 + 打开快速开始；跳过也能先跑起来
        if (!_lockManager.HasProtectionConfig && !_lockManager.Settings.Current.QuickStartPrompted)
        {
            _lockManager.Settings.Current.QuickStartPrompted = true;
            _lockManager.Settings.Save();
            ShowBalloon("硬盘还没有被保护，点一下就能开始。", ToolTipIcon.Info);
            Dispatcher.BeginInvoke(new Action(() => OpenQuickStart(null)),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        _lockManager?.Dispose();
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
    }

    private void OnThemeChanged()
    {
        UpdateTrayIcon();
        if (_mainWindow is { IsLoaded: true })
        {
            _mainWindow.UpdateStatus();
            _mainWindow.RefreshAfterThemeChange();
        }
    }

    // ===================== 全局异常处理 =====================

    private void App_DispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        SimpleLogger.Log($"[FATAL] DispatcherUnhandledException: {e.Exception}");
        try { ToastService.Show(ActivitySeverity.Error, "出错了", e.Exception.Message); } catch { }
        e.Handled = true;
    }

    private void App_DomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        SimpleLogger.Log($"[FATAL] AppDomain.UnhandledException (terminating={e.IsTerminating}): {e.ExceptionObject}");
    }

    private void App_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        SimpleLogger.Log($"[FATAL] UnobservedTaskException: {e.Exception}");
        e.SetObserved();
    }

    // ===================== 后台模式 =====================

    private void StartBackgroundMode()
    {
        if (_lockManager == null) return;

        _lockManager.StateChanged += OnStateChanged;
        _lockManager.RequestUnlockDialog += OnRequestUnlockDialog;
        _lockManager.ErrorOccurred += OnErrorOccurred;
        _lockManager.NotifyUser += OnNotifyUser;

        try
        {
            _lockManager.Initialize();
        }
        catch (Exception ex)
        {
            // 初始化失败也要留下托盘图标，用户才能感知并退出
            SimpleLogger.Log($"[FATAL] Initialize failed: {ex}");
        }

        InitializeTray();
        UpdateTrayIcon();
    }

    // ===================== 系统托盘 =====================

    private void InitializeTray()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = LockedIcon,
            Text = "DiskLocker",
            Visible = true
        };

        // 左键开主窗口，中键呼出解锁，右键自绘菜单
        _notifyIcon.MouseClick += OnTrayMouseClick;
    }

    private void OnTrayMouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        switch (e.Button)
        {
            case System.Windows.Forms.MouseButtons.Right:
                ShowTrayMenu();
                break;
            case System.Windows.Forms.MouseButtons.Left:
                ShowMainWindow();
                break;
            case System.Windows.Forms.MouseButtons.Middle:
                RequestUnlock();
                break;
        }
    }

    /// <summary>托盘右键菜单（设计稿 T1）：顶部状态区 + 全部常用操作</summary>
    private void ShowTrayMenu()
    {
        if (_lockManager == null) return;

        var settings = _lockManager.Settings.Current;
        var count = settings.TargetDisks.Count;
        var locked = _lockManager.IsLocked;
        var hotkey = settings.HotKeyDisplayText;

        string headerTitle, headerSub, accentKey;
        if (count == 0)
        {
            headerTitle = "未设置";
            headerSub = "还没有要保护的硬盘";
            accentKey = "TextMutedBrush";
        }
        else if (!_lockManager.IsTargetConnected)
        {
            headerTitle = "未连接";
            headerSub = $"{count} 块硬盘当前没插入";
            accentKey = "TextMutedBrush";
        }
        else if (locked)
        {
            var letters = _lockManager.GetAllTargetDriveLetters();
            headerTitle = "已锁定";
            headerSub = letters.Count > 0
                ? $"{count} 块硬盘已保护 · 盘符 {DiskState.JoinLetters(letters)}"
                : $"{count} 块硬盘已保护 · 盘符已隐藏";
            accentKey = "AccentBrush";
        }
        else
        {
            headerTitle = "已解锁";
            headerSub = $"{count} 块硬盘可以正常访问";
            accentKey = "WarnBrush";
        }

        var brush = TryFindResource(accentKey) as System.Windows.Media.Brush
                    ?? System.Windows.Media.Brushes.Transparent;

        var paused = _lockManager.IsAutoLockPaused;
        var items = new List<MenuSpec>
        {
            new(locked ? "解锁硬盘" : "锁定硬盘", locked ? "\uE785" : "\uE72E",
                locked ? (Action)RequestUnlock : RequestLock, Hint: hotkey),
            new("打开 DiskLocker", "\uE80F", () => ShowMainWindow()),
            new("查看活动记录", "\uE823", () => ShowMainWindow("Activity")),
            new("开始保护…", "\uE72E", () => OpenQuickStart(_mainWindow),
                Enabled: !_lockManager.HasProtectionConfig),
            new("-", null, null),
            new(paused ? "恢复自动锁定" : "暂停自动锁定", paused ? "\uE768" : "\uE769",
                paused ? _lockManager.ResumeAutoLock
                       : () => _lockManager.PauseAutoLock(TimeSpan.FromHours(1)),
                Hint: paused ? null : "1 小时"),
            new("退出 DiskLocker", "\uE7E8", ExitApp, Hint: "硬盘保持锁定", Danger: true)
        };

        var panel = FlyoutMenu.BuildPanel(items, new MenuHeader(headerTitle, headerSub, brush), 286);

        _trayMenuWindow?.Close();
        _trayMenuWindow = new Window
        {
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize
        };
        _trayMenuWindow.SourceInitialized += (_, _) =>
            Native.ToastExStyle.ApplyNoActivate(_trayMenuWindow!);
        _trayMenuWindow.Closed += (_, _) => _trayMenuWindow = null;

        PositionAtCursor(_trayMenuWindow, 296);
        _trayMenuWindow.Show();

        // 点到别处就收起
        _trayMenuWindow.LostFocus += (_, _) => _trayMenuWindow?.Close();
        _trayMenuWindow.MouseLeftButtonUp += (_, _) => _trayMenuWindow?.Close();
    }

    /// <summary>把浮层窗口摆到鼠标处（物理像素转 WPF 单位），并避免超出屏幕</summary>
    private void PositionAtCursor(Window window, double width)
    {
        var scale = DesktopScale();
        var px = System.Windows.Forms.Cursor.Position;

        var wa = SystemParameters.WorkArea;
        var left = Math.Min(px.X / scale, wa.Right - width - 6);
        var top = Math.Min(px.Y / scale, wa.Bottom - 340);
        window.Left = Math.Max(wa.Left, left);
        window.Top = Math.Max(wa.Top, top);
    }

    /// <summary>用主屏物理宽度与 WPF 逻辑宽度之比换算 DPI 缩放，避免依赖窗口句柄时序</summary>
    private static double DesktopScale()
    {
        try
        {
            var physical = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width
                           ?? (int)SystemParameters.PrimaryScreenWidth;
            var logical = SystemParameters.PrimaryScreenWidth;
            if (logical <= 0) return 1.0;
            var scale = physical / logical;
            return scale > 0.5 ? scale : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private void UpdateTrayIcon()
    {
        if (_notifyIcon == null || _lockManager == null) return;
        // 托盘 Text 上限 63 字符，超了会抛异常
        _notifyIcon.Text = Truncate(_lockManager.GetStatusText(), 63);
        _notifyIcon.Icon = _lockManager.IsLocked ? LockedIcon : UnlockedIcon;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];

    // ===================== LockManager 事件 =====================

    private void OnStateChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateTrayIcon();
            _mainWindow?.UpdateStatus();
        });
    }

    private void OnRequestUnlockDialog() => RequestUnlock();

    private void OnErrorOccurred(string error) =>
        Dispatcher.BeginInvoke(() => ToastService.Show(ActivitySeverity.Warning, "需要留意", error));

    private void OnNotifyUser(ActivitySeverity severity, string title, string detail) =>
        ToastService.Show(severity, title, detail);

    // ===================== 对外操作入口 =====================

    public void ShowMainWindow(string? page = null)
    {
        if (_lockManager == null) return;

        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = new MainWindow(_lockManager);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.Present();
        if (page != null) _mainWindow.ShowPage(page);
    }

    /// <summary>呼出解锁弹窗（托盘 / 热键 / 侧栏按钮）</summary>
    public void RequestUnlock()
    {
        if (_lockManager == null || _isShuttingDown) return;

        if (!_lockManager.HasProtectionConfig)
        {
            OpenQuickStart(_mainWindow);
            return;
        }

        if (!_lockManager.IsLocked)
        {
            ToastService.Show(ActivitySeverity.Info, "硬盘已解锁", "当前不需要输入密码。");
            return;
        }

        ShowUnlockDialog(_mainWindow);
    }

    /// <summary>解锁弹窗；owner 为空时居中置顶</summary>
    public void ShowUnlockDialog(Window? owner)
    {
        if (_lockManager == null) return;
        var dialog = new UnlockDialog(_lockManager)
        {
            Owner = owner,
            Topmost = true
        };
        dialog.ShowDialog();
        UpdateTrayIcon();
    }

    /// <summary>一键锁定全部硬盘（侧栏 / 概览 / 托盘共用）</summary>
    public void RequestLock()
    {
        if (_lockManager == null || _isShuttingDown) return;

        if (!_lockManager.HasProtectionConfig)
        {
            OpenQuickStart(_mainWindow);
            return;
        }

        if (!_lockManager.IsTargetConnected)
        {
            ToastService.Show(ActivitySeverity.Warning, "无法锁定", "目标硬盘当前没有连接到电脑。");
            return;
        }

        if (_lockManager.IsLocked)
        {
            ToastService.Show(ActivitySeverity.Info, "已经在保护中", "硬盘的盘符已经隐藏了。");
            return;
        }

        _lockManager.LockDisk(toast: true);
        UpdateTrayIcon();
    }

    /// <summary>快速开始：非模态，跳过也不影响后台运行</summary>
    public void OpenQuickStart(Window? owner)
    {
        if (_lockManager == null) return;

        var dialog = new QuickStartDialog(_lockManager)
        {
            Owner = owner,
            Topmost = true
        };
        dialog.Completed += () =>
        {
            UpdateTrayIcon();
            _mainWindow?.UpdateStatus();
            _mainWindow?.RefreshAfterThemeChange();
        };
        dialog.Show();
    }

    // ===================== 退出 =====================

    private void ExitApp()
    {
        if (_isShuttingDown) return;

        var confirmed = ConfirmDialog.Show(_mainWindow,
            "退出 DiskLocker？",
            _lockManager?.IsLocked == true
                ? "硬盘当前处于锁定状态。退出后盘符仍然隐藏，需要重新运行并用密码解锁。"
                : "退出后硬盘不再受保护，也不会再自动锁定。",
            "退出 DiskLocker", danger: true);
        if (!confirmed) return;

        _isShuttingDown = true;

        _trayMenuWindow?.Close();
        _mainWindow?.Close();
        _lockManager?.Shutdown();

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        Shutdown(0);
    }

    private void ShowBalloon(string text, ToolTipIcon icon)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _notifyIcon?.ShowBalloonTip(4000, "DiskLocker", text, icon);
        }));
    }

    // ===================== 托盘图标绘制 =====================

    /// <summary>锁形图标：锁定用主色蓝，解锁用注意色橙，与界面同一套语义色</summary>
    private static Icon CreateLockIcon(bool isLocked)
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);

        var color = isLocked
            ? System.Drawing.Color.FromArgb(76, 141, 255)   // #4C8DFF
            : System.Drawing.Color.FromArgb(245, 166, 35);  // #F5A623

        using var bodyBrush = new SolidBrush(color);
        var bodyRect = new Rectangle(7, 13, 18, 16);
        using var bodyPath = CreateRoundedRect(bodyRect, 3);
        g.FillPath(bodyBrush, bodyPath);

        using var pen = new Pen(color, 3.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawArc(pen, 10, 5, 12, 14, 180, 180);

        g.FillEllipse(System.Drawing.Brushes.White, 14, 18, 4, 4);
        g.FillRectangle(System.Drawing.Brushes.White, 15, 21, 2, 5);

        var handle = bitmap.GetHicon();
        try
        {
            // Icon.FromHandle 不接管句柄，Clone 后再销毁原始句柄，避免 GDI 泄漏
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

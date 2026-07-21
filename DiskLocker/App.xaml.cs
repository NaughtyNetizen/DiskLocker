using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using DiskLocker.Models;
using DiskLocker.Services;
using DiskLocker.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace DiskLocker;

/// <summary>
/// 应用程序入口 - 管理生命周期、系统托盘、全局协调
/// </summary>
public partial class App : Application
{
    private LockManager? _lockManager;
    private NotifyIcon? _notifyIcon;
    private Mutex? _singleInstanceMutex;
    private SettingsWindow? _settingsWindow;
    private bool _isShuttingDown;

    // 缓存的图标
    private static readonly Icon LockedIcon = CreateLockIcon(true);
    private static readonly Icon UnlockedIcon = CreateLockIcon(false);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    // ===================== 应用生命周期 =====================

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // 单实例检查
        _singleInstanceMutex = new Mutex(true, "DiskLocker_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("DiskLocker 已经在运行中。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 创建锁管理器并加载设置
        _lockManager = new LockManager();
        _lockManager.Settings.Load();

        // 系统会话结束（注销/关机）时保持锁定状态，不自动解锁

        // 检查是否已配置
        if (!_lockManager.Settings.Current.IsConfigured ||
            string.IsNullOrEmpty(_lockManager.Settings.Current.PasswordHash) ||
            _lockManager.Settings.Current.TargetDisks.Count == 0)
        {
            // 首次运行，显示配置向导
            ShowSetupWizard();
        }
        else
        {
            // 已配置，直接进入后台模式
            StartBackgroundMode();
        }
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _lockManager?.Dispose();
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
    }

    // ===================== 配置向导 =====================

    private void ShowSetupWizard()
    {
        var wizard = new SetupWizard();
        wizard.OnConfigured = (disks, password, timeout, lockOnScreenLock, lockOnSleep, autoStart) =>
        {
            // 保存配置
            var settings = _lockManager!.Settings.Current;
            settings.IsConfigured = true;
            settings.TargetDisks = disks.Select(d => new TargetDiskEntry
            {
                Model = d.Model,
                SerialNumber = d.SerialNumber,
                PnpDeviceId = d.PnpDeviceId
            }).ToList();
            settings.PasswordHash = _lockManager.Password.HashPassword(password);
            settings.IdleLockTimeoutMinutes = timeout;
            settings.LockOnScreenLock = lockOnScreenLock;
            settings.LockOnSleep = lockOnSleep;
            settings.AutoStartWithWindows = autoStart;
            _lockManager.Settings.Save();

            // 设置开机自启
            _lockManager.AutoStart.SetAutoStart(autoStart);

            // 进入后台模式
            StartBackgroundMode();
        };

        wizard.ShowDialog();

        // 用户取消配置向导则退出
        if (wizard.DialogResult != true && _lockManager?.Settings.Current.IsConfigured != true)
        {
            Shutdown();
        }
    }

    // ===================== 后台模式启动 =====================

    private void StartBackgroundMode()
    {
        if (_lockManager == null) return;

        // 绑定事件
        _lockManager.StateChanged += OnStateChanged;
        _lockManager.RequestUnlockDialog += OnRequestUnlockDialog;
        _lockManager.ErrorOccurred += OnErrorOccurred;

        // 初始化所有服务（设备监听、自动锁定、全局热键）
        _lockManager.Initialize();

        // 创建系统托盘
        InitializeTray();
        UpdateTrayIcon();

        // 静默启动，不显示主窗口
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

        // 右键菜单
        var menu = new ContextMenuStrip();
        menu.Items.Add("解锁硬盘", null, OnTrayUnlock);
        menu.Items.Add("锁定硬盘", null, OnTrayLock);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("打开设置", null, OnTraySettings);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出程序", null, OnTrayExit);

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (s, e) => OnTrayUnlock(s, e);
    }

    private void UpdateTrayIcon()
    {
        if (_notifyIcon == null || _lockManager == null) return;

        _notifyIcon.Text = _lockManager.GetStatusText();
        _notifyIcon.Icon = _lockManager.IsLocked ? LockedIcon : UnlockedIcon;
    }

    // ===================== 托盘菜单事件 =====================

    private void OnTrayUnlock(object? sender, EventArgs e)
    {
        ShowUnlockDialog();
    }

    private void OnTrayLock(object? sender, EventArgs e)
    {
        if (_lockManager == null) return;

        if (!_lockManager.IsTargetConnected)
        {
            ShowBalloon("目标硬盘未连接", ToolTipIcon.Warning);
            return;
        }

        if (_lockManager.IsLocked)
        {
            ShowBalloon("硬盘已处于锁定状态", ToolTipIcon.Info);
            return;
        }

        if (_lockManager.LockDisk())
        {
            ShowBalloon("硬盘已锁定，盘符已隐藏", ToolTipIcon.Info);
        }
    }

    private void OnTraySettings(object? sender, EventArgs e)
    {
        ShowSettingsWindow();
    }

    private void OnTrayExit(object? sender, EventArgs e)
    {
        ExitApp();
    }

    // ===================== LockManager 事件 =====================

    private void OnStateChanged()
    {
        UpdateTrayIcon();

        // 如果设置窗口打开，更新其状态
        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.Dispatcher.Invoke(() => _settingsWindow.UpdateStatus());
        }
    }

    private void OnRequestUnlockDialog()
    {
        ShowUnlockDialog();
    }

    private void OnErrorOccurred(string error)
    {
        ShowBalloon(error, ToolTipIcon.Warning);
    }

    // ===================== 对话框 =====================

    private void ShowUnlockDialog()
    {
        if (_lockManager == null || _isShuttingDown) return;

        if (!_lockManager.IsTargetConnected)
        {
            ShowBalloon("目标硬盘未连接", ToolTipIcon.Warning);
            return;
        }

        if (!_lockManager.IsLocked)
        {
            ShowBalloon("硬盘已解锁，无需操作", ToolTipIcon.Info);
            return;
        }

        // 显示密码对话框
        var dialog = new PasswordDialog(_lockManager)
        {
            Topmost = true
        };
        dialog.ShowDialog();

        UpdateTrayIcon();
    }

    private void ShowSettingsWindow()
    {
        if (_lockManager == null) return;

        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_lockManager);
            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
        _settingsWindow.UpdateStatus();
    }

    // ===================== 退出 =====================

    private void ExitApp()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        // 关闭设置窗口
        _settingsWindow?.Close();

        // 关闭所有服务并恢复磁盘
        _lockManager?.Shutdown();

        // 隐藏托盘图标
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        // 退出应用
        Shutdown(0);
    }

    // ===================== 辅助方法 =====================

    private void ShowBalloon(string text, ToolTipIcon icon)
    {
        _notifyIcon?.ShowBalloonTip(3000, "DiskLocker", text, icon);
    }

    /// <summary>
    /// 绘制锁形图标（红色=已锁定，蓝色=已解锁）
    /// </summary>
    private static Icon CreateLockIcon(bool isLocked)
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // 锁体颜色：锁定=红色，解锁=蓝色
        var color = isLocked
            ? Color.FromArgb(208, 52, 56)
            : Color.FromArgb(0, 120, 215);

        // 锁体（圆角矩形）
        using var bodyBrush = new SolidBrush(color);
        var bodyRect = new Rectangle(7, 13, 18, 16);
        using var bodyPath = CreateRoundedRect(bodyRect, 3);
        g.FillPath(bodyBrush, bodyPath);

        // 锁扣（弧形）
        using var pen = new Pen(color, 3.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawArc(pen, 10, 5, 12, 14, 180, 180);

        // 钥匙孔
        g.FillEllipse(Brushes.White, 14, 18, 4, 4);
        g.FillRectangle(Brushes.White, 15, 21, 2, 5);

        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    /// <summary>
    /// 创建圆角矩形路径
    /// </summary>
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

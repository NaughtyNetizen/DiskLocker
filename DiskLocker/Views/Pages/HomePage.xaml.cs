using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskLocker.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;

namespace DiskLocker.Views.Pages;

/// <summary>
/// 首页 - 保护状态总览与一键锁定/解锁
/// </summary>
public partial class HomePage : UserControl
{
    private readonly LockManager _lockManager;
    private readonly DiskService _diskService;

    /// <summary>请求跳转到其他页面（由导航壳 SettingsWindow 处理）</summary>
    public event Action<string>? NavigateRequested;

    public HomePage(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;
        _diskService = lockManager.Disk;
    }

    /// <summary>刷新状态总览、信息行与磁盘概览</summary>
    public void Refresh()
    {
        UpdateStatus();
        LoadDiskOverview();
    }

    /// <summary>更新锁定状态大卡片与信息行</summary>
    private void UpdateStatus()
    {
        var settings = _lockManager.Settings.Current;
        var isConnected = _lockManager.IsTargetConnected;
        var isLocked = _lockManager.IsLocked;

        if (isLocked)
        {
            StatusIconCircle.Background = (Brush)FindResource("DangerLightBrush");
            StatusIconGlyph.Text = "\uE72E"; // 锁
            StatusIconGlyph.Foreground = (Brush)FindResource("DangerBrush");
            LockStateText.Text = "已锁定";
            LockStateSubText.Text = "硬盘已藏好，别人看不到";
        }
        else
        {
            StatusIconCircle.Background = (Brush)FindResource("AccentLightBrush");
            StatusIconGlyph.Text = "\uE785"; // 解锁
            StatusIconGlyph.Foreground = (Brush)FindResource("AccentBrush");
            LockStateText.Text = "已解锁";
            LockStateSubText.Text = "硬盘可以正常使用";
        }

        ConnectionDot.Fill = isConnected
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("TextTertiaryBrush");
        ConnectionStatusText.Text = isConnected ? "已连接" : "未连接";
        ConnectionStatusText.Foreground = isConnected
            ? (Brush)FindResource("TextPrimaryBrush")
            : (Brush)FindResource("DangerBrush");

        if (isConnected)
        {
            var letters = _lockManager.GetAllTargetDriveLetters();
            DriveLettersText.Text = letters.Count > 0 ? string.Join("、", letters) : "已隐藏";
        }
        else
        {
            DriveLettersText.Text = "--";
        }

        TargetCountText.Text = $"{settings.TargetDisks.Count} 台";

        // 按钮状态：锁定时始终允许尝试解锁（即使连接状态瞬间为 false）
        LockButton.IsEnabled = isConnected && !isLocked;
        UnlockButton.IsEnabled = isLocked;
    }

    /// <summary>加载受保护磁盘概览行（型号 + 盘符/未连接徽章）</summary>
    private void LoadDiskOverview()
    {
        var settings = _lockManager.Settings.Current;
        var allDisks = _diskService.GetAllPhysicalDisks();
        var isLocked = _lockManager.IsLocked;

        DiskListPanel.Children.Clear();

        var hasTargets = settings.TargetDisks.Count > 0;
        DiskListHeader.Visibility = hasTargets ? Visibility.Visible : Visibility.Collapsed;
        DiskListPanel.Visibility = hasTargets ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = hasTargets ? Visibility.Collapsed : Visibility.Visible;

        for (int i = 0; i < settings.TargetDisks.Count; i++)
        {
            var target = settings.TargetDisks[i];
            var actual = allDisks.FirstOrDefault(d =>
                string.Equals(d.SerialNumber, target.SerialNumber, StringComparison.OrdinalIgnoreCase));
            var connected = actual != null;

            string stateText;
            Brush stateBrush;
            if (!connected)
            {
                stateText = "未连接";
                stateBrush = (Brush)FindResource("TextTertiaryBrush");
            }
            else if (isLocked)
            {
                stateText = "已锁定";
                stateBrush = (Brush)FindResource("DangerBrush");
            }
            else
            {
                var letters = _diskService.GetDriveLettersForDisk(target.SerialNumber);
                stateText = letters.Count > 0 ? string.Join("、", letters) : "无盘符";
                stateBrush = (Brush)FindResource("SuccessBrush");
            }

            var row = new Border { Style = (Style)FindResource("HomeDiskRow") };
            if (i == settings.TargetDisks.Count - 1)
                row.BorderThickness = new Thickness(0);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var namePanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var icon = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(7),
                Background = connected
                    ? (Brush)FindResource("AccentLightBrush")
                    : (Brush)FindResource("SubtleBrush"),
                Child = new TextBlock
                {
                    Text = "\uEDA2",
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 14,
                    Foreground = connected
                        ? (Brush)FindResource("AccentBrush")
                        : (Brush)FindResource("TextSecondaryBrush"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            namePanel.Children.Add(icon);

            var nameText = new TextBlock
            {
                Text = string.IsNullOrEmpty(target.Model) ? "未知设备" : target.Model,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            namePanel.Children.Add(nameText);
            Grid.SetColumn(namePanel, 0);
            grid.Children.Add(namePanel);

            var stateText2 = new TextBlock
            {
                Text = stateText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = stateBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(stateText2, 1);
            grid.Children.Add(stateText2);

            row.Child = grid;
            DiskListPanel.Children.Add(row);
        }
    }

    // ===================== 操作 =====================

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lockManager.LockDisk())
        {
            ShowFeedback("硬盘已锁定，盘符已隐藏", true);
            Refresh();
        }
        else
        {
            ShowFeedback("锁定失败，请检查硬盘是否插好", false);
            Refresh();
        }
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordDialog(_lockManager) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
        Refresh();
    }

    private void GoAddDiskButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateRequested?.Invoke("Disks");
    }

    /// <summary>显示操作反馈条（3 秒后自动隐藏）</summary>
    private void ShowFeedback(string message, bool success)
    {
        FeedbackBar.Style = (Style)FindResource(success ? "InfoBarSuccess" : "InfoBarError");
        FeedbackText.Text = message;
        FeedbackText.Foreground = (Brush)FindResource(success ? "SuccessBrush" : "DangerBrush");
        FeedbackBar.Visibility = Visibility.Visible;

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (s, e) =>
        {
            FeedbackBar.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskLocker.Services;
using DiskLocker.ViewModels;
using MessageBox = System.Windows.MessageBox;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;

namespace DiskLocker.Views.Pages;

/// <summary>
/// 磁盘管理页 - 已保护硬盘与可添加硬盘的增删
/// </summary>
public partial class DisksPage : UserControl
{
    private readonly LockManager _lockManager;
    private readonly DiskService _diskService;

    public DisksPage(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;
        _diskService = lockManager.Disk;
    }

    /// <summary>刷新磁盘卡片列表</summary>
    public void Refresh()
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

    // ===================== 磁盘卡片操作 =====================

    private void AddDiskCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DiskCardModel card } || card.Disk == null)
            return;

        if (_lockManager.IsLocked)
        {
            var result = MessageBox.Show(
                "添加新硬盘后会重新锁定所有硬盘（包括新加的这块）。要继续吗？",
                "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;
        }

        _lockManager.AddTargetDisk(card.Disk);
        Refresh();
        ShowFeedback($"已添加保护：{card.Model}", true);
    }

    private void RemoveDiskCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DiskCardModel card })
            return;

        var result = MessageBox.Show(
            $"确定不再保护这块硬盘吗？\n\n{card.Model}\nS/N: {card.SerialNumber}\n\n移除后它会恢复成普通硬盘。",
            "确认移除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        _lockManager.RemoveTargetDisk(card.SerialNumber);
        Refresh();
        ShowFeedback("已移除保护", true);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        Refresh();
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

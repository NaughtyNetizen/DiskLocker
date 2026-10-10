using System.Windows;
using System.Windows.Controls;
using DiskLocker.Services;
using DiskLocker.ViewModels;

namespace DiskLocker.Views;

/// <summary>
/// 磁盘详情抽屉内容（设计稿 T4）：完整硬件信息 + 单盘解锁 / 复制序列号 / 移除保护。
/// </summary>
public partial class DiskDetailPanel : UserControl
{
    private readonly LockManager _lockManager;
    private readonly DiskState _disk;

    /// <summary>请求关闭抽屉</summary>
    public Action? CloseRequested { get; set; }

    public DiskDetailPanel(LockManager lockManager, DiskState disk)
    {
        InitializeComponent();
        _lockManager = lockManager;
        _disk = disk;
        DataContext = disk;

        TitleText.Text = disk.Model;
        SubtitleText.Text = $"{(string.IsNullOrEmpty(disk.SizeDisplay) ? "容量未知" : disk.SizeDisplay)} · " +
                            $"{(string.IsNullOrEmpty(disk.InterfaceType) ? "外接硬盘" : disk.InterfaceType + " 外接硬盘")}";
        StatusGlyph.Text = disk.State == DiskDisplayState.Locked ? "\uE72E" : "\uE785";
        StatusText.Text = disk.StatusText;
        StatusDesc.Text = disk.State switch
        {
            DiskDisplayState.Locked => "盘符已隐藏，解锁后可恢复访问。",
            DiskDisplayState.Unlocked => "盘符正常，可以直接打开查看文件。",
            DiskDisplayState.Disconnected => "这块硬盘当前没有连接到电脑。",
            _ => "这块硬盘还没有纳入保护。"
        };

        ModelValue.Text = disk.Model;
        SerialValue.Text = string.IsNullOrEmpty(disk.SerialNumber) ? "未知" : disk.SerialNumber;
        InterfaceValue.Text = disk.InterfaceLine;
        PartitionValue.Text = disk.PartitionLine;

        UnlockBtn.IsEnabled = disk.State == DiskDisplayState.Locked;
        UnlockBtn.Content = disk.IsProtected ? "立即解锁" : "添加保护";
        RemoveBtn.Visibility = disk.IsProtected ? Visibility.Visible : Visibility.Collapsed;
        FooterHint.Text = disk.IsProtected
            ? "锁定状态会被记住：电脑重启后这块硬盘仍保持隐藏。"
            : "加入保护后，锁定硬盘时这块盘的盘符会从系统中隐藏。";
    }

    private void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (_disk.IsProtected)
        {
            DiskActions.UnlockOne(_lockManager, _disk, Window.GetWindow(this));
            CloseRequested?.Invoke();
            return;
        }

        DiskActions.Add(_lockManager, _disk, Window.GetWindow(this));
        CloseRequested?.Invoke();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => DiskActions.CopySerial(_disk);

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        DiskActions.Remove(_lockManager, _disk, Window.GetWindow(this));
        CloseRequested?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}

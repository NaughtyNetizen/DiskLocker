using System.Diagnostics;
using System.Windows;
using DiskLocker.Services;
using DiskLocker.ViewModels;

namespace DiskLocker.Views;

/// <summary>
/// 硬盘相关的共享操作，概览页 / 硬盘管理页 / 详情抽屉 / 托盘都走这里，
/// 避免同一套确认与解锁逻辑散落多处。
/// </summary>
public static class DiskActions
{
    /// <summary>硬盘卡「更多」下拉菜单（设计稿 T2）</summary>
    public static void ShowMoreMenu(FrameworkElement anchor, LockManager lm, DiskState disk,
                                    Window? owner, Action<DiskState>? openDrawer = null)
    {
        var canOpen = disk.DriveLetters.Count > 0;
        var showDetails = openDrawer ?? (d => MainWindowOf(owner)?.OpenDiskDrawer(d));

        var items = new List<MenuSpec>
        {
            new("查看详情", "\uE897", () => showDetails(disk)),
            new("立即解锁这块硬盘", "\uE785", () => UnlockOne(lm, disk, owner),
                Enabled: disk.State == DiskDisplayState.Locked),
            new("在资源管理器中打开", "\uE838", () => OpenInExplorer(disk), Enabled: canOpen),
            new("复制序列号", "\uE8C8", () => CopySerial(disk)),
            new("-", null, null),
            new("移除保护…", "\uE74D", () => Remove(lm, disk, owner), Danger: true,
                Enabled: disk.IsProtected)
        };

        FlyoutMenu.Show(anchor, items, 246);
    }

    public static void Add(LockManager lm, DiskState disk, Window? owner)
    {
        if (disk.IsSystemDisk)
        {
            MessageBox.Show(owner, "这是系统盘，锁住它电脑就开不了机，不能加入保护。",
                "不能保护这块硬盘", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (lm.IsLocked)
        {
            var confirmed = ConfirmDialog.Show(owner,
                "添加保护会先解锁全部硬盘？",
                $"加入 {disk.Model} 后，当前锁定的硬盘会先恢复盘符，再连同这块新硬盘一起重新锁定。",
                "继续添加", danger: false);
            if (!confirmed) return;
        }

        lm.AddTargetDisk(new Models.DiskInfo
        {
            Model = disk.Model,
            SerialNumber = disk.SerialNumber,
            PnpDeviceId = disk.PnpDeviceId
        }, toast: true);
    }

    /// <summary>移除保护（危险操作，先说清后果 —— 设计稿 D3）</summary>
    public static void Remove(LockManager lm, DiskState disk, Window? owner)
    {
        var lockedNote = disk.State == DiskDisplayState.Locked
            ? "它当前处于锁定状态，移除前会先解锁并恢复盘符。"
            : "它当前可以正常访问。";

        var confirmed = ConfirmDialog.Show(owner,
            "移除这个硬盘的保护？",
            $"移除后，{disk.Model} 会恢复成普通硬盘，不再受密码保护。{lockedNote}",
            "移除保护", danger: true);
        if (!confirmed) return;

        lm.RemoveTargetDisk(disk.SerialNumber, toast: true);
    }

    /// <summary>只解锁这一块硬盘，其余保持锁定</summary>
    public static void UnlockOne(LockManager lm, DiskState disk, Window? owner)
    {
        var dialog = new UnlockDialog(lm, disk.SerialNumber) { Owner = owner };
        dialog.ShowDialog();
    }

    public static void OpenInExplorer(DiskState disk)
    {
        var letter = disk.DriveLetters.FirstOrDefault();
        if (string.IsNullOrEmpty(letter)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"{DiskState.LetterText(letter)}\\") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"OpenInExplorer failed: {ex.Message}");
        }
    }

    public static void CopySerial(DiskState disk)
    {
        try
        {
            if (!string.IsNullOrEmpty(disk.SerialNumber))
                Clipboard.SetText(disk.SerialNumber);
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"CopySerial failed: {ex.Message}");
        }
    }

    private static MainWindow? MainWindowOf(Window? owner) => owner as MainWindow ??
        Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
}

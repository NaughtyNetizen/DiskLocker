using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiskLocker.Models;
using DiskLocker.Services;
using DiskLocker.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DiskLocker.Views;

/// <summary>
/// 快速开始弹窗（设计稿 D6）：取代原 4 步向导，选盘 + 设密码单屏完成，行为偏好按推荐值预填。
/// </summary>
public partial class QuickStartDialog : Window
{
    private readonly LockManager _lockManager;
    private readonly List<(DiskState Disk, CheckBox Box)> _rows = new();
    private bool _reveal;

    /// <summary>用户完成配置后触发（非模态显示，不能用 DialogResult）</summary>
    public event Action? Completed;

    public QuickStartDialog(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;

        Loaded += (_, _) =>
        {
            LoadDisks();
            Pwd.Focus();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    // ===================== 硬盘列表 =====================

    private void LoadDisks()
    {
        DiskPanel.Children.Clear();
        _rows.Clear();

        var candidates = _lockManager.GetDiskStates()
            .Where(s => !s.IsSystemDisk && !s.IsProtected)
            .OrderByDescending(s => s.IsConnected)
            .ToList();

        // 已经保护过的盘也允许保留在选中状态，避免重进弹窗时丢选择
        candidates.AddRange(_lockManager.GetDiskStates().Where(s => s.IsProtected && !s.IsSystemDisk));

        foreach (var disk in candidates)
        {
            var box = new CheckBox
            {
                Style = (Style)FindResource("CheckRow"),
                Margin = new Thickness(0, 0, 0, 8),
                IsChecked = disk.IsProtected,
                Content = $"{disk.Model} · {(string.IsNullOrEmpty(disk.SizeDisplay) ? "容量未知" : disk.SizeDisplay)}",
                Tag = disk
            };
            box.Checked += (_, _) => UpdateSummary();
            box.Unchecked += (_, _) => UpdateSummary();

            DiskPanel.Children.Add(box);
            _rows.Add((disk, box));
        }

        DiskEmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummary();
    }

    private List<DiskState> Selected() => _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Disk).ToList();

    private void UpdateSummary()
    {
        var picked = Selected();
        if (picked.Count == 0)
        {
            SelectionSummary.Text = "还没有选择硬盘";
            return;
        }

        var physical = _lockManager.Disk.GetAllPhysicalDisks();
        ulong total = 0;
        foreach (var p in picked)
        {
            var raw = physical.FirstOrDefault(d =>
                string.Equals(d.SerialNumber, p.SerialNumber, StringComparison.OrdinalIgnoreCase));
            if (raw != null) total += raw.Size;
        }

        SelectionSummary.Text = $"已选择 {picked.Count} 块硬盘{(total > 0 ? "，合计 " + FormatSize(total) : "")}";
    }

    private static string FormatSize(ulong bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double v = bytes;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {units[i]}";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadDisks();

    // ===================== 密码可见切换 =====================

    private void Eye_Click(object sender, RoutedEventArgs e)
    {
        _reveal = !_reveal;
        if (_reveal)
        {
            PwdVisible.Text = Pwd.Password;
            PwdVisible.Visibility = Visibility.Visible;
            Pwd.Visibility = Visibility.Collapsed;
            EyeBtn.Content = "\uE7B4";
            PwdVisible.Focus();
        }
        else
        {
            Pwd.Password = PwdVisible.Text;
            PwdVisible.Clear();
            PwdVisible.Visibility = Visibility.Collapsed;
            Pwd.Visibility = Visibility.Visible;
            EyeBtn.Content = "\uE7B3";
            Pwd.Focus();
        }
    }

    private void Pwd_Changed(object sender, RoutedEventArgs e) => HideError();
    private void PwdVisible_TextChanged(object sender, TextChangedEventArgs e) => HideError();

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    // ===================== 提交 =====================

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        var picked = Selected();
        if (picked.Count == 0)
        {
            ShowError("请至少勾选一块要保护的硬盘。");
            return;
        }

        var password = _reveal ? PwdVisible.Text : Pwd.Password;
        if (!PasswordService.ValidatePasswordStrength(password, out var error))
        {
            ShowError(error);
            Pwd.Focus();
            return;
        }

        var infos = picked
            .Select(p => _lockManager.Disk.GetAllPhysicalDisks()
                .FirstOrDefault(d => string.Equals(d.SerialNumber, p.SerialNumber, StringComparison.OrdinalIgnoreCase))
                ?? new DiskInfo { Model = p.Model, SerialNumber = p.SerialNumber, PnpDeviceId = p.PnpDeviceId })
            .ToList();

        _lockManager.ApplyProtection(infos, password, toast: true);
        Completed?.Invoke();
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();
}

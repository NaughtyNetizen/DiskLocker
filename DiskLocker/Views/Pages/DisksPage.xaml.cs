using System.Windows;
using System.Windows.Controls;
using DiskLocker.Services;
using DiskLocker.ViewModels;

namespace DiskLocker.Views.Pages;

/// <summary>
/// 硬盘管理页 - 搜索过滤 + 卡片/列表双视图 + 单盘解锁与移除 + 待添加引导
/// </summary>
public partial class DisksPage : UserControl
{
    private readonly LockManager _lockManager;

    private List<DiskState> _protected = new();
    private List<DiskState> _available = new();

    /// <summary>请求打开磁盘详情抽屉</summary>
    public event Action<DiskState>? DrawerRequested;

    public DisksPage(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;
        SearchBox.Tag = "搜索硬盘型号或序列号";
    }

    public void Refresh()
    {
        var states = _lockManager.GetDiskStates();
        _protected = states.Where(s => s.IsProtected).ToList();
        _available = states.Where(s => !s.IsProtected && !s.IsSystemDisk)
            .OrderByDescending(s => s.IsConnected)
            .ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = SearchBox.Text?.Trim() ?? string.Empty;

        List<DiskState> Filter(IEnumerable<DiskState> source) =>
            string.IsNullOrEmpty(q)
                ? source.ToList()
                : source.Where(s =>
                    Contains(s.Model, q) || Contains(s.SerialNumber, q) || Contains(s.SizeDisplay, q)).ToList();

        var protectedList = Filter(_protected);
        var availableList = Filter(_available);

        ProtectedCards.ItemsSource = protectedList;
        ProtectedRows.ItemsSource = protectedList;
        AvailableCards.ItemsSource = availableList;
        AvailableRows.ItemsSource = availableList;

        var lockedCount = _protected.Count(p => p.State == DiskDisplayState.Locked);
        ProtectedPillText.Text = _protected.Count switch
        {
            0 => "0 块",
            var n when lockedCount == n => $"{n} 块 · 全部已锁定",
            var n when lockedCount == 0 => $"{n} 块 · 全部已解锁",
            var n => $"{n} 块 · {lockedCount} 块已锁定"
        };

        NoProtectedText.Visibility = _protected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        AvailableHint.Text = _available.Count > 0
            ? $"已检测到 {_available.Count} 块未保护的硬盘"
            : "没有可添加的硬盘";
        NoAvailableText.Visibility = _available.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateViewVisibility();
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack != null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private void UpdateViewVisibility()
    {
        var cards = CardViewToggle.IsChecked == true;
        ProtectedCards.Visibility = cards ? Visibility.Visible : Visibility.Collapsed;
        AvailableCards.Visibility = cards ? Visibility.Visible : Visibility.Collapsed;

        ListHeader.Visibility = cards ? Visibility.Collapsed : Visibility.Visible;
        ListBody.Visibility = cards ? Visibility.Collapsed : Visibility.Visible;
        AvailableListBody.Visibility = cards ? Visibility.Collapsed : Visibility.Visible;
    }

    // ===================== 交互 =====================

    private void View_Toggled(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) UpdateViewVisibility();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) ApplyFilter();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DiskState disk })
            DrawerRequested?.Invoke(disk);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DiskState disk })
            DiskActions.ShowMoreMenu((Button)sender, _lockManager, disk, Window.GetWindow(this),
                d => DrawerRequested?.Invoke(d));
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DiskState disk })
        {
            DiskActions.Add(_lockManager, disk, Window.GetWindow(this));
            Refresh();
        }
    }
}

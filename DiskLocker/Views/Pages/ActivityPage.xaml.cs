using System.Windows;
using System.Windows.Controls;
using DiskLocker.Models;
using DiskLocker.Services;
using Microsoft.Win32;

namespace DiskLocker.Views.Pages;

/// <summary>
/// 活动记录页 - 事件时间线、类型与时间筛选、统计卡与导出
/// </summary>
public partial class ActivityPage : UserControl
{
    private readonly LockManager _lockManager;
    private List<ActivityEvent> _all = new();
    private bool _loading;

    public ActivityPage(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;
        BuildFilters();
    }

    private void BuildFilters()
    {
        _loading = true;

        KindFilter.Items.Add(new ComboBoxItem { Content = "全部事件", Tag = (ActivityKind?)null, IsSelected = true });
        foreach (var kind in new[]
                 {
                     ActivityKind.Lock, ActivityKind.Unlock, ActivityKind.AutoLock,
                     ActivityKind.WrongPassword, ActivityKind.Device,
                     ActivityKind.Settings, ActivityKind.Failure
                 })
        {
            KindFilter.Items.Add(new ComboBoxItem
            {
                Content = ActivityEvent.KindLabel(kind),
                Tag = (ActivityKind?)kind
            });
        }

        RangeFilter.Items.Add(new ComboBoxItem { Content = "最近 7 天", Tag = 7 });
        RangeFilter.Items.Add(new ComboBoxItem { Content = "最近 30 天", Tag = 30 });
        RangeFilter.Items.Add(new ComboBoxItem { Content = "今天", Tag = 1 });
        RangeFilter.Items.Add(new ComboBoxItem { Content = "全部时间", Tag = (int?)null });

        // 未挂到可视树时给 ComboBoxItem 设 IsSelected 不生效，这里显式选中首项
        KindFilter.SelectedIndex = 0;
        RangeFilter.SelectedIndex = 0;

        _loading = false;
    }

    public void Refresh()
    {
        _all = _lockManager.Events.LoadAll();

        StatToday.Text = _all.Count(e => e.Time.Date == DateTime.Today).ToString();
        StatAutoLock.Text = _all.Count(e =>
            e.Kind == ActivityKind.AutoLock && e.Time >= DateTime.Now.AddDays(-7)).ToString();
        var wrong = _all.Count(e =>
            e.Kind == ActivityKind.WrongPassword && e.Time >= DateTime.Now.AddDays(-7));
        StatWrongPassword.Text = wrong.ToString();

        RiskPillText.Text = $"近 7 天有 {wrong} 次密码错误";
        RiskPill.Visibility = wrong > 0 ? Visibility.Visible : Visibility.Collapsed;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var kind = (KindFilter.SelectedItem as ComboBoxItem)?.Tag as ActivityKind?;
        var days = (RangeFilter.SelectedItem as ComboBoxItem)?.Tag as int?;

        var cutoff = days.HasValue ? DateTime.Now.AddDays(-days.Value) : DateTime.MinValue;
        var rows = _all
            .Where(e => (kind == null || e.Kind == kind) && e.Time >= cutoff)
            .ToList();

        EventList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        ApplyFilter();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var rows = (EventList.ItemsSource as List<ActivityEvent>) ?? _all;
        if (rows.Count == 0)
        {
            ToastService.Show(this, ActivitySeverity.Info, "没有可导出的记录", "换个筛选条件试试。");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出活动记录",
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"DiskLocker-活动记录-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        if (_lockManager.Events.ExportCsv(rows, dialog.FileName))
            ToastService.Show(this, ActivitySeverity.Success, "记录已导出", dialog.FileName);
        else
            ToastService.Show(this, ActivitySeverity.Error, "导出失败", "目标文件可能正被其他程序占用。");
    }
}

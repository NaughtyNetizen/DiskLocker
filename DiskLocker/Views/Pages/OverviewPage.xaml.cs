using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskLocker.Services;
using DiskLocker.ViewModels;

namespace DiskLocker.Views.Pages;

/// <summary>
/// 概览页 - 状态主卡（锁定 / 解锁两态）+ 一键锁定解锁 + 保护概览 + 硬盘列表 + 未保护引导
/// </summary>
public partial class OverviewPage : UserControl
{
    // Segoe Fluent Icons 字形，用转义写以免源码里的私有区字符被编辑器吞掉
    private const string GlyphLock = "\uE72E";
    private const string GlyphUnlock = "\uE785";
    private const string GlyphAdd = "\uE710";

    private readonly LockManager _lockManager;

    private List<DiskState> _protectedDisks = new();
    private List<DiskState> _unprotectedDisks = new();

    /// <summary>请求切换主窗口页面（Disks / Settings）</summary>
    public event Action<string>? NavigateRequested;

    public OverviewPage(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;
    }

    public void Refresh()
    {
        var states = _lockManager.GetDiskStates();
        _protectedDisks = states.Where(s => s.IsProtected).ToList();
        _unprotectedDisks = states
            .Where(s => !s.IsProtected && !s.IsSystemDisk)
            .OrderByDescending(s => s.IsConnected)
            .ToList();

        var configured = _lockManager.HasProtectionConfig;
        var locked = _lockManager.IsLocked;
        var connected = _lockManager.IsTargetConnected;
        var count = _protectedDisks.Count;

        // ---------- 引导条：未配置时一直引导，配置后消失 ----------
        QuickStartBanner.Visibility = configured ? Visibility.Collapsed : Visibility.Visible;

        // ---------- 页头 ----------
        PageSubtitle.Text = count switch
        {
            0 => "还没有添加要保护的硬盘。",
            _ when !connected => $"{count} 块硬盘在保护名单里，当前都没连接。",
            _ when locked => $"{count} 块硬盘处于锁定状态，盘符已从系统中隐藏。",
            _ => "硬盘已解锁，盘符可正常访问，当前未处于保护状态。"
        };

        string pillKey, pillText, pillGlyph;
        if (!configured)
        {
            pillKey = "Neutral"; pillText = "未保护"; pillGlyph = GlyphLock;
        }
        else if (locked)
        {
            pillKey = "Accent"; pillText = "已锁定"; pillGlyph = GlyphLock;
        }
        else
        {
            pillKey = "Warn"; pillText = "已解锁"; pillGlyph = GlyphUnlock;
        }

        HeaderPill.Background = FindBrush($"Pill{pillKey}BgBrush");
        HeaderPill.BorderBrush = FindBrush($"Pill{pillKey}BorderBrush");
        HeaderPillText.Text = pillText;
        HeaderPillText.Foreground = FindBrush($"Pill{pillKey}TextBrush");
        HeaderPillGlyph.Text = pillGlyph;
        HeaderPillGlyph.Foreground = FindBrush($"Pill{pillKey}TextBrush");

        // ---------- 状态主卡 ----------
        if (!configured)
        {
            HeroCircle.Background = FindBrush("PillNeutralBgBrush");
            HeroCircle.BorderBrush = FindBrush("PillNeutralBorderBrush");
            HeroGlyph.Text = GlyphLock;
            HeroGlyph.Foreground = FindBrush("TextTertiaryBrush");
            HeroTitle.Text = "未保护";
            HeroDesc.Text = _unprotectedDisks.Count > 0
                ? "检测到可以保护的硬盘，选硬盘、设密码两步就能上锁。"
                : "插上移动硬盘后就能开始保护；锁定时盘符会从系统中隐藏。";
            HeroPrimaryGlyph.Text = GlyphAdd;
            HeroPrimaryText.Text = "开始保护";
            HeroPrimaryButton.IsEnabled = true;
            HeroSecondaryText.Text = "行为设置";
        }
        else if (locked)
        {
            HeroCircle.Background = FindBrush("PillAccentBgBrush");
            HeroCircle.BorderBrush = FindBrush("PillAccentBorderBrush");
            HeroGlyph.Text = GlyphLock;
            HeroGlyph.Foreground = FindBrush("AccentBrush");
            HeroTitle.Text = "已锁定";
            HeroDesc.Text = connected
                ? $"{count} 块硬盘的盘符已从系统中隐藏，插入电脑也不会自动显示内容。"
                : "硬盘当前未连接，插入后仍会保持隐藏。";
            HeroPrimaryGlyph.Text = GlyphUnlock;
            HeroPrimaryText.Text = "解锁全部硬盘";
            HeroPrimaryButton.IsEnabled = connected;
            HeroSecondaryText.Text = "锁定设置";
        }
        else
        {
            HeroCircle.Background = FindBrush("PillWarnBgBrush");
            HeroCircle.BorderBrush = FindBrush("PillWarnBorderBrush");
            HeroGlyph.Text = GlyphUnlock;
            HeroGlyph.Foreground = FindBrush("WarnBrush");
            HeroTitle.Text = "已解锁";
            HeroDesc.Text = connected
                ? "硬盘盘符已恢复，可以直接打开查看文件。锁定后盘符会再次从系统中隐藏。"
                : "硬盘未连接，插入后可以直接访问；随时可以一键锁定。";
            HeroPrimaryGlyph.Text = GlyphLock;
            HeroPrimaryText.Text = "锁定全部硬盘";
            HeroPrimaryButton.IsEnabled = connected;
            HeroSecondaryText.Text = "自动锁定规则";
        }

        // ---------- 保护概览 ----------
        ProtectedCountValue.Text = $"{count} 块";
        HotKeyValue.Text = _lockManager.Settings.Current.HotKeyDisplayText;

        var letters = _lockManager.GetAllTargetDriveLetters();
        if (count == 0)
        {
            DriveLettersValue.Text = "--";
        }
        else if (!connected)
        {
            DriveLettersValue.Text = "未连接";
        }
        else if (locked)
        {
            var original = _protectedDisks.SelectMany(d => d.OriginalLetters).Distinct().ToList();
            DriveLettersValue.Text = original.Count > 0
                ? $"已隐藏（原 {DiskState.JoinLetters(original)}）"
                : "已隐藏";
        }
        else
        {
            DriveLettersValue.Text = letters.Count > 0
                ? DiskState.JoinLetters(letters) + "（可访问）"
                : "无盘符";
        }

        ConnectionDot.Fill = connected ? FindBrush("SuccessBrush") : FindBrush("TextMutedBrush");
        ConnectionValue.Text = connected ? "已连接" : "未连接";

        // ---------- 硬盘列表 ----------
        ProtectedPillText.Text = count > 0 ? $"{count} 块已保护" : "尚无保护";

        var rows = new List<DiskState>(_protectedDisks);
        // 只引导第一块未保护硬盘，避免列表被噪音淹没
        if (_unprotectedDisks.Count > 0) rows.Add(_unprotectedDisks[0]);

        DiskList.ItemsSource = rows;
        EmptyDisksHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Brush FindBrush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;

    // ===================== 交互 =====================

    private void QuickStart_Click(object sender, RoutedEventArgs e) =>
        App.Instance.OpenQuickStart(Window.GetWindow(this));

    private void HeroPrimary_Click(object sender, RoutedEventArgs e)
    {
        if (!_lockManager.HasProtectionConfig)
        {
            App.Instance.OpenQuickStart(Window.GetWindow(this));
            return;
        }

        if (_lockManager.IsLocked) App.Instance.ShowUnlockDialog(Window.GetWindow(this));
        else App.Instance.RequestLock();
    }

    private void HeroSecondary_Click(object sender, RoutedEventArgs e) =>
        NavigateRequested?.Invoke("Settings");

    private void GoDisks_Click(object sender, RoutedEventArgs e) =>
        NavigateRequested?.Invoke("Disks");

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DiskState disk })
            DiskActions.ShowMoreMenu((Button)sender, _lockManager, disk, Window.GetWindow(this));
    }

    private void AddProtection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DiskState disk })
        {
            DiskActions.Add(_lockManager, disk, Window.GetWindow(this));
            Refresh();
        }
    }
}

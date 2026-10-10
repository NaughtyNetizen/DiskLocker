using System.Text.Json.Serialization;

namespace DiskLocker.Models;

/// <summary>事件归类，用于活动记录页的类型筛选</summary>
public enum ActivityKind
{
    Lock,
    Unlock,
    AutoLock,
    WrongPassword,
    Device,
    Settings,
    Failure
}

/// <summary>语义级别，决定 Toast / 时间线的「颜色 + 图标 + 文字」三重编码</summary>
public enum ActivitySeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>一条本地活动记录</summary>
public class ActivityEvent
{
    // Segoe Fluent Icons 字形，统一用转义写，避免私有区字符在源码里丢失
    private const string GlyphUnlock = "\uE785";
    private const string GlyphWarning = "\uE7BA";
    private const string GlyphError = "\uEA39";
    private const string GlyphClock = "\uE823";
    private const string GlyphLock = "\uE72E";
    private const string GlyphDevice = "\uE839";
    private const string GlyphSettings = "\uE713";
    private const string GlyphInfo = "\uE946";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime Time { get; set; } = DateTime.Now;

    public ActivityKind Kind { get; set; }

    public ActivitySeverity Severity { get; set; } = ActivitySeverity.Info;

    /// <summary>事件标题（如「空闲超时自动锁定」）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>事件详情（如「电脑空闲 30 分钟，2 块硬盘已自动锁定」）</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>关联硬盘序列号，可空</summary>
    public string? Serial { get; set; }

    /// <summary>时间线左侧显示的时间</summary>
    [JsonIgnore]
    public string TimeText => Time.ToString("HH:mm");

    /// <summary>完整时间，用于导出与 tooltip</summary>
    [JsonIgnore]
    public string FullTimeText => Time.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>语义级别 → 令牌键前缀（配合 PillBrushConverter）</summary>
    [JsonIgnore]
    public string SeverityBrushKey => Severity switch
    {
        ActivitySeverity.Success => "Success",
        ActivitySeverity.Warning => "Warn",
        ActivitySeverity.Error => "Danger",
        _ => "Accent"
    };

    /// <summary>时间线圆点图标</summary>
    [JsonIgnore]
    public string Glyph => Severity switch
    {
        ActivitySeverity.Success => Kind == ActivityKind.Unlock ? GlyphUnlock : GlyphLock,
        ActivitySeverity.Error => Kind == ActivityKind.WrongPassword ? GlyphWarning : GlyphError,
        ActivitySeverity.Warning => GlyphWarning,
        _ => Kind switch
        {
            ActivityKind.AutoLock => GlyphClock,
            ActivityKind.Lock => GlyphLock,
            ActivityKind.Device => GlyphDevice,
            ActivityKind.Settings => GlyphSettings,
            _ => GlyphInfo
        }
    };

    /// <summary>失败类事件整行用危险色底，便于扫读</summary>
    [JsonIgnore]
    public bool IsFailureRow => Severity == ActivitySeverity.Error;

    public static string KindLabel(ActivityKind kind) => kind switch
    {
        ActivityKind.Lock => "锁定",
        ActivityKind.Unlock => "解锁",
        ActivityKind.AutoLock => "自动锁定",
        ActivityKind.WrongPassword => "密码错误",
        ActivityKind.Device => "设备变化",
        ActivityKind.Settings => "设置变更",
        ActivityKind.Failure => "失败告警",
        _ => "其他"
    };
}

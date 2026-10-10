using DiskLocker.Models;

namespace DiskLocker.ViewModels;

/// <summary>硬盘状态枚举，决定胶囊的颜色 + 图标 + 文字</summary>
public enum DiskDisplayState
{
    /// <summary>受保护且盘符已隐藏</summary>
    Locked,
    /// <summary>受保护、当前可访问</summary>
    Unlocked,
    /// <summary>已检测到但未纳入保护</summary>
    Unprotected,
    /// <summary>在保护列表里但此刻未连接</summary>
    Disconnected
}

/// <summary>
/// 单块硬盘的展示模型（由 LockManager.GetDiskStates() 一次性算出，页面只做绑定）
/// </summary>
public class DiskState
{
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string PnpDeviceId { get; set; } = string.Empty;

    public bool IsProtected { get; set; }
    public bool IsConnected { get; set; }
    public bool IsLocked { get; set; }
    public bool IsSystemDisk { get; set; }

    public string SizeDisplay { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = string.Empty;
    public uint PartitionCount { get; set; }

    /// <summary>当前已挂载的盘符</summary>
    public List<string> DriveLetters { get; set; } = new();

    /// <summary>纳入保护时的原始盘符（锁定后仍能显示「原盘符 E:」）</summary>
    public List<string> OriginalLetters { get; set; } = new();

    public DiskDisplayState State =>
        !IsProtected ? DiskDisplayState.Unprotected
        : !IsConnected ? DiskDisplayState.Disconnected
        : IsLocked ? DiskDisplayState.Locked
        : DiskDisplayState.Unlocked;

    public string StatusText => State switch
    {
        DiskDisplayState.Locked => "已锁定",
        DiskDisplayState.Unlocked => "已解锁",
        DiskDisplayState.Unprotected => "未保护",
        _ => "未连接"
    };

    /// <summary>设计稿要求状态三重编码：颜色 + 文字 + 图标</summary>
    public string StatusGlyph => State switch
    {
        DiskDisplayState.Locked => "\uE72E",
        DiskDisplayState.Unlocked => "\uE785",
        DiskDisplayState.Unprotected => "\uE710",
        _ => "\uE8CE"
    };

    /// <summary>用于选择胶囊样式的键后缀（Pill{0} / PillText{0}）</summary>
    public string StatusBrushKey => State switch
    {
        DiskDisplayState.Locked => "Accent",
        DiskDisplayState.Unlocked => "Warn",
        DiskDisplayState.Unprotected => "Neutral",
        _ => "Neutral"
    };

    public bool IsHighlighted => State == DiskDisplayState.Unprotected;

    /// <summary>模板内两套操作区的显隐开关</summary>
    public bool ShowProtectedActions => IsProtected;
    public bool ShowUnprotectedActions => !IsProtected;

    public string SerialText => string.IsNullOrEmpty(SerialNumber) ? "S/N: 未知" : $"S/N: {SerialNumber}";

    /// <summary>
    /// 盘符统一成「E:」形式。服务层返回的已经带冒号，直接再拼一个会变成「E::」。
    /// </summary>
    public static string LetterText(string letter) =>
        string.IsNullOrWhiteSpace(letter) ? string.Empty : letter.TrimEnd(':', '\\') + ":";

    public static string JoinLetters(IEnumerable<string> letters) =>
        string.Join(" · ", letters.Select(LetterText).Where(s => s.Length > 0).Distinct());

    public string LettersText => DriveLetters.Count > 0 ? JoinLetters(DriveLetters) : "无盘符";

    public string OriginalLettersText => OriginalLetters.Count > 0
        ? JoinLetters(OriginalLetters)
        : DriveLetters.Count > 0 ? JoinLetters(DriveLetters) : "未知";

    /// <summary>概览页紧凑行：S/N · 容量 · 原盘符（盘符未知时不显示这一段）</summary>
    public string MetaLine
    {
        get
        {
            var parts = new List<string> { SerialText, string.IsNullOrEmpty(SizeDisplay) ? "容量未知" : SizeDisplay };
            if (OriginalLettersText != "未知") parts.Add("原盘符 " + OriginalLettersText);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>硬盘管理卡片分行：容量 · 原盘符（盘符未知时不显示这一段）</summary>
    public string CapacityLine => OriginalLettersText == "未知"
        ? (string.IsNullOrEmpty(SizeDisplay) ? "容量未知" : SizeDisplay)
        : $"{(string.IsNullOrEmpty(SizeDisplay) ? "容量未知" : SizeDisplay)} · 原盘符 {OriginalLettersText}";

    /// <summary>抽屉里的接口与容量行</summary>
    public string InterfaceLine =>
        $"{(string.IsNullOrEmpty(InterfaceType) ? "外接" : InterfaceType)} · {(string.IsNullOrEmpty(SizeDisplay) ? "容量未知" : SizeDisplay)}";

    public string PartitionLine => State == DiskDisplayState.Locked
        ? $"{Math.Max(PartitionCount, 1)} 个分区 · 原盘符 {OriginalLettersText}（当前已隐藏）"
        : $"{Math.Max(PartitionCount, 1)} 个分区 · 盘符 {LettersText}";

    public static DiskState FromTarget(TargetDiskEntry t) => new()
    {
        Model = string.IsNullOrEmpty(t.Model) ? "未知设备" : t.Model,
        SerialNumber = t.SerialNumber,
        PnpDeviceId = t.PnpDeviceId,
        IsProtected = true
    };
}

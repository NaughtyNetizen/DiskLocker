using DiskLocker.Models;

namespace DiskLocker.ViewModels;

/// <summary>
/// 磁盘卡片视图模型（用于仪表盘中的磁盘卡片绑定）
/// </summary>
public class DiskCardModel
{
    /// <summary>磁盘型号</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>硬件序列号</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>容量显示</summary>
    public string SizeDisplay { get; set; } = string.Empty;

    /// <summary>盘符文本（如 "E:, F:" 或 "无盘符"）</summary>
    public string DriveLettersText { get; set; } = string.Empty;

    /// <summary>是否已连接</summary>
    public bool IsConnected { get; set; }

    /// <summary>是否处于锁定（盘符隐藏）状态</summary>
    public bool IsLocked { get; set; }

    /// <summary>状态描述文本（如 "已连接 · 已锁定"）</summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>原始磁盘信息引用（用于添加/移除操作）</summary>
    public DiskInfo? Disk { get; set; }
}

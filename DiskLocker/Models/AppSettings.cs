namespace DiskLocker.Models;

/// <summary>
/// 被隐藏的卷与其归属硬盘（支持「只解锁这一块硬盘」）
/// </summary>
public class HiddenVolumeEntry
{
    public string VolumeGuid { get; set; } = string.Empty;

    /// <summary>恢复时优先使用的原盘符</summary>
    public string DriveLetter { get; set; } = string.Empty;

    /// <summary>该卷所属硬盘的序列号</summary>
    public string SerialNumber { get; set; } = string.Empty;
}

/// <summary>
/// 目标磁盘条目（支持多磁盘）
/// </summary>
public class TargetDiskEntry
{
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string PnpDeviceId { get; set; } = string.Empty;
}

/// <summary>
/// 应用程序设置（持久化到 JSON）
/// </summary>
public class AppSettings
{
    /// <summary>是否已完成初始配置向导</summary>
    public bool IsConfigured { get; set; }

    /// <summary>目标磁盘列表（支持多个）</summary>
    public List<TargetDiskEntry> TargetDisks { get; set; } = new();

    // ===== 以下为向后兼容的旧字段（单磁盘），初始化时自动迁移到 TargetDisks =====
    public string TargetDiskModel { get; set; } = string.Empty;
    public string TargetDiskSerialNumber { get; set; } = string.Empty;
    public string TargetDiskPnpDeviceId { get; set; } = string.Empty;

    /// <summary>
    /// 迁移旧的单磁盘设置到 TargetDisks 列表（仅一次）
    /// </summary>
    public void MigrateLegacySingleDisk()
    {
        if (TargetDisks.Count == 0 && !string.IsNullOrEmpty(TargetDiskSerialNumber))
        {
            TargetDisks.Add(new TargetDiskEntry
            {
                Model = TargetDiskModel,
                SerialNumber = TargetDiskSerialNumber,
                PnpDeviceId = TargetDiskPnpDeviceId
            });
        }
    }

    /// <summary>密码哈希值（BCrypt）</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>无操作自动锁定超时（分钟，0 表示禁用）</summary>
    public int IdleLockTimeoutMinutes { get; set; } = 30;

    /// <summary>系统锁屏时自动锁定</summary>
    public bool LockOnScreenLock { get; set; } = true;

    /// <summary>系统休眠/睡眠时自动锁定</summary>
    public bool LockOnSleep { get; set; } = true;

    /// <summary>开机自启动</summary>
    public bool AutoStartWithWindows { get; set; } = true;

    /// <summary>主题模式：system / dark / light</summary>
    public string ThemeMode { get; set; } = "system";

    /// <summary>是否已展示过首次「开始保护」引导（保证只提示一次）</summary>
    public bool QuickStartPrompted { get; set; }

    /// <summary>全局快捷键修饰符（MOD_ALT | MOD_CTRL = 0x0003）</summary>
    public uint HotKeyModifiers { get; set; } = 0x0003;

    /// <summary>全局快捷键虚拟键码（'L' = 0x4C）</summary>
    public uint HotKeyVirtualKey { get; set; } = 0x4C;

    /// <summary>当前保存的隐藏卷 GUID 路径列表（用于恢复盘符）</summary>
    public List<string> HiddenVolumeGuids { get; set; } = new();

    /// <summary>上次恢复时使用的盘符列表（优先恢复相同盘符）</summary>
    public List<string> LastDriveLetters { get; set; } = new();

    /// <summary>
    /// 带归属信息的隐藏卷列表，用于「单独解锁某一块硬盘」。
    /// HiddenVolumeGuids 仍是它的全集，老配置该列表为空时自动回退到整体解锁。
    /// </summary>
    public List<HiddenVolumeEntry> HiddenVolumes { get; set; } = new();

    /// <summary>全局快捷键显示文本（顺序与 Windows 惯例一致：Ctrl + Alt + Shift + Win + 键）</summary>
    public string HotKeyDisplayText
    {
        get
        {
            var parts = new List<string>();
            if ((HotKeyModifiers & 0x0002) != 0) parts.Add("Ctrl");
            if ((HotKeyModifiers & 0x0001) != 0) parts.Add("Alt");
            if ((HotKeyModifiers & 0x0004) != 0) parts.Add("Shift");
            if ((HotKeyModifiers & 0x0008) != 0) parts.Add("Win");
            parts.Add(VirtualKeyName(HotKeyVirtualKey));
            return string.Join(" + ", parts);
        }
    }

    /// <summary>虚拟键码转可读键名</summary>
    public static string VirtualKeyName(uint vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
        >= 0x60 and <= 0x69 => $"Num{vk - 0x60}",
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x1D => "Ctrl",
        0x12 => "Alt",
        0x10 => "Shift",
        0x5B => "Win",
        0x21 => "PgUp",
        0x22 => "PgDn",
        0x24 => "Home",
        0x23 => "End",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        _ => vk > 0 && vk <= 255 ? ((char)vk).ToString() : "L"
    };
}

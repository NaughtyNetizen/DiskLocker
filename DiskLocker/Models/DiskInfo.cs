namespace DiskLocker.Models;

/// <summary>
/// 物理磁盘信息模型
/// </summary>
public class DiskInfo
{
    /// <summary>磁盘索引（WMI Index，如 0, 1, 2）</summary>
    public uint Index { get; set; }

    /// <summary>磁盘型号</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>硬件序列号</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>PNP 设备 ID（包含 USB VID/PID 和序列号）</summary>
    public string PnpDeviceId { get; set; } = string.Empty;

    /// <summary>磁盘总大小（字节）</summary>
    public ulong Size { get; set; }

    /// <summary>接口类型（如 USB、SCSI 等）</summary>
    public string InterfaceType { get; set; } = string.Empty;

    /// <summary>媒体类型</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>分区数量</summary>
    public uint Partitions { get; set; }

    /// <summary>当前分配的盘符列表</summary>
    public List<string> DriveLetters { get; set; } = new();

    /// <summary>格式化后的大小显示</summary>
    public string SizeDisplay => FormatSize(Size);

    /// <summary>是否为外接/可移动磁盘</summary>
    public bool IsExternal =>
        InterfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase) ||
        MediaType.Equals("External hard disk media", StringComparison.OrdinalIgnoreCase) ||
        MediaType.Equals("Removable Media", StringComparison.OrdinalIgnoreCase) ||
        PnpDeviceId.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase);

    /// <summary>显示名称（用于 UI）</summary>
    public string DisplayName
    {
        get
        {
            var letters = DriveLetters.Count > 0
                ? $" ({string.Join(", ", DriveLetters)})"
                : "";
            var serial = !string.IsNullOrWhiteSpace(SerialNumber)
                ? $" [S/N: {SerialNumber}]"
                : "";
            return $"{Model}{letters}{serial} — {SizeDisplay}";
        }
    }

    private static string FormatSize(ulong bytes)
    {
        if (bytes == 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unitIndex = 0;
        double size = bytes;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:F1} {units[unitIndex]}";
    }
}

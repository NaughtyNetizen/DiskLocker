using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DiskLocker.Models;
using DiskLocker.Native;
using static DiskLocker.Native.WinApi;

namespace DiskLocker.Services;

/// <summary>
/// 磁盘管理服务
/// 纯 Win32 API 实现，不依赖 WMI/CIM
/// </summary>
public class DiskService
{
    // ===================== 磁盘信息查询（Win32 API） =====================

    /// <summary>
    /// 获取所有物理磁盘（纯 Win32 API，不依赖 WMI）
    /// </summary>
    public List<DiskInfo> GetAllPhysicalDisks()
    {
        var disks = new List<DiskInfo>();
        try
        {
            SimpleLogger.Log("GetAllPhysicalDisks: scanning via DeviceIoControl...");

            for (uint i = 0; i < 16; i++)
            {
                var devicePath = $@"\\.\PHYSICALDRIVE{i}";
                var handle = CreateFileW(devicePath, GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

                if (handle == INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                    continue; // 此磁盘号不存在

                try
                {
                    var disk = QueryDiskInfo(handle, i);
                    if (disk != null)
                    {
                        disk.DriveLetters = GetDriveLettersForDiskIndex(disk.Index);
                        SimpleLogger.Log($"GetAllPhysicalDisks: disk[{disk.Index}] model={disk.Model}, serial={disk.SerialNumber}, bus={disk.InterfaceType}");
                        disks.Add(disk);
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            SimpleLogger.Log($"GetAllPhysicalDisks: found {disks.Count} disks");
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"GetAllPhysicalDisks: EXCEPTION: {ex.Message}");
        }
        return disks;
    }

    /// <summary>
    /// 通过 DeviceIoControl 查询单个磁盘的详细信息
    /// </summary>
    private DiskInfo? QueryDiskInfo(IntPtr handle, uint index)
    {
        var disk = new DiskInfo { Index = index };

        // 查询存储设备属性（型号、序列号、总线类型）
        var queryBuffer = Marshal.AllocHGlobal(16);
        Marshal.WriteInt32(queryBuffer, 0, (int)StorageDeviceProperty); // PropertyId
        Marshal.WriteInt32(queryBuffer, 4, (int)PropertyStandardQuery); // QueryType

        try
        {
            var outSize = 4096;
            var outBuffer = Marshal.AllocHGlobal(outSize);

            try
            {
                if (DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                    queryBuffer, 16, outBuffer, (uint)outSize, out _, IntPtr.Zero))
                {
                    // 解析 STORAGE_DEVICE_DESCRIPTOR
                    uint vendorOffset = (uint)Marshal.ReadInt32(outBuffer, 12);
                    uint productOffset = (uint)Marshal.ReadInt32(outBuffer, 16);
                    uint serialOffset = (uint)Marshal.ReadInt32(outBuffer, 24);
                    uint busType = (uint)Marshal.ReadInt32(outBuffer, 28);

                    // 读取型号（ProductId）
                    var model = productOffset > 0
                        ? ReadAnsiString(outBuffer, (int)productOffset)
                        : null;
                    disk.Model = string.IsNullOrWhiteSpace(model) ? "Unknown" : model.Trim();

                    // 读取序列号
                    var serial = serialOffset > 0
                        ? ReadAnsiString(outBuffer, (int)serialOffset)
                        : null;
                    disk.SerialNumber = (serial ?? "").Trim();

                    // 读取厂商
                    var vendor = vendorOffset > 0
                        ? ReadAnsiString(outBuffer, (int)vendorOffset)
                        : null;

                    // 总线类型
                    disk.InterfaceType = busType == BusTypeUsb ? "USB" : "";
                    disk.MediaType = busType == BusTypeUsb ? "External hard disk media" : "";
                    disk.PnpDeviceId = busType == BusTypeUsb ? "USBSTOR" : "";
                }
                else
                {
                    var err = Marshal.GetLastWin32Error();
                    SimpleLogger.Log($"QueryDiskInfo({index}): IOCTL_STORAGE_QUERY_PROPERTY failed, error={err}");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(outBuffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(queryBuffer);
        }

        // 查询磁盘大小
        var sizeBuffer = Marshal.AllocHGlobal(8);
        try
        {
            if (DeviceIoControl(handle, IOCTL_DISK_GET_LENGTH_INFO,
                IntPtr.Zero, 0, sizeBuffer, 8, out _, IntPtr.Zero))
            {
                disk.Size = (ulong)Marshal.ReadInt64(sizeBuffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(sizeBuffer);
        }

        return disk;
    }

    /// <summary>
    /// 从非托管内存读取 ANSI 字符串
    /// </summary>
    private static string? ReadAnsiString(IntPtr buffer, int offset)
    {
        if (offset <= 0) return null;
        var ptr = IntPtr.Add(buffer, offset);
        return Marshal.PtrToStringAnsi(ptr);
    }

    // ===================== 盘符查询（Win32 API） =====================

    /// <summary>
    /// 获取外接/可移动磁盘列表
    /// </summary>
    public List<DiskInfo> GetExternalDisks()
    {
        return GetAllPhysicalDisks().Where(d => d.IsExternal).ToList();
    }

    /// <summary>
    /// 根据序列号查找磁盘
    /// </summary>
    public DiskInfo? FindDiskBySerialNumber(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
            return null;

        return GetAllPhysicalDisks().FirstOrDefault(d =>
            string.Equals(d.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 根据序列号或 PNP 设备 ID 检查目标磁盘是否已连接
    /// </summary>
    public bool IsDiskConnected(string serialNumber, string pnpDeviceId)
    {
        var disks = GetAllPhysicalDisks();
        SimpleLogger.Log($"IsDiskConnected: looking for serial='{serialNumber}', pnp='{pnpDeviceId}', disks={disks.Count}");
        var found = disks.Any(d =>
            (!string.IsNullOrEmpty(serialNumber) &&
             string.Equals(d.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(pnpDeviceId) &&
             string.Equals(d.PnpDeviceId, pnpDeviceId, StringComparison.OrdinalIgnoreCase)));
        SimpleLogger.Log($"IsDiskConnected: result={found}");
        return found;
    }

    /// <summary>
    /// 获取目标磁盘当前的所有盘符
    /// </summary>
    public List<string> GetDriveLettersForDisk(string serialNumber)
    {
        var disk = FindDiskBySerialNumber(serialNumber);
        if (disk == null)
            return new List<string>();
        return GetDriveLettersForDiskIndex(disk.Index);
    }

    /// <summary>
    /// 通过 Win32 API DeviceIoControl 获取磁盘的所有盘符
    /// 使用 IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS 查询每个卷对应的物理磁盘号
    /// </summary>
    public List<string> GetDriveLettersForDiskIndex(uint diskIndex)
    {
        var letters = new List<string>();
        try
        {
            SimpleLogger.Log($"GetDriveLettersForDiskIndex({diskIndex}): scanning via DeviceIoControl...");

            // 枚举所有逻辑驱动器
            foreach (var drive in DriveInfo.GetDrives())
            {
                // 跳过光驱、网络驱动器等
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                    continue;

                var driveLetter = $"{drive.Name[0]}:";
                var devicePath = $@"\\.\{drive.Name[0]}:";

                // 打开卷设备
                var handle = CreateFileW(devicePath, GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

                if (handle == INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                {
                    var err = Marshal.GetLastWin32Error();
                    SimpleLogger.Log($"GetDriveLettersForDiskIndex: CreateFileW failed for {devicePath}, error={err}");
                    continue;
                }

                try
                {
                    // 分配缓冲区，足够容纳多个 DISK_EXTENT
                    int extentSize = Marshal.SizeOf<WinApi.DISK_EXTENT>();
                    int bufferSize = 8 + 16 * extentSize; // header(8) + 16 extents
                    var buffer = Marshal.AllocHGlobal(bufferSize);

                    try
                    {
                        if (DeviceIoControl(handle, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                            IntPtr.Zero, 0, buffer, (uint)bufferSize, out _, IntPtr.Zero))
                        {
                            // 读取 NumberOfDiskExtents（前4字节）
                            uint numExtents = (uint)Marshal.ReadInt32(buffer);

                            // 第一个 DISK_EXTENT 在偏移量 8 处（uint + 4字节填充）
                            int firstOffset = 8;

                            for (int i = 0; i < numExtents; i++)
                            {
                                int offset = firstOffset + i * extentSize;
                                uint diskNum = (uint)Marshal.ReadInt32(buffer, offset);
                                if (diskNum == diskIndex)
                                {
                                    if (!letters.Contains(driveLetter))
                                        letters.Add(driveLetter);
                                    SimpleLogger.Log($"GetDriveLettersForDiskIndex: {driveLetter} is on disk {diskIndex}");
                                    break;
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            SimpleLogger.Log($"GetDriveLettersForDiskIndex({diskIndex}): letters={string.Join(", ", letters)}");
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"GetDriveLettersForDiskIndex EXCEPTION: {ex.Message}");
        }
        return letters;
    }

    // ===================== 盘符隐藏/恢复（Win32 API） =====================

    /// <summary>
    /// 获取盘符对应的卷 GUID 路径
    /// </summary>
    public string? GetVolumeGuidForDriveLetter(string driveLetter)
    {
        if (string.IsNullOrEmpty(driveLetter))
            return null;

        var mountPoint = driveLetter.EndsWith("\\") ? driveLetter : driveLetter + "\\";

        var buffer = new char[260];
        if (GetVolumeNameForVolumeMountPointW(mountPoint, buffer, (uint)buffer.Length))
        {
            var guid = new string(buffer).TrimEnd('\0');
            return guid;
        }
        return null;
    }

    /// <summary>
    /// 隐藏指定盘符（移除挂载点）
    /// </summary>
    public string? HideDriveLetter(string driveLetter)
    {
        if (string.IsNullOrEmpty(driveLetter))
            return null;

        var mountPoint = driveLetter.EndsWith("\\") ? driveLetter : driveLetter + "\\";

        var volumeGuid = GetVolumeGuidForDriveLetter(driveLetter);
        SimpleLogger.Log($"HideDriveLetter: letter={driveLetter}, guid={volumeGuid}, mountPoint={mountPoint}");

        var success = DeleteVolumeMountPointW(mountPoint);
        if (!success)
        {
            var err = Marshal.GetLastWin32Error();
            SimpleLogger.Log($"HideDriveLetter: DeleteVolumeMountPointW FAILED, error={err}");
            Trace.WriteLine($"[DiskLocker] DeleteVolumeMountPointW failed for {mountPoint}, error={err}");

            var deviceName = driveLetter.EndsWith(":") ? driveLetter : driveLetter + ":";
            var ok2 = DefineDosDeviceW(DDD_REMOVE_DEFINITION | DDD_EXACT_MATCH_ON_REMOVE, deviceName, null);
            if (!ok2)
            {
                var err2 = Marshal.GetLastWin32Error();
                SimpleLogger.Log($"HideDriveLetter: DefineDosDeviceW FAILED, error={err2}");
            }
            else
            {
                SimpleLogger.Log("HideDriveLetter: DefineDosDeviceW succeeded");
            }
        }
        else
        {
            SimpleLogger.Log("HideDriveLetter: DeleteVolumeMountPointW succeeded");
        }

        SHChangeNotify(SHCNE_DRIVEREMOVED, SHCNF_PATHW, Marshal.StringToHGlobalUni(mountPoint), IntPtr.Zero);

        return volumeGuid;
    }

    /// <summary>
    /// 隐藏目标磁盘的所有盘符
    /// </summary>
    public (List<string> volumeGuids, List<string> originalLetters) HideAllDriveLetters(string serialNumber)
    {
        var volumeGuids = new List<string>();
        var originalLetters = new List<string>();

        var letters = GetDriveLettersForDisk(serialNumber);
        SimpleLogger.Log($"HideAllDriveLetters({serialNumber}): letters={string.Join(", ", letters)}");
        foreach (var letter in letters)
        {
            var guid = HideDriveLetter(letter);
            SimpleLogger.Log($"HideAllDriveLetters: letter={letter}, guid={guid}");
            if (guid != null)
            {
                volumeGuids.Add(guid);
                originalLetters.Add(letter);
            }
        }

        return (volumeGuids, originalLetters);
    }

    /// <summary>
    /// 恢复卷的盘符
    /// </summary>
    public bool RestoreDriveLetter(string volumeGuid, string? preferredLetter = null)
    {
        if (string.IsNullOrEmpty(volumeGuid))
            return false;

        if (!volumeGuid.EndsWith("\\"))
            volumeGuid += "\\";

        var letter = preferredLetter;
        if (string.IsNullOrEmpty(letter) || IsDriveLetterInUse(letter))
        {
            letter = FindFreeDriveLetter();
        }

        if (string.IsNullOrEmpty(letter))
            return false;

        var mountPoint = letter.EndsWith("\\") ? letter : letter + "\\";

        var success = SetVolumeMountPointW(mountPoint, volumeGuid);
        if (!success)
        {
            var err = Marshal.GetLastWin32Error();
            SimpleLogger.Log($"RestoreDriveLetter: SetVolumeMountPointW FAILED for {mountPoint} -> {volumeGuid}, error={err}");
        }
        else
        {
            SimpleLogger.Log($"RestoreDriveLetter: SetVolumeMountPointW succeeded for {mountPoint} -> {volumeGuid}");
            SHChangeNotify(SHCNE_DRIVEADD, SHCNF_PATHW, Marshal.StringToHGlobalUni(mountPoint), IntPtr.Zero);
        }
        return success;
    }

    /// <summary>
    /// 恢复多个卷的盘符
    /// </summary>
    public List<string> RestoreAllDriveLetters(List<string> volumeGuids, List<string>? preferredLetters = null)
    {
        var restoredLetters = new List<string>();
        for (int i = 0; i < volumeGuids.Count; i++)
        {
            var guid = volumeGuids[i];
            var preferred = preferredLetters != null && i < preferredLetters.Count
                ? preferredLetters[i]
                : null;
            if (RestoreDriveLetter(guid, preferred))
            {
                restoredLetters.Add(preferred ?? FindFreeDriveLetter() ?? "");
            }
        }
        return restoredLetters;
    }

    // ===================== 卷 GUID 查询 =====================

    /// <summary>
    /// 获取指定物理磁盘上所有卷的 GUID（用于备用恢复：当存储的 GUID 失效时重新发现卷）
    /// </summary>
    public List<string> GetVolumeGuidsForDiskIndex(uint diskIndex)
    {
        var result = new List<string>();
        var buffer = new char[260];
        var handle = FindFirstVolumeW(buffer, (uint)buffer.Length);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            return result;

        try
        {
            do
            {
                var guid = new string(buffer).TrimEnd('\0');

                // 打开卷设备查询其所属的物理磁盘号（CreateFileW 需要去掉末尾反斜杠）
                var volHandle = CreateFileW(guid.TrimEnd('\\'), GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (volHandle == INVALID_HANDLE_VALUE || volHandle == IntPtr.Zero)
                    continue;

                try
                {
                    int extentSize = Marshal.SizeOf<WinApi.DISK_EXTENT>();
                    int bufferSize = 8 + 16 * extentSize;
                    var extentBuffer = Marshal.AllocHGlobal(bufferSize);
                    try
                    {
                        if (DeviceIoControl(volHandle, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                            IntPtr.Zero, 0, extentBuffer, (uint)bufferSize, out _, IntPtr.Zero))
                        {
                            uint numExtents = (uint)Marshal.ReadInt32(extentBuffer);
                            for (int i = 0; i < numExtents; i++)
                            {
                                uint diskNum = (uint)Marshal.ReadInt32(extentBuffer, 8 + i * extentSize);
                                if (diskNum == diskIndex)
                                {
                                    result.Add(guid);
                                    break;
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(extentBuffer);
                    }
                }
                finally
                {
                    CloseHandle(volHandle);
                }
            }
            while (FindNextVolumeW(handle, buffer, (uint)buffer.Length));
        }
        finally
        {
            FindVolumeClose(handle);
        }

        SimpleLogger.Log($"GetVolumeGuidsForDiskIndex({diskIndex}): found {result.Count} volume(s)");
        return result;
    }

    /// <summary>
    /// 检查卷 GUID 是否仍然存在（磁盘是否连接）
    /// </summary>
    public bool IsVolumeAvailable(string volumeGuid)
    {
        if (string.IsNullOrEmpty(volumeGuid))
            return false;

        if (!volumeGuid.EndsWith("\\"))
            volumeGuid += "\\";

        var buffer = new char[260];
        var handle = FindFirstVolumeW(buffer, (uint)buffer.Length);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            return false;

        try
        {
            do
            {
                var guid = new string(buffer).TrimEnd('\0');
                if (string.Equals(guid, volumeGuid, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            while (FindNextVolumeW(handle, buffer, (uint)buffer.Length));
        }
        finally
        {
            FindVolumeClose(handle);
        }
        return false;
    }

    /// <summary>
    /// 获取卷 GUID 当前挂载的盘符列表
    /// </summary>
    public List<string> GetMountPointsForVolumeGuid(string volumeGuid)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(volumeGuid))
            return result;

        if (!volumeGuid.EndsWith("\\"))
            volumeGuid += "\\";

        GetVolumePathNamesForVolumeNameW(volumeGuid, null, 0, out uint requiredLength);
        if (requiredLength == 0)
            return result;

        var buffer = new char[requiredLength];
        if (GetVolumePathNamesForVolumeNameW(volumeGuid, buffer, requiredLength, out _))
        {
            var paths = new string(buffer).TrimEnd('\0');
            foreach (var path in paths.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                result.Add(path);
            }
        }
        return result;
    }

    // ===================== 辅助方法 =====================

    /// <summary>
    /// 查找可用的盘符（从 D 到 Z）
    /// </summary>
    public string? FindFreeDriveLetter()
    {
        var usedLetters = DriveInfo.GetDrives()
            .Select(d => d.Name[0])
            .ToHashSet();

        for (char c = 'D'; c <= 'Z'; c++)
        {
            if (!usedLetters.Contains(c))
                return $"{c}:";
        }
        return null;
    }

    /// <summary>
    /// 检查盘符是否已被使用
    /// </summary>
    public bool IsDriveLetterInUse(string letter)
    {
        if (string.IsNullOrEmpty(letter))
            return true;
        var driveChar = letter[0];
        return DriveInfo.GetDrives().Any(d => d.Name[0] == driveChar);
    }

    /// <summary>
    /// 获取磁盘状态摘要
    /// </summary>
    public string GetDiskStatusSummary(string serialNumber, bool isLocked)
    {
        var disk = FindDiskBySerialNumber(serialNumber);
        if (disk == null)
            return "目标硬盘未连接";

        var status = isLocked ? "已锁定（盘符已隐藏）" : "已解锁（可正常访问）";
        var letters = GetDriveLettersForDisk(serialNumber);
        var letterStr = letters.Count > 0 ? string.Join(", ", letters) : "无盘符";
        return $"{disk.Model} — {letterStr} — {status}";
    }
}

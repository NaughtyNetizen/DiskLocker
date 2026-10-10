using System.Runtime.InteropServices;

namespace DiskLocker.Native;

/// <summary>
/// Windows API P/Invoke 声明
/// </summary>
internal static class WinApi
{
    // ===================== 卷挂载点管理 =====================

    /// <summary>
    /// 获取指定挂载点（盘符）对应的卷 GUID 名称
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVolumeNameForVolumeMountPointW(
        string lpszVolumeMountPoint,
        [Out] char[] lpszVolumeName,
        uint cchBufferLength);

    /// <summary>
    /// 删除卷挂载点（移除盘符，使磁盘在资源管理器中不可见）
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteVolumeMountPointW(
        string lpszVolumeMountPoint);

    /// <summary>
    /// 设置卷挂载点（分配盘符，恢复磁盘可见性）
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetVolumeMountPointW(
        string lpszVolumeMountPoint,
        string lpszVolumeName);

    /// <summary>
    /// 开始枚举所有卷的 GUID 名称
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindFirstVolumeW(
        [Out] char[] lpszVolumeName,
        uint cchBufferLength);

    /// <summary>
    /// 继续枚举卷的 GUID 名称
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindNextVolumeW(
        IntPtr hFindVolume,
        [Out] char[] lpszVolumeName,
        uint cchBufferLength);

    /// <summary>
    /// 结束卷枚举
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindVolumeClose(IntPtr hFindVolume);

    /// <summary>
    /// 获取指定卷 GUID 对应的所有挂载路径（盘符）
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVolumePathNamesForVolumeNameW(
        string lpszVolumeName,
        [Out] char[]? lpszVolumePathNames,
        uint cchBufferLength,
        out uint lpcchReturnLength);

    // ===================== DefineDosDevice =====================

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DefineDosDeviceW(
        uint dwFlags,
        string lpDeviceName,
        string? lpTargetPath);

    public const uint DDD_RAW_TARGET_PATH = 0x00000001;
    public const uint DDD_REMOVE_DEFINITION = 0x00000002;
    public const uint DDD_EXACT_MATCH_ON_REMOVE = 0x00000004;
    public const uint DDD_NO_BROADCAST_SYSTEM = 0x00000020;

    // ===================== 空闲检测 =====================

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll")]
    public static extern uint GetTickCount();

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    // ===================== 全局热键 =====================

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(
        IntPtr hWnd,
        int id);

    // 热键修饰符
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // 窗口消息
    public const int WM_HOTKEY = 0x0312;
    public const int WM_DEVICECHANGE = 0x0219;

    // ===================== 窗口辅助 =====================

    /// <summary>HWND_MESSAGE 常量，用于创建仅消息窗口</summary>
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    // ===================== 设备变更广播相关 =====================

    public const uint DBT_DEVICEARRIVAL = 0x8000;
    public const uint DBT_DEVICEREMOVECOMPLETE = 0x8004;
    public const uint DBT_DEVTYP_VOLUME = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct DEV_BROADCAST_VOLUME
    {
        public uint dbcv_size;
        public uint dbcv_devicetype;
        public uint dbcv_reserved;
        public uint dbcv_unitmask;
        public short dbcv_flags;
    }

    // ===================== Shell 通知 =====================

    /// <summary>
    /// 通知 Shell（资源管理器）刷新，使盘符变更立即生效
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern void SHChangeNotify(
        uint wEventId,
        uint uFlags,
        IntPtr dwItem1,
        IntPtr dwItem2);

    public const uint SHCNE_DRIVEREMOVED = 0x00000080;
    public const uint SHCNE_DRIVEADD = 0x00000100;
    public const uint SHCNE_ASSOCCHANGED = 0x08000000;
    public const uint SHCNF_PATHW = 0x0005;

    // ===================== 卷磁盘 Extents 查询 =====================

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    public const uint GENERIC_READ = 0x80000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;
    public const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;

    [StructLayout(LayoutKind.Sequential)]
    public struct DISK_EXTENT
    {
        public uint DiskNumber;
        public long StartingOffset;
        public long ExtentLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VOLUME_DISK_EXTENTS
    {
        public uint NumberOfDiskExtents;
        public DISK_EXTENT FirstExtent;
    }

    // ===================== 存储设备属性查询 =====================

    public const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    public const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
    public const uint StorageDeviceProperty = 0;
    public const uint PropertyStandardQuery = 0;
    public const uint BusTypeUsb = 7;

    // ===================== DWM 窗口属性（Mica 背景） =====================

    /// <summary>设置窗口 DWM 扩展属性（用于启用 Mica/云母背景、深色模式等）</summary>
    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    /// <summary>启用非客户区渲染（允许自绘标题栏并让 Mica 延伸到整个窗口）</summary>
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>系统背景材质类型（Mica/亚克力等）</summary>
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>背景材质：默认（无）</summary>
    public const int DWMSBT_AUTO = 0;

    /// <summary>背景材质：Mica（云母，主窗口）</summary>
    public const int DWMSBT_MAINWINDOW = 2;

    /// <summary>背景材质：Acrylic（亚克力/毛玻璃，类似 macOS 玻璃质感）</summary>
    public const int DWMSBT_TRANSIENTWINDOW = 3;

    /// <summary>背景材质：Mica Alt（标签页窗口）</summary>
    public const int DWMSBT_TABBEDWINDOW = 4;

    /// <summary>窗口圆角偏好（Win11）</summary>
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    /// <summary>圆角偏好：系统默认圆角</summary>
    public const int DWMWCP_ROUND = 2;

    // ===== 扩展窗口样式（Toast / 托盘浮层） =====

    public const int GWL_EXSTYLE = -20;

    /// <summary>窗口不出现在任务栏与 Alt+Tab</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>点击与显示都不抢占焦点，避免打断用户输入</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}

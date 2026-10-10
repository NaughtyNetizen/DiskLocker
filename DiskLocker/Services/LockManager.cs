using System.IO;
using System.Windows;
using DiskLocker.Models;
using DiskLocker.ViewModels;

namespace DiskLocker.Services;

/// <summary>解锁操作的结果</summary>
public enum UnlockResult
{
    /// <summary>解锁成功</summary>
    Success,
    /// <summary>密码错误</summary>
    WrongPassword,
    /// <summary>密码正确但恢复盘符失败</summary>
    RestoreFailed
}

/// <summary>
/// 锁管理器 - 核心协调器
/// 统一管理磁盘锁定/解锁、设备监听、自动锁定、全局热键等所有功能
/// </summary>
public class LockManager : IDisposable
{
    // ===== 服务实例 =====
    public SettingsService Settings { get; }
    public PasswordService Password { get; }
    public DiskService Disk { get; }
    public AutoStartService AutoStart { get; }

    /// <summary>本地活动记录（活动记录页的数据源）</summary>
    public EventLogService Events { get; }

    private DeviceWatcherService? _deviceWatcher;
    private GlobalHotkeyService? _hotkeyService;
    private AutoLockService? _autoLockService;

    // ===== 状态 =====
    private readonly object _stateLock = new();
    private bool _disposed;

    /// <summary>磁盘是否已锁定（至少一块受保护硬盘的盘符被隐藏）</summary>
    public bool IsLocked { get; private set; }

    /// <summary>目标磁盘是否已连接</summary>
    public bool IsTargetConnected { get; private set; }

    /// <summary>本次自动锁定的触发来源，供事件文案区分（在调用 LockDisk 前设置）</summary>
    private AutoLockReason? _pendingAutoLockReason;

    // ===== 事件 =====
    /// <summary>锁定状态变化时触发（UI 更新托盘图标等）</summary>
    public event Action? StateChanged;

    /// <summary>请求弹出解锁对话框（热键触发/托盘双击）</summary>
    public event Action? RequestUnlockDialog;

    /// <summary>目标磁盘插入时触发</summary>
    public event Action<DiskInfo>? TargetDiskArrived;

    /// <summary>发生错误时触发</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>需要以右下角 Toast 告知用户的操作结果（级别, 标题, 详情）</summary>
    public event Action<ActivitySeverity, string, string>? NotifyUser;

    public LockManager()
    {
        Settings = new SettingsService();
        Password = new PasswordService();
        Disk = new DiskService();
        AutoStart = new AutoStartService();
        Events = new EventLogService();
    }

    /// <summary>是否已完成保护配置（选了硬盘且设了密码）</summary>
    public bool HasProtectionConfig
    {
        get
        {
            var s = Settings.Current;
            return s.TargetDisks.Count > 0 && !string.IsNullOrEmpty(s.PasswordHash);
        }
    }

    /// <summary>记录一条活动事件，同时按需推送 Toast</summary>
    private void Log(ActivityKind kind, ActivitySeverity severity, string title,
                     string detail = "", string? serial = null, bool toast = false)
    {
        Events.Record(kind, severity, title, detail, serial);
        if (toast) NotifyUser?.Invoke(severity, title, detail);
    }

    /// <summary>
    /// 初始化所有服务并启动监听
    /// </summary>
    public void Initialize()
    {
        // 【D-16 修复】检查管理员权限（manifest 已 requireAdministrator，此处为安全兜底）
        CheckAdminPrivilege();

        var settings = Settings.Current;

        // 迁移旧的单磁盘设置到 TargetDisks 列表
        settings.MigrateLegacySingleDisk();
        if (settings.TargetDisks.Count > 0)
            Settings.Save();

        // 检查是否有任意目标磁盘已连接
        IsTargetConnected = CheckAnyTargetConnected();

        SimpleLogger.Log($"Initialize: TargetDisks={settings.TargetDisks.Count}, AnyConnected={IsTargetConnected}");

        // 旧配置补全卷归属，保证「单盘解锁」对升级用户同样可用
        EnsureVolumeOwnership();

        // 如果有保存的隐藏卷 GUID，说明磁盘当前处于锁定状态
        IsLocked = settings.HiddenVolumeGuids.Count > 0;
        SimpleLogger.Log($"Initialize: IsLocked={IsLocked}, HiddenGuids={settings.HiddenVolumeGuids.Count}");

        // 启动设备监听
        StartDeviceWatcher();

        // 启动自动锁定服务
        _autoLockService = new AutoLockService();
        _autoLockService.UpdateSettings(
            settings.IdleLockTimeoutMinutes,
            settings.LockOnScreenLock,
            settings.LockOnSleep);
        _autoLockService.AutoLockTriggered += OnAutoLockTriggered;
        _autoLockService.Start();
        if (IsLocked)
            _autoLockService.IsLocked = true;

        // 注册全局热键
        _hotkeyService = new GlobalHotkeyService();
        _hotkeyService.HotKeyPressed += OnHotKeyPressed;
        // 【D-17 修复】注册失败时提示用户（可能被其他程序占用）
        if (!_hotkeyService.Register(settings.HotKeyModifiers, settings.HotKeyVirtualKey))
        {
            SimpleLogger.Log("Initialize: global hotkey registration FAILED (may be occupied by another app)");
            ErrorOccurred?.Invoke("全局热键注册失败，可能被其他程序占用");
        }

        // 如果标记为锁定但磁盘实际有盘符（如磁盘被重新挂载），重新锁定
        if (IsTargetConnected && IsLocked)
        {
            var hasLetters = false;
            foreach (var target in settings.TargetDisks)
            {
                var currentLetters = Disk.GetDriveLettersForDisk(target.SerialNumber);
                SimpleLogger.Log($"Initialize: Locked but checking letters for {target.SerialNumber}: {string.Join(", ", currentLetters)}");
                if (currentLetters.Count > 0)
                {
                    hasLetters = true;
                    break;
                }
            }
            if (hasLetters)
            {
                IsLocked = false;
                Settings.Current.HiddenVolumeGuids.Clear();
                Settings.Current.HiddenVolumes.Clear();
                Settings.Save();
            }
        }

        // 关键修复：检测“盘符已隐藏但 GUID 丢失”的死锁状态
        // 场景：上次会话锁定了磁盘，但 HiddenVolumeGuids 被错误清除
        // 表现：目标磁盘无盘符 + 无存储 GUID → 实际已锁定但应用不知道
        if (IsTargetConnected && !IsLocked)
        {
            var anyDiskHasLetters = false;
            var allConnectedDisksHaveNoLetters = true;

            foreach (var target in settings.TargetDisks)
            {
                if (!Disk.IsDiskConnected(target.SerialNumber, target.PnpDeviceId))
                    continue; // 跳过未连接的磁盘

                var letters = Disk.GetDriveLettersForDisk(target.SerialNumber);
                if (letters.Count > 0)
                {
                    anyDiskHasLetters = true;
                    allConnectedDisksHaveNoLetters = false;
                }
            }

            if (!anyDiskHasLetters && allConnectedDisksHaveNoLetters)
            {
                // 所有已连接的目标磁盘都没有盘符，但也没有存储的 GUID
                // 说明磁盘实际处于锁定状态，只是 GUID 丢失了
                SimpleLogger.Log("Initialize: DETECTED LOST LOCK STATE - disks have no letters but no stored GUIDs, recovering...");
                var recovered = RecoverVolumeGuids();
                if (recovered.Count > 0)
                {
                    Settings.Current.HiddenVolumes = recovered;
                    Settings.Current.HiddenVolumeGuids = recovered.Select(v => v.VolumeGuid).ToList();
                    Settings.Save();
                    IsLocked = true;
                    if (_autoLockService != null)
                        _autoLockService.IsLocked = true;
                    SimpleLogger.Log($"Initialize: recovered {recovered.Count} volume GUID(s), state restored to LOCKED");
                }
                else
                {
                    SimpleLogger.Log("Initialize: WARNING - could not recover volume GUIDs");
                }
            }
        }

        // 如果目标磁盘已连接且未锁定，自动锁定
        if (IsTargetConnected && !IsLocked)
        {
            SimpleLogger.Log("Initialize: calling LockDisk()");
            LockDisk();
        }
    }

    /// <summary>
    /// 检查是否有任意目标磁盘已连接
    /// </summary>
    private bool CheckAnyTargetConnected()
    {
        foreach (var target in Settings.Current.TargetDisks)
        {
            if (Disk.IsDiskConnected(target.SerialNumber, target.PnpDeviceId))
                return true;
        }
        return false;
    }

    /// <summary>【D-16 修复】检查是否以管理员权限运行，否则警告</summary>
    private void CheckAdminPrivilege()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                SimpleLogger.Log("WARNING: not running as administrator, disk operations may fail");
                ErrorOccurred?.Invoke("程序未以管理员身份运行，磁盘操作可能失败");
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"CheckAdminPrivilege: {ex.Message}");
        }
    }

    /// <summary>
    /// 刷新目标磁盘连接状态（用于解锁前纠正可能的瞬间误判）
    /// </summary>
    public void RefreshConnectionState()
    {
        IsTargetConnected = CheckAnyTargetConnected();
        SimpleLogger.Log($"RefreshConnectionState: IsTargetConnected={IsTargetConnected}");
    }

    /// <summary>启动/重启设备监听</summary>
    private void StartDeviceWatcher()
    {
        _deviceWatcher?.Dispose();
        var settings = Settings.Current;
        var targets = settings.TargetDisks
            .Select(t => (t.SerialNumber, t.PnpDeviceId))
            .ToList();
        if (targets.Count > 0)
        {
            _deviceWatcher = new DeviceWatcherService(Disk, targets);
            _deviceWatcher.TargetDiskArrived += OnTargetDiskArrived;
            _deviceWatcher.TargetDiskRemoved += OnTargetDiskRemoved;
            _deviceWatcher.ErrorOccurred += ex => ErrorOccurred?.Invoke(ex.Message);
            _deviceWatcher.Start();
        }
    }

    // ===== 锁定/解锁核心方法 =====

    /// <summary>
    /// 锁定磁盘（隐藏所有已连接目标盘的盘符）
    /// </summary>
    /// <param name="toast">是否向用户推送右下角通知（后台自动锁定传 false）</param>
    public bool LockDisk(bool toast = false)
    {
        lock (_stateLock)
        {
            SimpleLogger.Log($"LockDisk: start, Connected={IsTargetConnected}, Locked={IsLocked}");
            if (!IsTargetConnected)
            {
                SimpleLogger.Log("LockDisk: no target connected");
                ErrorOccurred?.Invoke("目标硬盘未连接，无法锁定");
                Log(ActivityKind.Failure, ActivitySeverity.Warning, "锁定未执行", "目标硬盘未连接");
                return false;
            }

            if (IsLocked)
                return true; // 已锁定

            try
            {
                var entries = new List<HiddenVolumeEntry>();

                foreach (var target in Settings.Current.TargetDisks)
                {
                    if (!Disk.IsDiskConnected(target.SerialNumber, target.PnpDeviceId))
                    {
                        SimpleLogger.Log($"LockDisk: skipping {target.SerialNumber} (not connected)");
                        continue;
                    }

                    SimpleLogger.Log($"LockDisk: hiding letters for serial={target.SerialNumber}");
                    var (volumeGuids, originalLetters) = Disk.HideAllDriveLetters(target.SerialNumber);
                    SimpleLogger.Log($"LockDisk: {target.SerialNumber} volumeGuids={volumeGuids.Count}, letters={originalLetters.Count}");

                    // 逐卷记录归属硬盘，支持「只解锁这一块硬盘」
                    for (int i = 0; i < volumeGuids.Count; i++)
                    {
                        entries.Add(new HiddenVolumeEntry
                        {
                            VolumeGuid = volumeGuids[i],
                            DriveLetter = i < originalLetters.Count ? originalLetters[i] : string.Empty,
                            SerialNumber = target.SerialNumber
                        });
                    }
                }

                SimpleLogger.Log($"LockDisk: total hidden volumes={entries.Count}");

                if (entries.Count == 0)
                {
                    ErrorOccurred?.Invoke("目标硬盘没有可隐藏的盘符");
                    Log(ActivityKind.Failure, ActivitySeverity.Error, "锁定失败", "目标硬盘没有可隐藏的盘符");
                    return false;
                }

                // 保存隐藏的卷用于后续恢复（HiddenVolumeGuids 保持为全集，兼容旧逻辑）
                Settings.Current.HiddenVolumes = entries;
                Settings.Current.HiddenVolumeGuids = entries.Select(e => e.VolumeGuid).ToList();
                Settings.Current.LastDriveLetters = entries.Select(e => e.DriveLetter)
                    .Where(l => !string.IsNullOrEmpty(l)).ToList();
                Settings.Save();

                IsLocked = true;
                if (_autoLockService != null)
                    _autoLockService.IsLocked = true;

                var letterText = FormatLetters(entries.Select(e => e.DriveLetter));
                var autoReason = _pendingAutoLockReason;
                _pendingAutoLockReason = null;

                if (autoReason.HasValue)
                {
                    var (title, detail) = autoReason.Value switch
                    {
                        AutoLockReason.Idle => ("空闲超时自动锁定",
                            $"电脑空闲 {Settings.Current.IdleLockTimeoutMinutes} 分钟，{entries.Count} 个分区的盘符已隐藏"),
                        AutoLockReason.SessionLock => ("锁屏时自动锁定",
                            "检测到离开电脑，盘符已隐藏"),
                        _ => ("睡眠时自动锁定", "电脑进入睡眠或休眠，盘符已隐藏")
                    };
                    Log(ActivityKind.AutoLock, ActivitySeverity.Info, title, detail);
                }
                else
                {
                    Log(ActivityKind.Lock, ActivitySeverity.Success, "硬盘已锁定",
                        $"{entries.Count} 个分区的盘符已从系统中隐藏{letterText}", toast: toast);
                }

                StateChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"锁定失败: {ex.Message}");
                Log(ActivityKind.Failure, ActivitySeverity.Error, "锁定失败", ex.Message, toast: true);
                return false;
            }
        }
    }

    /// <summary>把盘符列表格式化为「（原盘符 E: · F:）」，为空时返回空串</summary>
    private static string FormatLetters(IEnumerable<string> letters)
    {
        var list = letters.Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(l => l).ToList();
        return list.Count == 0 ? "" : $"（原盘符 {DiskState.JoinLetters(list)}）";
    }

    /// <summary>
    /// 解锁全部磁盘（验证密码后恢复盘符）
    /// </summary>
    public UnlockResult UnlockDisk(string password) => UnlockDisk(password, null);

    /// <summary>
    /// 解锁磁盘。serial 为 null 时解锁全部；否则只恢复该硬盘的盘符。
    /// </summary>
    public UnlockResult UnlockDisk(string password, string? serial)
    {
        // 验证密码
        if (!Password.VerifyPassword(password, Settings.Current.PasswordHash))
        {
            SimpleLogger.Log("UnlockDisk: password verification FAILED");
            return UnlockResult.WrongPassword;
        }

        SimpleLogger.Log($"UnlockDisk: password verified, restoring disks (serial={serial ?? "ALL"})...");
        lock (_stateLock)
        {
            // 解锁前刷新连接状态（防止瞬间误判）
            IsTargetConnected = CheckAnyTargetConnected();

            var ok = string.IsNullOrEmpty(serial)
                ? RestoreDiskInternal()
                : RestoreDiskForSerial(serial);

            var result = ok ? UnlockResult.Success : UnlockResult.RestoreFailed;

            // 解锁成功后给予宽限期，避免自动锁定立即重新锁定
            if (ok && _autoLockService != null)
            {
                _autoLockService.GrantGracePeriod();
            }

            return result;
        }
    }

    /// <summary>
    /// 只恢复某一块硬盘的盘符，其余硬盘保持锁定。
    /// 老配置缺少卷归属信息时自动退回整体恢复，保证不会解不开。
    /// </summary>
    private bool RestoreDiskForSerial(string serial)
    {
        var all = Settings.Current.HiddenVolumes;
        if (all.Count == 0)
            return RestoreDiskInternal();

        var mine = all.Where(v => string.Equals(v.SerialNumber, serial, StringComparison.OrdinalIgnoreCase)).ToList();
        if (mine.Count == 0)
        {
            SimpleLogger.Log($"RestoreDiskForSerial({serial}): no hidden volume for this disk");
            return true;
        }

        try
        {
            var restored = Disk.RestoreAllDriveLetters(
                mine.Select(v => v.VolumeGuid).ToList(),
                mine.Select(v => v.DriveLetter).ToList());

            if (restored.Count == 0)
            {
                SimpleLogger.Log($"RestoreDiskForSerial({serial}): restore FAILED, keeping locked state");
                ErrorOccurred?.Invoke("恢复盘符失败，请重试");
                Log(ActivityKind.Failure, ActivitySeverity.Error, "解锁失败",
                    "无法恢复这块硬盘的盘符，请以管理员身份重新运行 DiskLocker。", serial, toast: true);
                return false;
            }

            var remaining = all.Except(mine).ToList();
            Settings.Current.HiddenVolumes = remaining;
            Settings.Current.HiddenVolumeGuids = remaining.Select(v => v.VolumeGuid).ToList();
            Settings.Current.LastDriveLetters = remaining.Select(v => v.DriveLetter)
                .Where(l => !string.IsNullOrEmpty(l)).ToList();
            Settings.Save();

            IsLocked = remaining.Count > 0;
            if (!IsLocked)
                _autoLockService?.ResetLockState();

            var model = Settings.Current.TargetDisks
                .FirstOrDefault(t => string.Equals(t.SerialNumber, serial, StringComparison.OrdinalIgnoreCase))?.Model;
            var letters = Disk.GetDriveLettersForDisk(serial);

            Log(ActivityKind.Unlock, ActivitySeverity.Success, "解锁成功",
                $"{(string.IsNullOrEmpty(model) ? "硬盘" : model)} 已恢复到盘符 {DiskState.JoinLetters(letters)}",
                serial, toast: true);

            StateChanged?.Invoke();
            SimpleLogger.Log($"RestoreDiskForSerial({serial}): SUCCESS, {restored.Count} volume(s) restored");
            return true;
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"RestoreDiskForSerial({serial}): EXCEPTION: {ex}");
            ErrorOccurred?.Invoke($"恢复盘符失败: {ex.Message}");
            Log(ActivityKind.Failure, ActivitySeverity.Error, "解锁失败", ex.Message, serial, toast: true);
            return false;
        }
    }

    /// <summary>
    /// 内部恢复盘符（不验证密码，用于退出时自动恢复）
    /// 只有真正恢复成功才清除锁定状态；失败时保留卷 GUID 以便重试
    /// </summary>
    /// <param name="announce">是否记为「解锁成功」事件（增删硬盘等内部恢复传 false）</param>
    private bool RestoreDiskInternal(bool announce = true)
    {
        try
        {
            var volumeGuids = Settings.Current.HiddenVolumeGuids;
            var preferredLetters = Settings.Current.LastDriveLetters;

            if (volumeGuids.Count == 0)
            {
                SimpleLogger.Log("RestoreDiskInternal: no stored volume GUIDs, nothing to restore");
                IsLocked = false;
                Settings.Current.HiddenVolumes.Clear();
                _autoLockService?.ResetLockState();
                StateChanged?.Invoke();
                return true; // 没有需要恢复的卷
            }

            SimpleLogger.Log($"RestoreDiskInternal: restoring {volumeGuids.Count} volume(s): {string.Join(", ", volumeGuids)}");
            var restored = Disk.RestoreAllDriveLetters(volumeGuids, preferredLetters);

            // 如果存储的卷 GUID 全部恢复失败，尝试备用方案：重新发现目标磁盘上的卷
            if (restored.Count == 0)
            {
                SimpleLogger.Log("RestoreDiskInternal: primary restore failed, trying fallback volume discovery");
                restored = FallbackRestoreByDiscovery();
            }

            if (restored.Count == 0)
            {
                // 恢复失败：保留锁定状态和卷 GUID，让用户可以重试
                SimpleLogger.Log("RestoreDiskInternal: all restore attempts FAILED, keeping locked state");
                ErrorOccurred?.Invoke("恢复盘符失败，请重试");
                if (announce)
                {
                    Log(ActivityKind.Failure, ActivitySeverity.Error, "解锁失败",
                        "密码正确，但无法恢复盘符，请以管理员身份重新运行 DiskLocker。", toast: true);
                }
                return false;
            }

            // 恢复成功，清除隐藏状态
            Settings.Current.HiddenVolumeGuids.Clear();
            Settings.Current.HiddenVolumes.Clear();
            Settings.Current.LastDriveLetters = restored;
            Settings.Save();

            IsLocked = false;
            _autoLockService?.ResetLockState();

            if (announce)
            {
                Log(ActivityKind.Unlock, ActivitySeverity.Success, "解锁成功",
                    $"{restored.Count} 个分区的盘符已恢复{FormatLetters(restored)}，可以正常使用。", toast: true);
            }

            StateChanged?.Invoke();
            SimpleLogger.Log($"RestoreDiskInternal: SUCCESS, restored {restored.Count} drive letter(s): {string.Join(", ", restored)}");
            return true;
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"RestoreDiskInternal: EXCEPTION: {ex}");
            ErrorOccurred?.Invoke($"恢复盘符失败: {ex.Message}");
            if (announce)
                Log(ActivityKind.Failure, ActivitySeverity.Error, "解锁失败", ex.Message, toast: true);
            return false;
        }
    }

    /// <summary>
    /// 备用恢复方案：当存储的卷 GUID 失效时，重新枚举目标磁盘上的所有卷并分配盘符
    /// </summary>
    private List<string> FallbackRestoreByDiscovery()
    {
        var restored = new List<string>();
        try
        {
            foreach (var target in Settings.Current.TargetDisks)
            {
                var disk = Disk.FindDiskBySerialNumber(target.SerialNumber);
                if (disk == null)
                {
                    SimpleLogger.Log($"FallbackRestore: disk {target.SerialNumber} not connected, skipping");
                    continue;
                }

                var volumeGuids = Disk.GetVolumeGuidsForDiskIndex(disk.Index);
                SimpleLogger.Log($"FallbackRestore: disk[{disk.Index}] has {volumeGuids.Count} volume(s)");

                foreach (var guid in volumeGuids)
                {
                    // 跳过已经有盘符的卷（挂载点为 "X:\" 形式）
                    var mountPoints = Disk.GetMountPointsForVolumeGuid(guid);
                    if (mountPoints.Any(mp => mp.Length <= 3))
                    {
                        SimpleLogger.Log($"FallbackRestore: volume {guid} already mounted, skipping");
                        continue;
                    }

                    var letter = Disk.FindFreeDriveLetter();
                    if (letter == null) break;

                    // 【D-10 修复】RestoreDriveLetter 现返回实际分配的盘符
                    var actualLetter = Disk.RestoreDriveLetter(guid, letter);
                    if (actualLetter != null)
                    {
                        restored.Add(actualLetter);
                        SimpleLogger.Log($"FallbackRestore: restored {guid} -> {actualLetter}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"FallbackRestore: EXCEPTION: {ex.Message}");
        }
        return restored;
    }

    /// <summary>
    /// 恢复丢失的卷：当磁盘已锁定（无盘符）但 GUID 丢失时，
    /// 重新枚举目标磁盘上的所有卷并返回带归属信息的条目（不分配盘符）
    /// </summary>
    private List<HiddenVolumeEntry> RecoverVolumeGuids()
    {
        var entries = new List<HiddenVolumeEntry>();
        try
        {
            foreach (var target in Settings.Current.TargetDisks)
            {
                var disk = Disk.FindDiskBySerialNumber(target.SerialNumber);
                if (disk == null)
                {
                    SimpleLogger.Log($"RecoverVolumeGuids: disk {target.SerialNumber} not connected, skipping");
                    continue;
                }

                var volumeGuids = Disk.GetVolumeGuidsForDiskIndex(disk.Index);
                SimpleLogger.Log($"RecoverVolumeGuids: disk[{disk.Index}] ({target.SerialNumber}) has {volumeGuids.Count} volume(s)");

                foreach (var guid in volumeGuids)
                {
                    // 只收集没有盘符的卷（已隐藏的）
                    var mountPoints = Disk.GetMountPointsForVolumeGuid(guid);
                    if (mountPoints.Any(mp => mp.Length <= 3))
                    {
                        SimpleLogger.Log($"RecoverVolumeGuids: volume {guid} already has mount point, skipping");
                        continue;
                    }

                    entries.Add(new HiddenVolumeEntry { VolumeGuid = guid, SerialNumber = target.SerialNumber });
                    SimpleLogger.Log($"RecoverVolumeGuids: found hidden volume {guid}");
                }
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"RecoverVolumeGuids: EXCEPTION: {ex.Message}");
        }
        return entries;
    }

    /// <summary>
    /// 为旧配置补全卷归属：HiddenVolumeGuids 有值但 HiddenVolumes 为空时，
    /// 按当前磁盘枚举把每个卷映射回所属硬盘，使「单盘解锁」对老数据也可用。
    /// 无法归属的卷保留空序列号，单盘解锁会自动退回整体恢复。
    /// </summary>
    private void EnsureVolumeOwnership()
    {
        var s = Settings.Current;
        if (s.HiddenVolumeGuids.Count == 0)
        {
            if (s.HiddenVolumes.Count > 0) s.HiddenVolumes.Clear();
            return;
        }
        if (s.HiddenVolumes.Count > 0) return; // 新配置已自带归属

        var letterByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var letters = s.LastDriveLetters;
        for (int i = 0; i < s.HiddenVolumeGuids.Count && i < letters.Count; i++)
            letterByGuid[s.HiddenVolumeGuids[i]] = letters[i];

        var entries = new List<HiddenVolumeEntry>();
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in s.TargetDisks)
        {
            var disk = Disk.FindDiskBySerialNumber(target.SerialNumber);
            if (disk == null) continue;

            foreach (var guid in Disk.GetVolumeGuidsForDiskIndex(disk.Index))
            {
                var match = s.HiddenVolumeGuids.FirstOrDefault(g => SameGuid(g, guid));
                if (match == null || !assigned.Add(match)) continue;

                entries.Add(new HiddenVolumeEntry
                {
                    VolumeGuid = match,
                    SerialNumber = target.SerialNumber,
                    DriveLetter = letterByGuid.TryGetValue(match, out var l) ? l : string.Empty
                });
            }
        }

        foreach (var guid in s.HiddenVolumeGuids.Where(g => !assigned.Contains(g)))
        {
            entries.Add(new HiddenVolumeEntry
            {
                VolumeGuid = guid,
                SerialNumber = string.Empty,
                DriveLetter = letterByGuid.TryGetValue(guid, out var l) ? l : string.Empty
            });
        }

        s.HiddenVolumes = entries;
        Settings.Save();
        SimpleLogger.Log($"EnsureVolumeOwnership: rebuilt {entries.Count} hidden volume entr(ies) for legacy config");
    }

    /// <summary>比较卷 GUID，容忍末尾反斜杠差异</summary>
    private static bool SameGuid(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 静默恢复磁盘（仅在明确要求解锁退出的场景调用，不需要密码）
    /// </summary>
    public void RestoreSilently()
    {
        lock (_stateLock)
        {
            if (IsLocked)
            {
                RestoreDiskInternal(announce: false);
            }
        }
    }

    // ===== 事件处理 =====

    private void OnTargetDiskArrived(DiskInfo disk)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            lock (_stateLock)
            {
                IsTargetConnected = true;

                // 如果磁盘重新插入，检查是否需要自动锁定
                if (!IsLocked)
                {
                    // 自动锁定所有已连接的目标磁盘
                    _pendingAutoLockReason = null;
                    LockDisk();
                }
            }
            Log(ActivityKind.Device, ActivitySeverity.Info, "检测到受保护硬盘接入",
                $"{(string.IsNullOrEmpty(disk.Model) ? "硬盘" : disk.Model)} · {disk.SizeDisplay}",
                disk.SerialNumber);
            TargetDiskArrived?.Invoke(disk);
            StateChanged?.Invoke();
        });
    }

    private void OnTargetDiskRemoved()
    {
        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                // 第一次确认：防止瞬间检测失败导致误判（USB 选择性挂起等）
                if (CheckAnyTargetConnected())
                {
                    SimpleLogger.Log("OnTargetDiskRemoved: false alarm (1st check), disk still connected");
                    return;
                }

                // 等待 3 秒后二次确认，避免 USB 控制器短暂重置导致误判
                await System.Threading.Tasks.Task.Delay(3000);

                if (CheckAnyTargetConnected())
                {
                    SimpleLogger.Log("OnTargetDiskRemoved: false alarm (2nd check after delay), disk still connected");
                    return;
                }

                SimpleLogger.Log("OnTargetDiskRemoved: confirmed, all target disks removed");
                lock (_stateLock)
                {
                    IsTargetConnected = false;
                    IsLocked = false;

                    // 清除隐藏的卷记录（磁盘已移除）
                    Settings.Current.HiddenVolumeGuids.Clear();
                    Settings.Current.HiddenVolumes.Clear();
                    Settings.Save();

                    _autoLockService?.ResetLockState();
                }
                Log(ActivityKind.Device, ActivitySeverity.Info, "受保护硬盘已断开", "所有目标硬盘已从电脑移除");
                StateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"OnTargetDiskRemoved: EXCEPTION: {ex.Message}");
            }
        });
    }

    private void OnAutoLockTriggered(AutoLockReason reason)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (IsTargetConnected && !IsLocked)
            {
                _pendingAutoLockReason = reason;
                LockDisk();
            }
        });
    }

    private void OnHotKeyPressed()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            RequestUnlockDialog?.Invoke();
        });
    }

    // ===== 设置更新方法 =====

    /// <summary>
    /// 设置目标磁盘列表（完全替换）
    /// </summary>
    public void SetTargetDisks(List<DiskInfo> disks)
    {
        lock (_stateLock)
        {
            // 如果当前有锁定的磁盘，先恢复
            if (IsLocked)
            {
                RestoreDiskInternal(announce: false);
            }

            Settings.Current.TargetDisks = disks.Select(d => new TargetDiskEntry
            {
                Model = d.Model,
                SerialNumber = d.SerialNumber,
                PnpDeviceId = d.PnpDeviceId
            }).ToList();
            Settings.Save();

            IsTargetConnected = CheckAnyTargetConnected();
            IsLocked = false;
        }

        // 重启设备监听
        StartDeviceWatcher();
        Log(ActivityKind.Settings, ActivitySeverity.Success, "保护范围已更新",
            $"{disks.Count} 块硬盘纳入保护");
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 应用快速开始的完整配置：选定硬盘 + 密码 + 推荐行为偏好，随后立即锁定。
    /// 行为偏好沿用当前值（首次进入时为设计稿的推荐值：空闲 30 分钟 / 锁屏 / 睡眠 / 开机自启）。
    /// </summary>
    public bool ApplyProtection(List<DiskInfo> disks, string password, bool toast = false)
    {
        var s = Settings.Current;
        s.PasswordHash = Password.HashPassword(password);
        s.IsConfigured = true;
        s.QuickStartPrompted = true;
        Settings.Save();

        SetTargetDisks(disks);
        AutoStart.SetAutoStart(s.AutoStartWithWindows);

        Log(ActivityKind.Settings, ActivitySeverity.Success, "已开始保护",
            $"{disks.Count} 块硬盘纳入保护，锁定后立即生效。");

        if (IsTargetConnected) return LockDisk(toast: toast);
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 添加一个目标磁盘（如果当前已锁定，会先解锁再重新锁定所有磁盘）
    /// </summary>
    public void AddTargetDisk(DiskInfo disk, bool toast = false)
    {
        bool wasLocked;
        lock (_stateLock)
        {
            // 避免重复添加
            if (Settings.Current.TargetDisks.Any(t =>
                string.Equals(t.SerialNumber, disk.SerialNumber, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            wasLocked = IsLocked;

            // 如果当前已锁定，先解锁（恢复所有盘符）
            if (wasLocked)
            {
                RestoreDiskInternal(announce: false);
            }

            // 添加新磁盘到目标列表
            Settings.Current.TargetDisks.Add(new TargetDiskEntry
            {
                Model = disk.Model,
                SerialNumber = disk.SerialNumber,
                PnpDeviceId = disk.PnpDeviceId
            });
            Settings.Save();

            IsTargetConnected = CheckAnyTargetConnected();
        }

        // 重启设备监听
        StartDeviceWatcher();

        // 如果之前是锁定的，重新锁定所有磁盘（包括新添加的）
        if (wasLocked)
        {
            LockDisk();
        }

        Log(ActivityKind.Settings, ActivitySeverity.Success, "已添加保护",
            $"{(string.IsNullOrEmpty(disk.Model) ? "硬盘" : disk.Model)} · {disk.SizeDisplay}",
            disk.SerialNumber, toast: toast);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 移除一个目标磁盘（如果当前已锁定，会先解锁再重新锁定剩余磁盘）
    /// </summary>
    public void RemoveTargetDisk(string serialNumber, bool toast = false)
    {
        bool wasLocked;
        lock (_stateLock)
        {
            wasLocked = IsLocked;

            // 如果当前已锁定，先解锁
            if (wasLocked)
            {
                RestoreDiskInternal(announce: false);
            }

            Settings.Current.TargetDisks.RemoveAll(t =>
                string.Equals(t.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
            Settings.Save();

            IsTargetConnected = CheckAnyTargetConnected();
        }

        StartDeviceWatcher();

        // 如果之前是锁定的且仍有目标磁盘，重新锁定
        if (wasLocked && IsTargetConnected)
        {
            LockDisk();
        }

        Log(ActivityKind.Settings, ActivitySeverity.Info, "已移除保护",
            "该硬盘恢复为普通硬盘，不再受密码保护。", serialNumber, toast: toast);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 获取所有目标磁盘当前盘符（用于 UI 显示）
    /// </summary>
    public List<string> GetAllTargetDriveLetters()
    {
        var letters = new List<string>();
        foreach (var target in Settings.Current.TargetDisks)
        {
            letters.AddRange(Disk.GetDriveLettersForDisk(target.SerialNumber));
        }
        return letters;
    }

    /// <summary>
    /// 一次枚举给出所有硬盘的展示状态（受保护 + 可添加），避免各页面重复查询 WMI。
    /// </summary>
    public List<DiskState> GetDiskStates()
    {
        var all = Disk.GetAllPhysicalDisks();
        var targets = Settings.Current.TargetDisks;
        var result = new List<DiskState>();

        foreach (var target in targets)
        {
            var actual = all.FirstOrDefault(d =>
                string.Equals(d.SerialNumber, target.SerialNumber, StringComparison.OrdinalIgnoreCase));

            var letters = actual?.DriveLetters ?? new List<string>();
            var hidden = Settings.Current.HiddenVolumes
                .Where(v => string.Equals(v.SerialNumber, target.SerialNumber, StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrEmpty(v.DriveLetter))
                .Select(v => v.DriveLetter)
                .Distinct()
                .ToList();

            result.Add(new DiskState
            {
                Model = string.IsNullOrEmpty(target.Model) ? (actual?.Model ?? "未知设备") : target.Model,
                SerialNumber = target.SerialNumber,
                PnpDeviceId = target.PnpDeviceId,
                IsProtected = true,
                IsConnected = actual != null,
                SizeDisplay = actual?.SizeDisplay ?? string.Empty,
                InterfaceType = actual?.InterfaceType ?? string.Empty,
                PartitionCount = actual?.Partitions ?? 0,
                DriveLetters = letters,
                // 锁定后盘符消失，用锁定前记录的原始盘符补上，保证「原盘符 E:」始终可见
                OriginalLetters = letters.Count > 0 ? letters : hidden,
                IsLocked = IsDiskLocked(target.SerialNumber, actual == null)
            });
        }

        var protectedSerials = targets.Select(t => t.SerialNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in all.Where(d => !protectedSerials.Contains(d.SerialNumber)))
        {
            result.Add(new DiskState
            {
                Model = string.IsNullOrEmpty(d.Model) ? "未知设备" : d.Model,
                SerialNumber = d.SerialNumber,
                PnpDeviceId = d.PnpDeviceId,
                IsProtected = false,
                IsConnected = true,
                SizeDisplay = d.SizeDisplay,
                InterfaceType = d.InterfaceType,
                PartitionCount = d.Partitions,
                DriveLetters = d.DriveLetters,
                IsLocked = false,
                IsSystemDisk = IsSystemDisk(d)
            });
        }

        return result;
    }

    /// <summary>某块硬盘当前是否处于盘符已隐藏状态</summary>
    public bool IsDiskLocked(string serialNumber, bool notConnected)
    {
        if (notConnected) return false;
        var hidden = Settings.Current.HiddenVolumes;
        if (hidden.Count > 0)
            return hidden.Any(v => string.Equals(v.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
        // 旧配置无归属信息：以全局锁定状态兜底
        return IsLocked;
    }

    /// <summary>系统盘不可被保护（锁住会开不了机）</summary>
    public static bool IsSystemDisk(DiskInfo disk)
    {
        var sysRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
        var sysLetter = sysRoot != null && sysRoot.Length > 0 ? char.ToUpperInvariant(sysRoot[0]) : 'C';
        return disk.DriveLetters.Any(l => l.Length > 0 && char.ToUpperInvariant(l[0]) == sysLetter);
    }

    /// <summary>
    /// 更新密码
    /// </summary>
    public bool UpdatePassword(string newPassword)
    {
        try
        {
            Settings.Current.PasswordHash = Password.HashPassword(newPassword);
            Settings.Save();
            Log(ActivityKind.Settings, ActivitySeverity.Success, "解锁密码已修改",
                "下次解锁硬盘需要使用新密码。");
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"密码更新失败: {ex.Message}");
            Log(ActivityKind.Failure, ActivitySeverity.Error, "密码修改失败", ex.Message, toast: true);
            return false;
        }
    }

    /// <summary>
    /// 更新自动锁定设置
    /// </summary>
    public void UpdateAutoLockSettings(int idleTimeout, bool lockOnScreenLock, bool lockOnSleep)
    {
        Settings.Current.IdleLockTimeoutMinutes = idleTimeout;
        Settings.Current.LockOnScreenLock = lockOnScreenLock;
        Settings.Current.LockOnSleep = lockOnSleep;
        Settings.Save();

        _autoLockService?.UpdateSettings(idleTimeout, lockOnScreenLock, lockOnSleep);
    }

    /// <summary>
    /// 更新开机自启设置
    /// </summary>
    public void UpdateAutoStart(bool enabled)
    {
        Settings.Current.AutoStartWithWindows = enabled;
        Settings.Save();
        AutoStart.SetAutoStart(enabled);
    }

    /// <summary>
    /// 更新主题模式（system / dark / light）
    /// </summary>
    public void UpdateTheme(string mode)
    {
        Settings.Current.ThemeMode = mode;
        Settings.Save();
    }

    /// <summary>暂停自动锁定一段时间（托盘「暂停自动锁定」），到期自动恢复</summary>
    public void PauseAutoLock(TimeSpan span)
    {
        _autoLockService?.Pause(span);
        Log(ActivityKind.Settings, ActivitySeverity.Warning, "已暂停自动锁定",
            $"{(int)span.TotalMinutes} 分钟后自动恢复；手动锁定与热键不受影响。", toast: true);
        StateChanged?.Invoke();
    }

    /// <summary>立即恢复自动锁定</summary>
    public void ResumeAutoLock()
    {
        _autoLockService?.Resume();
        Log(ActivityKind.Settings, ActivitySeverity.Success, "已恢复自动锁定",
            "空闲、锁屏与睡眠的自动锁定规则重新生效。", toast: true);
        StateChanged?.Invoke();
    }

    public bool IsAutoLockPaused => _autoLockService?.IsPaused == true;

    /// <summary>
    /// 更新全局热键。注册失败时回滚到原设置，避免留下按不动的快捷键。
    /// </summary>
    public bool UpdateHotkey(uint modifiers, uint virtualKey)
    {
        var oldModifiers = Settings.Current.HotKeyModifiers;
        var oldKey = Settings.Current.HotKeyVirtualKey;

        Settings.Current.HotKeyModifiers = modifiers;
        Settings.Current.HotKeyVirtualKey = virtualKey;
        Settings.Save();

        _hotkeyService?.Unregister();
        var ok = _hotkeyService?.Register(modifiers, virtualKey) ?? false;

        if (!ok)
        {
            Settings.Current.HotKeyModifiers = oldModifiers;
            Settings.Current.HotKeyVirtualKey = oldKey;
            Settings.Save();
            _hotkeyService?.Register(oldModifiers, oldKey);
            Log(ActivityKind.Failure, ActivitySeverity.Warning, "快捷键未生效",
                $"{GetHotKeyText(modifiers, virtualKey)} 注册失败，可能已被其他程序占用，已恢复为 {GetHotKeyText(oldModifiers, oldKey)}", toast: true);
            return false;
        }

        Log(ActivityKind.Settings, ActivitySeverity.Success, "解锁快捷键已更新",
            $"现在按 {new AppSettings { HotKeyModifiers = modifiers, HotKeyVirtualKey = virtualKey }.HotKeyDisplayText} 可呼出解锁窗口");
        return true;
    }

    /// <summary>用临时实例渲染一组修饰符+键码的显示文本</summary>
    private static string GetHotKeyText(uint modifiers, uint virtualKey) =>
        new AppSettings { HotKeyModifiers = modifiers, HotKeyVirtualKey = virtualKey }.HotKeyDisplayText;

    /// <summary>
    /// 获取当前状态文本（用于托盘提示）
    /// </summary>
    public string GetStatusText()
    {
        var count = Settings.Current.TargetDisks.Count;
        if (count == 0)
            return "DiskLocker - 未设置目标硬盘";

        if (!IsTargetConnected)
            return $"DiskLocker - 目标硬盘未连接（{count} 个）";

        var modelList = string.Join("、", Settings.Current.TargetDisks
            .Select(t => string.IsNullOrEmpty(t.Model) ? "未知" : t.Model)
            .Distinct());

        return IsLocked
            ? $"DiskLocker - {modelList}（{count} 个已锁定）"
            : $"DiskLocker - {modelList}（{count} 个已解锁）";
    }

    /// <summary>
    /// 获取磁盘详细状态
    /// </summary>
    public string GetDetailedStatus()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var target in Settings.Current.TargetDisks)
        {
            var summary = Disk.GetDiskStatusSummary(target.SerialNumber, IsLocked);
            sb.AppendLine(summary);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 关闭所有服务（退出程序时调用）
    /// 注意：不恢复盘符，保持锁定状态，只有输入密码才能解锁
    /// </summary>
    public void Shutdown()
    {
        _deviceWatcher?.Stop();
        _autoLockService?.Stop();
        _hotkeyService?.Unregister();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _deviceWatcher?.Dispose();
        _autoLockService?.Dispose();
        _hotkeyService?.Dispose();
    }
}

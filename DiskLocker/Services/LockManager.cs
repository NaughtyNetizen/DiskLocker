using System.Windows;
using DiskLocker.Models;
using Application = System.Windows.Application;

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

    private DeviceWatcherService? _deviceWatcher;
    private GlobalHotkeyService? _hotkeyService;
    private AutoLockService? _autoLockService;

    // ===== 状态 =====
    private readonly object _stateLock = new();
    private bool _disposed;

    /// <summary>磁盘是否已锁定（盘符已隐藏）</summary>
    public bool IsLocked { get; private set; }

    /// <summary>目标磁盘是否已连接</summary>
    public bool IsTargetConnected { get; private set; }

    // ===== 事件 =====
    /// <summary>锁定状态变化时触发（UI 更新托盘图标等）</summary>
    public event Action? StateChanged;

    /// <summary>请求弹出解锁对话框（热键触发/托盘双击）</summary>
    public event Action? RequestUnlockDialog;

    /// <summary>目标磁盘插入时触发</summary>
    public event Action<DiskInfo>? TargetDiskArrived;

    /// <summary>发生错误时触发</summary>
    public event Action<string>? ErrorOccurred;

    public LockManager()
    {
        Settings = new SettingsService();
        Password = new PasswordService();
        Disk = new DiskService();
        AutoStart = new AutoStartService();
    }

    /// <summary>
    /// 初始化所有服务并启动监听
    /// </summary>
    public void Initialize()
    {
        var settings = Settings.Current;

        // 迁移旧的单磁盘设置到 TargetDisks 列表
        settings.MigrateLegacySingleDisk();
        if (settings.TargetDisks.Count > 0)
            Settings.Save();

        // 检查是否有任意目标磁盘已连接
        IsTargetConnected = CheckAnyTargetConnected();

        SimpleLogger.Log($"Initialize: TargetDisks={settings.TargetDisks.Count}, AnyConnected={IsTargetConnected}");

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
        _hotkeyService.Register(settings.HotKeyModifiers, settings.HotKeyVirtualKey);

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
                var recoveredGuids = RecoverVolumeGuids();
                if (recoveredGuids.Count > 0)
                {
                    Settings.Current.HiddenVolumeGuids = recoveredGuids;
                    Settings.Save();
                    IsLocked = true;
                    if (_autoLockService != null)
                        _autoLockService.IsLocked = true;
                    SimpleLogger.Log($"Initialize: recovered {recoveredGuids.Count} volume GUID(s), state restored to LOCKED");
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
    /// 锁定磁盘（隐藏所有盘符）
    /// </summary>
    public bool LockDisk()
    {
        lock (_stateLock)
        {
            SimpleLogger.Log($"LockDisk: start, Connected={IsTargetConnected}, Locked={IsLocked}");
            if (!IsTargetConnected)
            {
                SimpleLogger.Log("LockDisk: no target connected");
                ErrorOccurred?.Invoke("目标硬盘未连接，无法锁定");
                return false;
            }

            if (IsLocked)
                return true; // 已锁定

            try
            {
                var allVolumeGuids = new List<string>();
                var allOriginalLetters = new List<string>();

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

                    allVolumeGuids.AddRange(volumeGuids);
                    allOriginalLetters.AddRange(originalLetters);
                }

                SimpleLogger.Log($"LockDisk: total volumeGuids={allVolumeGuids.Count}, total letters={allOriginalLetters.Count}");

                if (allVolumeGuids.Count == 0)
                {
                    ErrorOccurred?.Invoke("目标硬盘没有可隐藏的盘符");
                    return false;
                }

                // 保存隐藏的卷 GUID 用于后续恢复
                Settings.Current.HiddenVolumeGuids = allVolumeGuids;
                Settings.Current.LastDriveLetters = allOriginalLetters;
                Settings.Save();

                IsLocked = true;
                if (_autoLockService != null)
                    _autoLockService.IsLocked = true;

                StateChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"锁定失败: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 解锁磁盘（验证密码后恢复盘符）
    /// </summary>
    public UnlockResult UnlockDisk(string password)
    {
        // 验证密码
        if (!Password.VerifyPassword(password, Settings.Current.PasswordHash))
        {
            SimpleLogger.Log("UnlockDisk: password verification FAILED");
            return UnlockResult.WrongPassword;
        }

        SimpleLogger.Log("UnlockDisk: password verified, restoring disks...");
        lock (_stateLock)
        {
            // 解锁前刷新连接状态（防止瞬间误判）
            IsTargetConnected = CheckAnyTargetConnected();

            var result = RestoreDiskInternal() ? UnlockResult.Success : UnlockResult.RestoreFailed;

            // 解锁成功后给予宽限期，避免自动锁定立即重新锁定
            if (result == UnlockResult.Success && _autoLockService != null)
            {
                _autoLockService.GrantGracePeriod();
            }

            return result;
        }
    }

    /// <summary>
    /// 内部恢复盘符（不验证密码，用于退出时自动恢复）
    /// 只有真正恢复成功才清除锁定状态；失败时保留卷 GUID 以便重试
    /// </summary>
    private bool RestoreDiskInternal()
    {
        try
        {
            var volumeGuids = Settings.Current.HiddenVolumeGuids;
            var preferredLetters = Settings.Current.LastDriveLetters;

            if (volumeGuids.Count == 0)
            {
                SimpleLogger.Log("RestoreDiskInternal: no stored volume GUIDs, nothing to restore");
                IsLocked = false;
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
                return false;
            }

            // 恢复成功，清除隐藏状态
            Settings.Current.HiddenVolumeGuids.Clear();
            Settings.Current.LastDriveLetters = restored;
            Settings.Save();

            IsLocked = false;
            _autoLockService?.ResetLockState();

            StateChanged?.Invoke();
            SimpleLogger.Log($"RestoreDiskInternal: SUCCESS, restored {restored.Count} drive letter(s): {string.Join(", ", restored)}");
            return true;
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"RestoreDiskInternal: EXCEPTION: {ex}");
            ErrorOccurred?.Invoke($"恢复盘符失败: {ex.Message}");
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

                    if (Disk.RestoreDriveLetter(guid, letter))
                    {
                        restored.Add(letter);
                        SimpleLogger.Log($"FallbackRestore: restored {guid} -> {letter}");
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
    /// 恢复丢失的卷 GUID：当磁盘已锁定（无盘符）但 GUID 丢失时，
    /// 重新枚举目标磁盘上的所有卷并返回其 GUID（不分配盘符）
    /// </summary>
    private List<string> RecoverVolumeGuids()
    {
        var guids = new List<string>();
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

                    guids.Add(guid);
                    SimpleLogger.Log($"RecoverVolumeGuids: found hidden volume {guid}");
                }
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"RecoverVolumeGuids: EXCEPTION: {ex.Message}");
        }
        return guids;
    }

    /// <summary>
    /// 静默恢复磁盘（退出程序时调用，不需要密码）
    /// </summary>
    public void RestoreSilently()
    {
        lock (_stateLock)
        {
            if (IsLocked)
            {
                RestoreDiskInternal();
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
                    LockDisk();
                }
            }
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

                    // 清除隐藏的卷 GUID（磁盘已移除）
                    Settings.Current.HiddenVolumeGuids.Clear();
                    Settings.Save();

                    _autoLockService?.ResetLockState();
                }
                StateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"OnTargetDiskRemoved: EXCEPTION: {ex.Message}");
            }
        });
    }

    private void OnAutoLockTriggered()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (IsTargetConnected && !IsLocked)
            {
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
                RestoreDiskInternal();
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
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 添加一个目标磁盘（如果当前已锁定，会先解锁再重新锁定所有磁盘）
    /// </summary>
    public void AddTargetDisk(DiskInfo disk)
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
                RestoreDiskInternal();
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

        StateChanged?.Invoke();
    }

    /// <summary>
    /// 移除一个目标磁盘（如果当前已锁定，会先解锁再重新锁定剩余磁盘）
    /// </summary>
    public void RemoveTargetDisk(string serialNumber)
    {
        bool wasLocked;
        lock (_stateLock)
        {
            wasLocked = IsLocked;

            // 如果当前已锁定，先解锁
            if (wasLocked)
            {
                RestoreDiskInternal();
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
    /// 更新密码
    /// </summary>
    public bool UpdatePassword(string newPassword)
    {
        try
        {
            Settings.Current.PasswordHash = Password.HashPassword(newPassword);
            Settings.Save();
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"密码更新失败: {ex.Message}");
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
    /// 更新全局热键
    /// </summary>
    public bool UpdateHotkey(uint modifiers, uint virtualKey)
    {
        Settings.Current.HotKeyModifiers = modifiers;
        Settings.Current.HotKeyVirtualKey = virtualKey;
        Settings.Save();

        _hotkeyService?.Unregister();
        return _hotkeyService?.Register(modifiers, virtualKey) ?? false;
    }

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

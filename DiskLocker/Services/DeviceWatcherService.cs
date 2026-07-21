using DiskLocker.Models;

namespace DiskLocker.Services;

/// <summary>
/// 设备热插拔监听服务
/// 使用轮询方式检测磁盘插入/移除（WMI 事件在 .NET 8 提权进程中有兼容问题）
/// 支持同时监听多个目标磁盘
/// </summary>
public class DeviceWatcherService : IDisposable
{
    private System.Timers.Timer? _pollTimer;
    private bool _disposed;

    /// <summary>目标磁盘插入时触发</summary>
    public event Action<DiskInfo>? TargetDiskArrived;

    /// <summary>目标磁盘移除时触发</summary>
    public event Action? TargetDiskRemoved;

    /// <summary>发生错误时触发</summary>
    public event Action<Exception>? ErrorOccurred;

    private readonly DiskService _diskService;
    private readonly List<(string serial, string pnpId)> _targets;
    private readonly HashSet<string> _connectedSerials = new();
    private bool _wasAnyConnected;

    public DeviceWatcherService(DiskService diskService, List<(string serial, string pnpId)> targets)
    {
        _diskService = diskService;
        _targets = targets ?? new List<(string, string)>();
    }

    /// <summary>启动轮询监听</summary>
    public void Start()
    {
        // 记录初始连接状态
        var allDisks = _diskService.GetAllPhysicalDisks();
        foreach (var (serial, pnpId) in _targets)
        {
            var connected = allDisks.Any(d =>
                (!string.IsNullOrEmpty(serial) &&
                 string.Equals(d.SerialNumber, serial, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(pnpId) &&
                 string.Equals(d.PnpDeviceId, pnpId, StringComparison.OrdinalIgnoreCase)));
            if (connected)
                _connectedSerials.Add(serial);
        }
        _wasAnyConnected = _connectedSerials.Count > 0;
        SimpleLogger.Log($"DeviceWatcher: Start, targets={_targets.Count}, connected={_connectedSerials.Count}");

        _pollTimer = new System.Timers.Timer(3000); // 每 3 秒轮询一次
        _pollTimer.Elapsed += OnPollElapsed;
        _pollTimer.Start();
    }

    /// <summary>轮询回调</summary>
    private void OnPollElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            var allDisks = _diskService.GetAllPhysicalDisks();
            var newlyConnected = new List<DiskInfo>();

            foreach (var (serial, pnpId) in _targets)
            {
                var connected = allDisks.Any(d =>
                    (!string.IsNullOrEmpty(serial) &&
                     string.Equals(d.SerialNumber, serial, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(pnpId) &&
                     string.Equals(d.PnpDeviceId, pnpId, StringComparison.OrdinalIgnoreCase)));

                if (connected && !_connectedSerials.Contains(serial))
                {
                    // 新插入的磁盘
                    var disk = allDisks.FirstOrDefault(d =>
                        !string.IsNullOrEmpty(serial) &&
                        string.Equals(d.SerialNumber, serial, StringComparison.OrdinalIgnoreCase));
                    if (disk != null)
                        newlyConnected.Add(disk);
                    _connectedSerials.Add(serial);
                }
                else if (!connected && _connectedSerials.Contains(serial))
                {
                    _connectedSerials.Remove(serial);
                }
            }

            if (newlyConnected.Count > 0)
            {
                SimpleLogger.Log($"DeviceWatcher: {newlyConnected.Count} disk(s) arrived, waiting for drive letters...");
                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                {
                    try
                    {
                        foreach (var disk in newlyConnected)
                        {
                            SimpleLogger.Log($"DeviceWatcher: disk arrived confirmed, model={disk.Model}");
                            TargetDiskArrived?.Invoke(disk);
                        }
                    }
                    catch (Exception ex)
                    {
                        SimpleLogger.Log($"DeviceWatcher: arrived check error: {ex.Message}");
                    }
                });
            }

            var anyConnected = _connectedSerials.Count > 0;
            if (!anyConnected && _wasAnyConnected)
            {
                SimpleLogger.Log("DeviceWatcher: all target disks removed");
                _wasAnyConnected = false;
                TargetDiskRemoved?.Invoke();
            }
            else if (anyConnected)
            {
                _wasAnyConnected = true;
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"DeviceWatcher: poll error: {ex.Message}");
            ErrorOccurred?.Invoke(ex);
        }
    }

    /// <summary>停止监听</summary>
    public void Stop()
    {
        _pollTimer?.Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _pollTimer?.Dispose();
    }
}

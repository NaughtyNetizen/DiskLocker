using System.Windows.Threading;
using Microsoft.Win32;
using DiskLocker.Native;
using static DiskLocker.Native.WinApi;

namespace DiskLocker.Services;

/// <summary>自动锁定的触发来源，用于活动记录区分文案</summary>
public enum AutoLockReason
{
    Idle,
    SessionLock,
    Sleep
}

/// <summary>
/// 自动锁定服务
/// 支持三种自动锁定触发方式：无操作超时、系统锁屏、系统休眠
/// </summary>
public class AutoLockService : IDisposable
{
    private readonly DispatcherTimer _idleTimer;
    private bool _disposed;

    /// <summary>触发自动锁定（携带触发来源）</summary>
    public event Action<AutoLockReason>? AutoLockTriggered;

    /// <summary>无操作超时时间（分钟，0 = 禁用）</summary>
    public int IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>是否监听系统锁屏</summary>
    public bool LockOnScreenLock { get; set; } = true;

    /// <summary>是否监听系统休眠</summary>
    public bool LockOnSleep { get; set; } = true;

    /// <summary>是否已锁定（避免重复锁定）</summary>
    public bool IsLocked { get; set; }

    /// <summary>解锁后的宽限期截止时间（在此期间不触发自动锁定）</summary>
    private DateTime _gracePeriodEnd = DateTime.MinValue;

    /// <summary>用户主动「暂停自动锁定」的截止时间</summary>
    private DateTime _pausedUntil = DateTime.MinValue;

    /// <summary>当前是否处于暂停自动锁定状态</summary>
    public bool IsPaused => DateTime.UtcNow < _pausedUntil;

    public AutoLockService()
    {
        _idleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5) // 每 5 秒检查一次空闲时间
        };
        _idleTimer.Tick += OnIdleTimerTick;

        // 注册系统事件
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>启动自动锁定服务</summary>
    public void Start()
    {
        if (IdleTimeoutMinutes > 0)
            _idleTimer.Start();
    }

    /// <summary>停止自动锁定服务</summary>
    public void Stop()
    {
        _idleTimer.Stop();
    }

    /// <summary>重置锁定状态（解锁后调用）</summary>
    public void ResetLockState()
    {
        IsLocked = false;
    }

    /// <summary>
    /// 给予解锁宽限期（默认 60 秒），防止解锁后立即被自动锁定重新锁定
    /// </summary>
    public void GrantGracePeriod(int seconds = 60)
    {
        _gracePeriodEnd = DateTime.UtcNow.AddSeconds(seconds);
    }

    /// <summary>暂停自动锁定（三种触发方式一起暂停），到期自动恢复</summary>
    public void Pause(TimeSpan span)
    {
        _pausedUntil = DateTime.UtcNow + span;
    }

    /// <summary>立即恢复自动锁定</summary>
    public void Resume()
    {
        _pausedUntil = DateTime.MinValue;
    }

    /// <summary>暂停期或解锁宽限期内都不触发自动锁定</summary>
    private bool Blocked() => IsPaused || DateTime.UtcNow < _gracePeriodEnd;

    private void OnIdleTimerTick(object? sender, EventArgs e)
    {
        if (IsLocked || IdleTimeoutMinutes <= 0)
            return;

        // 宽限期 / 暂停期内不触发自动锁定
        if (Blocked())
            return;

        var idleMinutes = GetIdleTimeMinutes();
        if (idleMinutes >= IdleTimeoutMinutes)
        {
            IsLocked = true;
            AutoLockTriggered?.Invoke(AutoLockReason.Idle);
        }
    }

    /// <summary>获取系统空闲时间（分钟）</summary>
    public static double GetIdleTimeMinutes()
    {
        var info = new LASTINPUTINFO();
        info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LASTINPUTINFO>();
        if (GetLastInputInfo(ref info))
        {
            var currentTime = GetTickCount();
            // 处理 GetTickCount 溢出（约 49 天后回绕）
            var idleMs = currentTime >= info.dwTime
                ? currentTime - info.dwTime
                : (uint.MaxValue - info.dwTime) + currentTime;
            return idleMs / 60000.0;
        }
        return 0;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (!LockOnScreenLock) return;

        if (e.Reason == SessionSwitchReason.SessionLock ||
            e.Reason == SessionSwitchReason.ConsoleDisconnect)
        {
            if (Blocked()) return;
            IsLocked = true;
            AutoLockTriggered?.Invoke(AutoLockReason.SessionLock);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (!LockOnSleep) return;

        if (e.Mode == PowerModes.Suspend)
        {
            if (Blocked()) return;
            IsLocked = true;
            AutoLockTriggered?.Invoke(AutoLockReason.Sleep);
        }
    }

    /// <summary>更新配置</summary>
    public void UpdateSettings(int idleTimeoutMinutes, bool lockOnScreenLock, bool lockOnSleep)
    {
        IdleTimeoutMinutes = idleTimeoutMinutes;
        LockOnScreenLock = lockOnScreenLock;
        LockOnSleep = lockOnSleep;

        if (idleTimeoutMinutes > 0 && !_idleTimer.IsEnabled)
            _idleTimer.Start();
        else if (idleTimeoutMinutes <= 0 && _idleTimer.IsEnabled)
            _idleTimer.Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _idleTimer.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}

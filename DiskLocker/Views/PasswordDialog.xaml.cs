using System.Windows;
using DiskLocker.Native;
using DiskLocker.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace DiskLocker.Views;

/// <summary>
/// 密码解锁对话框（置顶显示）
/// </summary>
public partial class PasswordDialog : Window
{
    private readonly LockManager _lockManager;

    // 【D-14 修复】防暴破：跨对话框实例（同一应用会话内）累计失败次数与锁定
    private static int s_failedAttempts = 0;
    private static DateTime? s_lockoutUntil = null;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);

    public PasswordDialog(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;
        MicaHelper.Apply(this);
        Loaded += PasswordDialog_Loaded;
    }

    private void PasswordDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // 显示磁盘信息（多磁盘）
        var settings = _lockManager.Settings.Current;
        var count = settings.TargetDisks.Count;
        var modelList = count > 0
            ? string.Join("、", settings.TargetDisks
                .Select(t => string.IsNullOrEmpty(t.Model) ? "未知" : t.Model)
                .Distinct())
            : "目标硬盘";
        DiskInfoText.Text = count > 1
            ? $"{modelList}（{count} 个） — 已锁定"
            : $"{modelList} — 已锁定";

        // 自动聚焦到密码框
        PasswordBox.Focus();
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        AttemptUnlock();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AttemptUnlock();
        }
    }

    private void AttemptUnlock()
    {
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(password))
        {
            StatusText.Text = "请输入密码";
            return;
        }

        // 【D-14 修复】防暴破：检查是否处于锁定期
        if (s_lockoutUntil.HasValue)
        {
            var remaining = s_lockoutUntil.Value - DateTime.Now;
            if (remaining > TimeSpan.Zero)
            {
                StatusText.Text = $"尝试次数过多，请等待 {(int)Math.Ceiling(remaining.TotalSeconds)} 秒后再试";
                return;
            }
            s_lockoutUntil = null; // 锁定期已过，重置
        }

        // 刷新磁盘连接状态（防止瞬间误判）
        _lockManager.RefreshConnectionState();

        // 【D-12 修复】移除"未连接即拒绝"的前置硬拦截，统一走 UnlockDisk 内部回退逻辑，
        // 与托盘路径保持一致（即使轮询瞬时误判也能尝试恢复）
        // 验证密码并解锁
        var result = _lockManager.UnlockDisk(password);
        switch (result)
        {
            case UnlockResult.Success:
                // 【D-14 修复】解锁成功，重置失败计数
                s_failedAttempts = 0;
                s_lockoutUntil = null;
                DialogResult = true;
                Close();
                break;
            case UnlockResult.WrongPassword:
                // 【D-14 修复】失败计数 + 锁定
                s_failedAttempts++;
                if (s_failedAttempts >= MaxAttempts)
                {
                    s_lockoutUntil = DateTime.Now + LockoutDuration;
                    s_failedAttempts = 0;
                    StatusText.Text = $"密码错误次数过多，请等待 30 秒后再试";
                }
                else
                {
                    var remaining = MaxAttempts - s_failedAttempts;
                    StatusText.Text = $"密码错误，解锁失败（剩余 {remaining} 次尝试机会）";
                }
                PasswordBox.Clear();
                PasswordBox.Focus();
                break;
            case UnlockResult.RestoreFailed:
                StatusText.Text = "密码正确，但恢复盘符失败，请重试";
                PasswordBox.Clear();
                PasswordBox.Focus();
                break;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
                 
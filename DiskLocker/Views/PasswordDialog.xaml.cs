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

        // 检查磁盘是否已连接
        if (!_lockManager.IsTargetConnected)
        {
            StatusText.Text = "目标硬盘未连接，无需解锁";
            return;
        }

        // 验证密码并解锁
        var result = _lockManager.UnlockDisk(password);
        switch (result)
        {
            case UnlockResult.Success:
                DialogResult = true;
                Close();
                break;
            case UnlockResult.WrongPassword:
                StatusText.Text = "密码错误，解锁失败";
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
                 
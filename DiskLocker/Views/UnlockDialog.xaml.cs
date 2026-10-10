using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DiskLocker.Models;
using DiskLocker.Services;
using DiskLocker.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DiskLocker.Views;

/// <summary>
/// 解锁弹窗（设计稿 D1 默认态 / D2 密码错误 + 防暴破倒计时）。
/// targetSerial 非空时只解锁这一块硬盘，否则解锁全部。
/// </summary>
public partial class UnlockDialog : Window
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);

    // 跨弹窗实例累计：用热键反复呼出窗口也不能绕过防暴破
    private static int s_failedAttempts;
    private static DateTime? s_lockoutUntil;

    private readonly LockManager _lockManager;
    private readonly string? _targetSerial;
    private readonly DispatcherTimer _tickTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _reveal;

    public UnlockDialog(LockManager lockManager, string? targetSerial = null)
    {
        InitializeComponent();
        _lockManager = lockManager;
        _targetSerial = targetSerial;

        _tickTimer.Tick += (_, _) => UpdateLockoutCountdown();

        Loaded += (_, _) =>
        {
            DescribeDisks();
            ApplyVisual(showLockout: false);
            Pwd.Focus();
            Keyboard.Focus(Pwd);
            if (IsInLockout()) UpdateLockoutCountdown();
        };
        Closed += (_, _) => _tickTimer.Stop();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private string ScopeLabel =>
        string.IsNullOrEmpty(_targetSerial) ? "硬盘" : (_lockManager.Settings.Current.TargetDisks
            .FirstOrDefault(t => string.Equals(t.SerialNumber, _targetSerial, StringComparison.OrdinalIgnoreCase))
            ?.Model ?? "这块硬盘");

    private void DescribeDisks()
    {
        var targets = _lockManager.Settings.Current.TargetDisks;
        string text;
        if (!string.IsNullOrEmpty(_targetSerial))
        {
            var one = targets.FirstOrDefault(t =>
                string.Equals(t.SerialNumber, _targetSerial, StringComparison.OrdinalIgnoreCase));
            var letters = one != null ? _lockManager.Disk.GetDriveLettersForDisk(one.SerialNumber) : new List<string>();
            text = $"1 块硬盘 · {(letters.Count > 0 ? "盘符 " + DiskState.JoinLetters(letters) : "原盘符未知")}";
        }
        else
        {
            var letters = _lockManager.GetAllTargetDriveLetters();
            text = letters.Count > 0
                ? $"{targets.Count} 块硬盘 · 原盘符 {DiskState.JoinLetters(letters)}"
                : $"{targets.Count} 块硬盘 · 盘符已隐藏";
        }
        DiskPillText.Text = text;
    }

    private string Password => _reveal ? PwdVisible.Text : Pwd.Password;

    private static bool IsInLockout() =>
        s_lockoutUntil.HasValue && s_lockoutUntil.Value > DateTime.Now;

    private int LockoutSecondsLeft() =>
        s_lockoutUntil.HasValue
            ? Math.Max(0, (int)Math.Ceiling((s_lockoutUntil.Value - DateTime.Now).TotalSeconds))
            : 0;

    // ===================== 视觉状态 =====================

    private void ApplyVisual(bool showLockout)
    {
        var locked = showLockout || IsInLockout();

        if (locked)
        {
            Title = "密码错误，暂时无法重试";
            TitleText.Text = "密码错误，暂时无法重试";
            SubtitleText.Text = "为了保护硬盘，连续输错 5 次后会锁定 30 秒。";
            IconCircle.Background = FindBrush("PillDangerBgBrush");
            IconCircle.BorderBrush = FindBrush("PillDangerBorderBrush");
            IconGlyph.Foreground = FindBrush("DangerBrush");
            IconGlyph.Text = "\uE7BA";

            DiskPill.Background = FindBrush("PillWarnBgBrush");
            DiskPill.BorderBrush = FindBrush("PillWarnBorderBrush");
            DiskPillText.Foreground = FindBrush("PillWarnTextBrush");
            DiskPillDot.Fill = FindBrush("WarnBrush");

            Pwd.IsEnabled = false;
            PwdVisible.IsEnabled = false;
            EyeBtn.IsEnabled = false;
            ActionText.Text = "解锁";
            HintText.Text = "锁定结束后可以再次尝试，或按 Ctrl + Alt + L 随时呼出本窗口。";
        }
        else
        {
            Title = "解锁硬盘";
            TitleText.Text = $"输入密码解锁{ScopeLabel}";
            SubtitleText.Text = "输入你设置的密码，硬盘就能恢复正常使用。";
            IconCircle.Background = FindBrush("PillAccentBgBrush");
            IconCircle.BorderBrush = FindBrush("PillAccentBorderBrush");
            IconGlyph.Foreground = FindBrush("AccentBrush");
            IconGlyph.Text = "\uE72E";

            DiskPill.Background = FindBrush("PillAccentBgBrush");
            DiskPill.BorderBrush = FindBrush("PillAccentBorderBrush");
            DiskPillText.Foreground = FindBrush("PillAccentTextBrush");
            DiskPillDot.Fill = FindBrush("AccentBrush");

            Pwd.IsEnabled = true;
            PwdVisible.IsEnabled = _reveal;
            EyeBtn.IsEnabled = true;
            ActionText.Text = string.IsNullOrEmpty(_targetSerial) ? "解锁" : "解锁这块硬盘";
            HintText.Text = "连续输错 5 次会等待 30 秒才能重试，用于防止暴力破解。";
        }
    }

    private void UpdateLockoutCountdown()
    {
        var left = LockoutSecondsLeft();
        if (left <= 0)
        {
            _tickTimer.Stop();
            s_lockoutUntil = null;
            ApplyVisual(showLockout: false);
            ClearError();
            ActionButton.IsEnabled = true;
            ActionText.Text = string.IsNullOrEmpty(_targetSerial) ? "解锁" : "解锁这块硬盘";
            Pwd.Focus();
            return;
        }

        ApplyVisual(showLockout: true);
        ActionButton.IsEnabled = false;
        ActionText.Text = $"等待 {left} 秒";
        DiskPillText.Text = $"已锁定 · 剩余 {left} 秒";
        if (!_tickTimer.IsEnabled) _tickTimer.Start();
    }

    private void ShowError(string message)
    {
        ErrorRun.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        ErrorText.Foreground = FindBrush("DangerBrush");
        Pwd.Style = (Style)FindResource("PasswordInputInvalid");
    }

    private void ClearError()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        Pwd.Style = (Style)FindResource("PasswordInput");
    }

    private Brush FindBrush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;

    // ===================== 交互 =====================

    private void AttemptUnlock()
    {
        if (IsInLockout())
        {
            UpdateLockoutCountdown();
            return;
        }

        var password = Password;
        if (string.IsNullOrEmpty(password))
        {
            ShowError("请输入密码");
            return;
        }

        _lockManager.RefreshConnectionState();

        var result = _lockManager.UnlockDisk(password, _targetSerial);
        switch (result)
        {
            case UnlockResult.Success:
                s_failedAttempts = 0;
                s_lockoutUntil = null;
                DialogResult = true;
                Close();
                break;

            case UnlockResult.WrongPassword:
                s_failedAttempts++;
                _lockManager.Events.Record(ActivityKind.WrongPassword, ActivitySeverity.Error,
                    "解锁失败 · 密码错误",
                    $"密码不正确（{ScopeLabel}）");

                if (s_failedAttempts >= MaxAttempts)
                {
                    s_lockoutUntil = DateTime.Now + LockoutDuration;
                    s_failedAttempts = 0;
                    ShowError("密码错误，解锁失败（剩余 0 次尝试机会）");
                    ClearPassword();
                    UpdateLockoutCountdown();
                }
                else
                {
                    var remaining = MaxAttempts - s_failedAttempts;
                    ShowError($"密码错误，解锁失败（还有 {remaining} 次尝试机会）");
                    ClearPassword();
                    Pwd.Focus();
                }
                break;

            case UnlockResult.RestoreFailed:
                ShowError("密码正确，但恢复盘符失败，请重试");
                ClearPassword();
                Pwd.Focus();
                break;
        }
    }

    private void ClearPassword()
    {
        Pwd.Clear();
        PwdVisible.Clear();
    }

    private void Action_Click(object sender, RoutedEventArgs e) => AttemptUnlock();

    private void Pwd_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AttemptUnlock();
    }

    private void Pwd_Changed(object sender, RoutedEventArgs e)
    {
        if (Pwd.Password.Length > 0) ClearError();
    }

    private void PwdVisible_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (PwdVisible.Text.Length > 0) ClearError();
    }

    private void Eye_Click(object sender, RoutedEventArgs e)
    {
        _reveal = !_reveal;

        if (_reveal)
        {
            PwdVisible.Text = Pwd.Password;
            PwdVisible.Visibility = Visibility.Visible;
            Pwd.Visibility = Visibility.Collapsed;
            EyeBtn.Content = "\uE7B4";
            PwdVisible.Focus();
        }
        else
        {
            Pwd.Password = PwdVisible.Text;
            PwdVisible.Clear();
            PwdVisible.Visibility = Visibility.Collapsed;
            Pwd.Visibility = Visibility.Visible;
            EyeBtn.Content = "\uE7B3";
            Pwd.Focus();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

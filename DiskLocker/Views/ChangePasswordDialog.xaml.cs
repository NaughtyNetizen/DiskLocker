using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiskLocker.Services;
using Border = System.Windows.Controls.Border;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DiskLocker.Views;

/// <summary>
/// 修改密码弹窗（设计稿 D4）：把原先内联在设置页的三个密码框收敛成独立弹窗。
/// </summary>
public partial class ChangePasswordDialog : Window
{
    private readonly LockManager _lockManager;
    private readonly Border[] _segments;

    public ChangePasswordDialog(LockManager lockManager)
    {
        InitializeComponent();
        _lockManager = lockManager;
        _segments = new[] { Seg1, Seg2, Seg3, Seg4 };

        Loaded += (_, _) =>
        {
            OldPwd.Focus();
            Keyboard.Focus(OldPwd);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    public static bool Show(Window? owner, LockManager lockManager)
    {
        var dialog = new ChangePasswordDialog(lockManager)
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true;
    }

    // ===================== 强度条 =====================

    private void NewPwd_Changed(object sender, RoutedEventArgs e) => RenderStrength(NewPwd.Password);

    private void RenderStrength(string password)
    {
        var level = Score(password);
        var (label, brushKey) = level switch
        {
            1 => ("强度：弱", "DangerBrush"),
            2 => ("强度：中", "WarnBrush"),
            3 => ("强度：较强", "SuccessBrush"),
            4 => ("强度：很强", "SuccessBrush"),
            _ => ("强度：--", "TextMutedBrush")
        };

        StrengthText.Text = label;
        StrengthText.Foreground = (Brush)Application.Current.FindResource(brushKey);

        var filled = (Brush)Application.Current.FindResource(brushKey == "TextMutedBrush" ? "StrokeBrush" : brushKey);
        var empty = (Brush)Application.Current.FindResource("StrokeBrush");
        for (int i = 0; i < _segments.Length; i++)
            _segments[i].Background = i < level ? filled : empty;
    }

    private static int Score(string pwd)
    {
        if (string.IsNullOrEmpty(pwd)) return 0;

        var score = 0;
        if (pwd.Length >= 6) score++;
        if (pwd.Length >= 10) score++;
        if (pwd.Any(char.IsDigit) && pwd.Any(char.IsLetter)) score++;
        if (pwd.Any(c => !char.IsLetterOrDigit(c)) || (pwd.Any(char.IsUpper) && pwd.Any(char.IsLower))) score++;

        return Math.Clamp(score, 1, 4);
    }

    // ===================== 提交 =====================

    private void ConfirmPwd_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TrySave();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => TrySave();

    private void TrySave()
    {
        if (!_lockManager.Password.VerifyPassword(OldPwd.Password, _lockManager.Settings.Current.PasswordHash))
        {
            ShowError("当前密码不对，请重新输入");
            OldPwd.Focus();
            return;
        }

        if (!PasswordService.ValidatePasswordStrength(NewPwd.Password, out var error))
        {
            ShowError(error);
            NewPwd.Focus();
            return;
        }

        if (NewPwd.Password != ConfirmPwd.Password)
        {
            ShowError("两次输入的密码不一致，请重新输入。");
            ConfirmPwd.Focus();
            return;
        }

        if (_lockManager.UpdatePassword(NewPwd.Password))
        {
            DialogResult = true;
            Close();
        }
        else
        {
            ShowError("密码修改失败，请稍后再试");
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Foreground = (Brush)Application.Current.FindResource("DangerBrush");
        ErrorText.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

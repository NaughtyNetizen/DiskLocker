using System.Windows;
using System.Windows.Input;

namespace DiskLocker.Views;

/// <summary>
/// 通用确认弹窗（设计稿 D3）：文案先说清后果，破坏性按钮用危险色并与「取消」拉开距离。
/// </summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string confirmText, bool danger)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmBtn.Content = confirmText;

        if (!danger)
        {
            IconCircle.Background = FindBrush("PillAccentBgBrush");
            IconCircle.BorderBrush = FindBrush("PillAccentBorderBrush");
            IconGlyph.Foreground = FindBrush("AccentBrush");
            IconGlyph.Text = "\uE897"; // 信息
            ConfirmBtn.Style = (Style)FindResource("PrimaryButton");
        }

        Loaded += (_, _) =>
        {
            ConfirmBtn.Focus();
            Keyboard.Focus(ConfirmBtn);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
            else if (e.Key == Key.Enter) { DialogResult = true; Close(); }
        };
    }

    /// <summary>返回 true 表示用户确认</summary>
    public static bool Show(Window? owner, string title, string message, string confirmText, bool danger = true)
    {
        var dialog = new ConfirmDialog(title, message, confirmText, danger)
        {
            Owner = owner ?? Application.Current?.MainWindow
        };
        return dialog.ShowDialog() == true;
    }

    private System.Windows.Media.Brush FindBrush(string key) =>
        Application.Current.TryFindResource(key) as System.Windows.Media.Brush
        ?? System.Windows.Media.Brushes.Transparent;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

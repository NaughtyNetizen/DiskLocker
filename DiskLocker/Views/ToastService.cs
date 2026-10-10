using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using DiskLocker.Models;

namespace DiskLocker.Views;

/// <summary>
/// 右下角 Toast 通知（设计稿 T3）：4 秒自动消失，悬停暂停计时，点 × 立即关闭，多条自下向上堆叠。
/// </summary>
public static class ToastService
{
    private const double CardWidth = 380;
    private const double MarginFromEdge = 24;
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(4);

    private static Window? _host;
    private static StackPanel? _stack;

    /// <summary>从任意元素发起（自动回到 UI 线程）</summary>
    public static void Show(DependencyObject? context, ActivitySeverity severity, string title, string detail)
    {
        var dispatcher = context?.Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ShowCore(severity, title, detail));
            return;
        }
        ShowCore(severity, title, detail);
    }

    /// <summary>无界面上下文时（托盘 / 后台线程）直接发起</summary>
    public static void Show(ActivitySeverity severity, string title, string detail) =>
        Show(Application.Current?.MainWindow, severity, title, detail);

    private static void ShowCore(ActivitySeverity severity, string title, string detail)
    {
        var app = Application.Current;
        if (app == null) return;

        EnsureHost();
        if (_host == null || _stack == null) return;

        var card = BuildCard(severity, title, detail);
        _stack.Children.Add(card);

        card.Opacity = 0;
        card.RenderTransform = new TranslateTransform(0, 12);
        var enter = new Storyboard();
        enter.Children.Add(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        Storyboard.SetTarget(enter.Children[0], card);
        Storyboard.SetTargetProperty(enter.Children[0], new PropertyPath(UIElement.OpacityProperty));
        enter.Begin();

        // 最多同时保留 4 条，超出时立即移除最旧的
        while (_stack.Children.Count > 4)
            _stack.Children.RemoveAt(0);

        PositionHost();
        _host.Show();
    }

    private static Border BuildCard(ActivitySeverity severity, string title, string detail)
    {
        var key = severity switch
        {
            ActivitySeverity.Success => "Success",
            ActivitySeverity.Warning => "Warn",
            ActivitySeverity.Error => "Danger",
            _ => "Accent"
        };
        var glyph = severity switch
        {
            ActivitySeverity.Success => "\uE73E",
            ActivitySeverity.Warning => "\uE7BA",
            ActivitySeverity.Error => "\uEA39",
            _ => "\uE946"
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Grid
        {
            Width = 28,
            Height = 28,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        icon.Children.Add(new Ellipse
        {
            Fill = Brush($"Pill{key}BgBrush"),
            Stroke = Brush($"Pill{key}BorderBrush"),
            StrokeThickness = 1
        });
        icon.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = Brush(key == "Accent" ? "AccentBrush" : $"{key}Brush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.FindResource("CardTitle")
        });
        if (!string.IsNullOrWhiteSpace(detail))
        {
            texts.Children.Add(new TextBlock
            {
                Text = detail,
                Style = (Style)Application.Current.FindResource("MetaText"),
                Margin = new Thickness(0, 3, 0, 0),
                MaxHeight = 46,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var card = new Border
        {
            Width = CardWidth,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(16, 13, 12, 13),
            CornerRadius = (CornerRadius)Application.Current.FindResource("SettingCardRadius"),
            Background = Brush("CardBrush"),
            BorderBrush = Brush($"Pill{key}BorderBrush"),
            BorderThickness = new Thickness(1),
            Effect = (Effect)Application.Current.FindResource("FlyoutShadow"),
            Child = grid
        };

        var close = new Button
        {
            Style = (Style)Application.Current.FindResource("IconButton"),
            Width = 26,
            Height = 26,
            Content = "\uE8BB",
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, -2, -4, 0)
        };
        close.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        // 4 秒自动消失；鼠标悬停暂停，移开后短延时再消失
        var timer = new DispatcherTimer { Interval = DefaultLifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Dismiss(card);
        };

        card.MouseEnter += (_, _) => timer.Stop();
        card.MouseLeave += (_, _) =>
        {
            timer.Interval = TimeSpan.FromSeconds(1.5);
            timer.Start();
        };
        close.Click += (_, _) =>
        {
            timer.Stop();
            Dismiss(card);
        };
        timer.Start();

        return card;
    }

    private static void Dismiss(Border card)
    {
        if (_stack == null || !ReferenceEquals(card.Parent, _stack)) return;

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) =>
        {
            _stack.Children.Remove(card);
            if (_stack.Children.Count == 0) HideHost();
            else PositionHost();
        };
        card.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static void EnsureHost()
    {
        if (_host != null) return;

        _stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };

        var root = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
        root.Children.Add(_stack);

        _host = new Window
        {
            Width = CardWidth + 8,
            Height = 460,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            ResizeMode = ResizeMode.NoResize,
            Content = root
        };

        // 不抢焦点：让 toast 浮在最上但不打断用户当前输入
        _host.SourceInitialized += (_, _) => Native.ToastExStyle.ApplyNoActivate(_host);
    }

    private static void PositionHost()
    {
        if (_host == null) return;
        var wa = SystemParameters.WorkArea;
        _host.Left = wa.Right - _host.Width - (MarginFromEdge - 8);
        _host.Top = wa.Bottom - _host.Height - MarginFromEdge;
    }

    private static void HideHost()
    {
        _host?.Hide();
    }

    private static Brush Brush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;
}

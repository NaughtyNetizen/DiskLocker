using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace DiskLocker.Views;

/// <summary>一个菜单项：文字、图标、点击动作，以及可选的右侧提示（如快捷键）</summary>
public record MenuSpec(
    string Text,
    string? Glyph,
    Action? OnClick,
    string? Hint = null,
    bool Danger = false,
    bool Enabled = true);

/// <summary>菜单顶部的状态区（托盘菜单用）</summary>
public record MenuHeader(string Title, string Subtitle, Brush TitleBrush);

/// <summary>
/// 自绘下拉/弹出菜单：统一的深色卡片外观，替代系统菜单样式。
/// 同一份面板内容既能挂到控件锚点（硬盘卡「更多」），也能塞进无边框窗口（系统托盘右键）。
/// </summary>
public static class FlyoutMenu
{
    private const double ItemHeight = 36;

    public static Border BuildPanel(IReadOnlyList<MenuSpec> items, MenuHeader? header, double width)
    {
        var panel = new StackPanel();

        if (header != null)
        {
            panel.Children.Add(new StackPanel
            {
                Margin = new Thickness(13, 12, 13, 10),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new Ellipse
                            {
                                Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0),
                                VerticalAlignment = VerticalAlignment.Center,
                                Fill = header.TitleBrush
                            },
                            new TextBlock
                            {
                                Text = header.Title,
                                FontSize = 13, FontWeight = FontWeights.SemiBold,
                                Foreground = header.TitleBrush,
                                VerticalAlignment = VerticalAlignment.Center
                            }
                        }
                    },
                    new TextBlock
                    {
                        Text = header.Subtitle,
                        Style = (Style)Application.Current.FindResource("MetaText"),
                        Margin = new Thickness(16, 3, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            });
            panel.Children.Add(new Border { Style = (Style)Application.Current.FindResource("MenuSeparator") });
        }

        for (int i = 0; i < items.Count; i++)
        {
            var spec = items[i];
            if (spec.Text == "-")
            {
                panel.Children.Add(new Border { Style = (Style)Application.Current.FindResource("MenuSeparator") });
                continue;
            }
            panel.Children.Add(BuildItem(spec));
        }

        return new Border
        {
            Width = width,
            Background = (Brush)Application.Current.FindResource("CardBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("StrokeStrongBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.FindResource("SettingCardRadius"),
            Padding = new Thickness(5),
            Effect = (Effect)Application.Current.FindResource("FlyoutShadow"),
            Child = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false
            }
        };
    }

    private static Button BuildItem(MenuSpec spec)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textBlock = new TextBlock
        {
            Text = spec.Text,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(textBlock, 1);

        if (spec.Glyph != null)
        {
            var glyph = new TextBlock
            {
                Text = spec.Glyph,
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 14,
                Width = 22,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(glyph, 0);
            grid.Children.Add(glyph);
        }

        if (spec.Hint != null)
        {
            var hint = new TextBlock
            {
                Text = spec.Hint,
                FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("MonoFontFamily"),
                FontSize = 11,
                Margin = new Thickness(14, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
        }

        grid.Children.Add(textBlock);

        var button = new Button
        {
            Style = (Style)Application.Current.FindResource(spec.Danger ? "MenuDangerButton" : "MenuButton"),
            Content = grid,
            Height = ItemHeight,
            IsEnabled = spec.Enabled,
            Cursor = spec.Enabled ? Cursors.Hand : Cursors.Arrow
        };
        button.SetResourceReference(TextBlock.ForegroundProperty,
            spec.Danger ? "DangerBrush" : "TextPrimaryBrush");

        if (spec.OnClick != null)
        {
            var action = spec.OnClick;
            button.Click += (_, _) =>
            {
                var popup = FindPopup(button);
                popup?.SetCurrentValue(Popup.IsOpenProperty, false);
                action();
            };
        }
        return button;
    }

    private static Popup? FindPopup(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is Popup p) return p;
            d = VisualTreeHelper.GetParent(d)
                ?? LogicalTreeHelper.GetParent(d)
                ?? (d as FrameworkElement)?.Parent as DependencyObject;
        }
        return null;
    }

    /// <summary>挂到控件下方弹出</summary>
    public static void Show(FrameworkElement anchor, IReadOnlyList<MenuSpec> items, double width = 240)
    {
        var popup = new Popup
        {
            Child = BuildPanel(items, null, width),
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            AllowsTransparency = true,
            StaysOpen = false,
            HorizontalOffset = -4,
            VerticalOffset = 4
        };
        popup.IsOpen = true;
    }
}

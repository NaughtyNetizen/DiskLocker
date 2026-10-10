using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace DiskLocker.Behaviors;

/// <summary>
/// 附加属性：支持 ModernWindow 模板中的标题栏拖动和关闭按钮功能
/// </summary>
public static class WindowBehavior
{
    /// <summary>设为 True 的元素可拖动窗口</summary>
    public static readonly DependencyProperty IsDragMoveEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsDragMoveEnabled",
            typeof(bool),
            typeof(WindowBehavior),
            new PropertyMetadata(false, OnIsDragMoveEnabledChanged));

    public static bool GetIsDragMoveEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsDragMoveEnabledProperty);

    public static void SetIsDragMoveEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsDragMoveEnabledProperty, value);

    private static void OnIsDragMoveEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            if ((bool)e.NewValue)
                element.MouseLeftButtonDown += DragMoveOnMouseDown;
            else
                element.MouseLeftButtonDown -= DragMoveOnMouseDown;
        }
    }

    private static void DragMoveOnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && sender is DependencyObject d)
        {
            if (Window.GetWindow(d) is Window window)
                window.DragMove();
        }
    }

    /// <summary>设为 True 的 Button 点击时关闭所在窗口</summary>
    public static readonly DependencyProperty IsCloseButtonProperty =
        DependencyProperty.RegisterAttached(
            "IsCloseButton",
            typeof(bool),
            typeof(WindowBehavior),
            new PropertyMetadata(false, OnIsCloseButtonChanged));

    public static bool GetIsCloseButton(DependencyObject obj) =>
        (bool)obj.GetValue(IsCloseButtonProperty);

    public static void SetIsCloseButton(DependencyObject obj, bool value) =>
        obj.SetValue(IsCloseButtonProperty, value);

    private static void OnIsCloseButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Button button)
        {
            if ((bool)e.NewValue)
                button.Click += CloseOnClick;
            else
                button.Click -= CloseOnClick;
        }
    }

    private static void CloseOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject d)
        {
            if (Window.GetWindow(d) is Window window)
                window.Close();
        }
    }

    // ===================== 标题栏最小化 / 最大化按钮 =====================

    public static readonly DependencyProperty IsMinimizeButtonProperty =
        DependencyProperty.RegisterAttached(
            "IsMinimizeButton", typeof(bool), typeof(WindowBehavior),
            new PropertyMetadata(false, OnMinimizeButtonChanged));

    public static bool GetIsMinimizeButton(DependencyObject obj) =>
        (bool)obj.GetValue(IsMinimizeButtonProperty);

    public static void SetIsMinimizeButton(DependencyObject obj, bool value) =>
        obj.SetValue(IsMinimizeButtonProperty, value);

    private static void OnMinimizeButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Button button)
        {
            if ((bool)e.NewValue)
                button.Click += MinimizeOnClick;
            else
                button.Click -= MinimizeOnClick;
        }
    }

    private static void MinimizeOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject d && Window.GetWindow(d) is Window window)
            window.WindowState = WindowState.Minimized;
    }

    public static readonly DependencyProperty IsMaximizeButtonProperty =
        DependencyProperty.RegisterAttached(
            "IsMaximizeButton", typeof(bool), typeof(WindowBehavior),
            new PropertyMetadata(false, OnMaximizeButtonChanged));

    public static bool GetIsMaximizeButton(DependencyObject obj) =>
        (bool)obj.GetValue(IsMaximizeButtonProperty);

    public static void SetIsMaximizeButton(DependencyObject obj, bool value) =>
        obj.SetValue(IsMaximizeButtonProperty, value);

    private static void OnMaximizeButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Button button)
        {
            if ((bool)e.NewValue)
                button.Click += MaximizeOnClick;
            else
                button.Click -= MaximizeOnClick;
        }
    }

    private static void MaximizeOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject d && Window.GetWindow(d) is Window window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }

    // ===================== 边缘拖拽缩放（无边框窗口，WM_NCHITTEST） =====================

    private const int WM_NCHITTEST = 0x0084;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int ResizeBorderWidth = 8;

    /// <summary>设为 True 的窗口可通过拖动边缘缩放（配合 WindowStyle=None）</summary>
    public static readonly DependencyProperty EnableResizeBorderProperty =
        DependencyProperty.RegisterAttached(
            "EnableResizeBorder",
            typeof(bool),
            typeof(WindowBehavior),
            new PropertyMetadata(false, OnEnableResizeBorderChanged));

    public static bool GetEnableResizeBorder(DependencyObject obj) =>
        (bool)obj.GetValue(EnableResizeBorderProperty);

    public static void SetEnableResizeBorder(DependencyObject obj, bool value) =>
        obj.SetValue(EnableResizeBorderProperty, value);

    private static void OnEnableResizeBorderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && (bool)e.NewValue)
        {
            window.SourceInitialized += (_, _) => AttachResizeHook(window);
        }
    }

    private static void AttachResizeHook(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook((IntPtr h, int m, IntPtr w, IntPtr l, ref bool handled) => ResizeHook(window, m, l, ref handled));
    }

    private static IntPtr ResizeHook(Window window, int msg, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST)
            return IntPtr.Zero;

        // 不允许缩放的窗口（如对话框）不提供缩放边框
        if (window.ResizeMode == ResizeMode.NoResize)
            return IntPtr.Zero;

        // lParam 低字为屏幕 X，高字为屏幕 Y
        int screenX = (short)((long)lParam & 0xFFFF);
        int screenY = (short)(((long)lParam >> 16) & 0xFFFF);

        Point pt;
        try
        {
            pt = window.PointFromScreen(new Point(screenX, screenY));
        }
        catch
        {
            return IntPtr.Zero;
        }

        bool left = pt.X < ResizeBorderWidth;
        bool right = pt.X > window.ActualWidth - ResizeBorderWidth;
        bool top = pt.Y < ResizeBorderWidth;
        bool bottom = pt.Y > window.ActualHeight - ResizeBorderWidth;

        if (top && left) { handled = true; return (IntPtr)HTTOPLEFT; }
        if (top && right) { handled = true; return (IntPtr)HTTOPRIGHT; }
        if (bottom && left) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
        if (bottom && right) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
        if (left) { handled = true; return (IntPtr)HTLEFT; }
        if (right) { handled = true; return (IntPtr)HTRIGHT; }
        if (top) { handled = true; return (IntPtr)HTTOP; }
        if (bottom) { handled = true; return (IntPtr)HTBOTTOM; }

        return IntPtr.Zero;
    }
}

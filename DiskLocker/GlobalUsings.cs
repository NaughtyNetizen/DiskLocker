// 本项目同时启用 UseWPF 与 UseWindowsForms（托盘用 NotifyIcon），
// 隐式全局 using 会同时引入 System.Windows.Forms / System.Drawing，
// 导致 Application、Brush、Button、ComboBox 等类型全部二义（CS0104）。
// 这里统一把常用简单名指向 WPF 版本；确实需要 GDI 类型的文件在文件内再加别名覆盖。
global using Application = System.Windows.Application;
global using Clipboard = System.Windows.Clipboard;
global using MessageBox = System.Windows.MessageBox;
global using Binding = System.Windows.Data.Binding;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Color = System.Windows.Media.Color;
global using Cursors = System.Windows.Input.Cursors;
global using FontFamily = System.Windows.Media.FontFamily;
global using Point = System.Windows.Point;
global using Size = System.Windows.Size;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
global using Button = System.Windows.Controls.Button;
global using CheckBox = System.Windows.Controls.CheckBox;
global using ComboBox = System.Windows.Controls.ComboBox;
global using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
global using Label = System.Windows.Controls.Label;
global using ListBox = System.Windows.Controls.ListBox;
global using ListBoxItem = System.Windows.Controls.ListBoxItem;
global using Panel = System.Windows.Controls.Panel;
global using RadioButton = System.Windows.Controls.RadioButton;
global using TextBox = System.Windows.Controls.TextBox;
global using UserControl = System.Windows.Controls.UserControl;
global using Orientation = System.Windows.Controls.Orientation;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment = System.Windows.VerticalAlignment;

using Microsoft.Win32;

namespace DiskLocker.Services;

/// <summary>
/// 开机自启动服务（通过注册表 Run 键）
/// </summary>
public class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "DiskLocker";

    /// <summary>当前应用可执行文件路径</summary>
    private static string AppPath =>
        Environment.ProcessPath ??
        System.IO.Path.Combine(AppContext.BaseDirectory, "DiskLocker.exe");

    /// <summary>检查是否已设置开机自启</summary>
    public bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(AppName)?.ToString();
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>启用开机自启</summary>
    public bool EnableAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key?.SetValue(AppName, $"\"{AppPath}\"");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>禁用开机自启</summary>
    public bool DisableAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>根据开关设置自启状态</summary>
    public void SetAutoStart(bool enabled)
    {
        if (enabled)
            EnableAutoStart();
        else
            DisableAutoStart();
    }
}

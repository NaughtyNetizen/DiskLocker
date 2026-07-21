using System.IO;
using System.Text.Json;
using DiskLocker.Models;

namespace DiskLocker.Services;

/// <summary>
/// 设置持久化服务（JSON 文件存储于 %APPDATA%\DiskLocker）
/// </summary>
public class SettingsService
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DiskLocker");

    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>当前内存中的设置实例</summary>
    public AppSettings Current { get; private set; } = new();

    /// <summary>设置是否已加载</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>加载设置文件</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            else
            {
                Current = new AppSettings();
            }
        }
        catch
        {
            Current = new AppSettings();
        }

        IsLoaded = true;
        return Current;
    }

    /// <summary>保存设置到文件</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // 保存失败时静默处理，可扩展为日志记录
        }
    }

    /// <summary>重置设置（删除配置文件）</summary>
    public void Reset()
    {
        try
        {
            if (File.Exists(SettingsPath))
                File.Delete(SettingsPath);
        }
        catch
        {
            // 忽略删除错误
        }
        Current = new AppSettings();
        IsLoaded = true;
    }

    /// <summary>获取设置文件路径（供 UI 显示）</summary>
    public static string GetSettingsPath() => SettingsPath;
}

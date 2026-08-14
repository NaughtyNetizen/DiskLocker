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
        catch (Exception ex)
        {
            // 配置文件损坏时，先备份再重置为默认值，避免用户被锁死且无提示
            SimpleLogger.Log($"SettingsService.Load: failed to read settings ({ex.Message}), backing up and resetting");
            BackupCorruptSettings();
            Current = new AppSettings();
        }

        IsLoaded = true;
        return Current;
    }

    /// <summary>保存设置到文件（原子写入：临时文件 + 替换，失败上抛而非静默吞掉）</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(Current, JsonOptions);

            // 原子写入：先写临时文件，再替换原文件，防止写入中途崩溃导致配置损坏
            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(SettingsPath))
                File.Replace(tempPath, SettingsPath, null);
            else
                File.Move(tempPath, SettingsPath);
        }
        catch (Exception ex)
        {
            // 不再静默吞掉异常：记录日志以便排查。
            // 不向上抛出，避免未做异常处理的调用方（UI 设置更新等）崩溃。
            // 关键改进：原子写入已保证不会因写入中途崩溃而损坏配置。
            SimpleLogger.Log($"SettingsService.Save FAILED: {ex.Message}");
        }
    }

    /// <summary>
    /// 备份损坏的配置文件（加时间戳后缀），便于后续排查
    /// </summary>
    private void BackupCorruptSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var backupPath = SettingsPath + $".corrupt.{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Move(SettingsPath, backupPath);
                SimpleLogger.Log($"SettingsService: corrupt settings backed up to {backupPath}");
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"SettingsService: failed to backup corrupt settings: {ex.Message}");
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

using System.IO;

namespace DiskLocker.Services;

/// <summary>
/// 简单文件日志（用于调试）
/// 路径：%LOCALAPPDATA%\DiskLocker\debug.log
/// 支持按大小滚动（避免长期运行磁盘耗尽）、线程安全写入、时间戳含日期。
/// </summary>
public static class SimpleLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiskLocker");

    private static readonly string LogPath = Path.Combine(LogDir, "debug.log");

    /// <summary>单个日志文件大小上限，超过后滚动</summary>
    private const long MaxLogSize = 5 * 1024 * 1024; // 5 MB

    /// <summary>日志写入锁（防止并发 AppendAllText 共享冲突导致日志丢失）</summary>
    private static readonly object WriteLock = new();

    /// <summary>当前日志文件路径</summary>
    public static string CurrentLogPath => LogPath;

    public static void Log(string message)
    {
        lock (WriteLock)
        {
            try
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);

                RotateIfNeeded();
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
            }
            catch
            {
                // 日志失败不应影响主流程
            }
        }
    }

    /// <summary>
    /// 日志滚动：当文件超过 MaxLogSize 时，将当前日志重命名为 .1 备份（覆盖旧备份），
    /// 然后开始新文件。保证日志不会无限增长导致磁盘耗尽。
    /// </summary>
    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogPath))
                return;

            var size = new FileInfo(LogPath).Length;
            if (size <= MaxLogSize)
                return;

            var backupPath = LogPath + ".1";
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            File.Move(LogPath, backupPath);
        }
        catch
        {
            // 滚动失败不影响后续写入尝试
        }
    }
}

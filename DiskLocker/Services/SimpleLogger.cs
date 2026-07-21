using System.IO;

namespace DiskLocker.Services;

/// <summary>
/// 简单文件日志（用于调试）
/// </summary>
public static class SimpleLogger
{
    private static readonly string LogPath = @"d:\DiskLocker\debug.log";

    public static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
        }
        catch { }
    }
}

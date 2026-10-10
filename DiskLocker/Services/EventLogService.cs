using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiskLocker.Models;

namespace DiskLocker.Services;

/// <summary>
/// 本地活动记录：JSONL 追加写入 %APPDATA%\DiskLocker\events.jsonl。
/// 供「活动记录」页展示、统计与导出；写入失败只记调试日志，绝不影响锁定主流程。
/// </summary>
public class EventLogService
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DiskLocker");

    private static readonly string LogPath = Path.Combine(DataDir, "events.jsonl");

    /// <summary>超过该条数时裁剪到 KeepCount，避免文件无限增长</summary>
    private const int TrimThreshold = 1500;
    private const int KeepCount = 1000;

    private static readonly object WriteLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string CurrentLogPath => LogPath;

    /// <summary>新记录写入后触发，活动记录页据此刷新</summary>
    public event Action? EntryAppended;

    public ActivityEvent Record(ActivityKind kind, ActivitySeverity severity, string title,
                                string detail = "", string? serial = null)
    {
        var entry = new ActivityEvent
        {
            Kind = kind,
            Severity = severity,
            Title = title,
            Detail = detail,
            Serial = serial
        };

        try
        {
            lock (WriteLock)
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(LogPath,
                    JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);

                if (CountLines() > TrimThreshold)
                    Trim();
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"EventLogService.Record FAILED: {ex.Message}");
        }

        EntryAppended?.Invoke();
        return entry;
    }

    /// <summary>倒序读取全部记录（最新在前）</summary>
    public List<ActivityEvent> LoadAll()
    {
        try
        {
            lock (WriteLock)
            {
                if (!File.Exists(LogPath))
                    return new List<ActivityEvent>();

                var lines = File.ReadAllLines(LogPath);
                var list = new List<ActivityEvent>(lines.Length);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var e = JsonSerializer.Deserialize<ActivityEvent>(line, JsonOptions);
                        if (e != null) list.Add(e);
                    }
                    catch
                    {
                        // 单行损坏不影响其余记录
                    }
                }
                list.Reverse();
                return list;
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"EventLogService.LoadAll FAILED: {ex.Message}");
            return new List<ActivityEvent>();
        }
    }

    /// <summary>按类型与时间范围筛选（kind 为 null 表示全部；days 为 null 表示不限）</summary>
    public List<ActivityEvent> Query(ActivityKind? kind, int? days)
    {
        var cutoff = days.HasValue ? DateTime.Now.AddDays(-days.Value) : DateTime.MinValue;
        return LoadAll()
            .Where(e => (kind == null || e.Kind == kind) && e.Time >= cutoff)
            .ToList();
    }

    /// <summary>今日事件总数</summary>
    public int CountToday(IEnumerable<ActivityEvent> all) =>
        all.Count(e => e.Time.Date == DateTime.Today);

    /// <summary>近 N 天某类事件数</summary>
    public int CountRecent(IEnumerable<ActivityEvent> all, ActivityKind kind, int days) =>
        all.Count(e => e.Kind == kind && e.Time >= DateTime.Now.AddDays(-days));

    /// <summary>导出为 CSV（UTF-8 BOM，Excel 直接可读）</summary>
    public bool ExportCsv(List<ActivityEvent> entries, string path)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("时间,类型,级别,标题,详情,序列号");
            foreach (var e in entries.OrderByDescending(x => x.Time))
            {
                sb.AppendLine(string.Join(',',
                    Csv(e.FullTimeText),
                    Csv(ActivityEvent.KindLabel(e.Kind)),
                    Csv(e.Severity.ToString()),
                    Csv(e.Title),
                    Csv(e.Detail),
                    Csv(e.Serial ?? "")));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            SimpleLogger.Log($"EventLogService.ExportCsv: wrote {entries.Count} entries to {path}");
            return true;
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"EventLogService.ExportCsv FAILED: {ex.Message}");
            return false;
        }
    }

    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    private int CountLines()
    {
        if (!File.Exists(LogPath)) return 0;
        try
        {
            using var sr = new StreamReader(LogPath);
            int n = 0;
            while (sr.ReadLine() != null) n++;
            return n;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>保留最近 KeepCount 条（原子替换）</summary>
    private void Trim()
    {
        try
        {
            var lines = File.ReadAllLines(LogPath);
            if (lines.Length <= KeepCount) return;

            var kept = lines.Skip(lines.Length - KeepCount).ToArray();
            var tempPath = LogPath + ".tmp";
            File.WriteAllLines(tempPath, kept);
            File.Replace(tempPath, LogPath, null);
            SimpleLogger.Log($"EventLogService.Trim: {lines.Length} -> {kept.Length} entries");
        }
        catch (Exception ex)
        {
            SimpleLogger.Log($"EventLogService.Trim FAILED: {ex.Message}");
        }
    }
}

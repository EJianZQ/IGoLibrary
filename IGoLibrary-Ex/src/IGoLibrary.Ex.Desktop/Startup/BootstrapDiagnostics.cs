using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Logging;
using IGoLibrary.Ex.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Startup;

internal static class BootstrapDiagnostics
{
    private const int MaximumBufferedEntries = 128;
    private const long MaximumEmergencyLogBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly object Gate = new();
    private static readonly List<BufferedEntry> Entries = [];
    private static IAppLogWriter? _writer;

    public static void Record(LogLevel level, string message, Exception? exception = null)
    {
        IAppLogWriter? writer;
        lock (Gate)
        {
            writer = _writer;
            if (writer is null)
            {
                if (Entries.Count >= MaximumBufferedEntries)
                {
                    Entries.RemoveAt(0);
                }

                Entries.Add(new BufferedEntry(DateTimeOffset.Now, level, message, exception));
            }
        }

        if (writer is not null)
        {
            try
            {
                writer.Write(level, "Bootstrap", message, exception);
            }
            catch (Exception writeException)
            {
                WriteEmergency(
                    LogLevel.Error,
                    "调用主日志写入器失败，原始诊断已转存到应急日志。",
                    writeException);
                WriteEmergency(level, message, exception);
            }
            return;
        }

        if (level >= LogLevel.Error)
        {
            WriteEmergency(level, message, exception);
        }
    }

    public static void RecordEmergency(
        LogLevel level,
        string message,
        Exception? exception = null)
    {
        WriteEmergency(level, message, exception);
    }

    public static void Attach(AppLogFileWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        List<BufferedEntry> buffered;
        lock (Gate)
        {
            _writer = writer;
            buffered = [.. Entries];
            Entries.Clear();
        }

        writer.HealthChanged += OnWriterHealthChanged;
        foreach (var entry in buffered)
        {
            writer.Write(
                entry.Level,
                "Bootstrap",
                entry.Message,
                entry.Exception,
                timestamp: entry.Timestamp);
        }
    }

    private static void OnWriterHealthChanged(object? sender, AppLogWriterHealthChangedEventArgs args)
    {
        var level = args.IsHealthy ? LogLevel.Information : LogLevel.Error;
        var message = args.IsHealthy
            ? $"主日志写入已恢复：{args.Operation}。"
            : $"主日志写入异常：{args.Operation}，连续失败 {args.ConsecutiveFailureCount} 次；{args.ErrorMessage}";
        WriteEmergency(level, message, exception: null);
    }

    private static void WriteEmergency(LogLevel level, string message, Exception? exception)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IGoLibrary-Ex",
                "logs");
            Directory.CreateDirectory(logDirectory);
            var path = Path.Combine(logDirectory, "bootstrap-emergency.log");
            if (File.Exists(path) && new FileInfo(path).Length > MaximumEmergencyLogBytes)
            {
                File.WriteAllText(
                    path,
                    $"{DateTimeOffset.Now:O} [INF] Bootstrap - 应急日志超过大小限制，已开始新的记录。{Environment.NewLine}",
                    Utf8NoBom);
            }

            var detail = exception is null
                ? string.Empty
                : $" | {exception}";
            var line = AppLogSanitizer.Sanitize(
                $"{DateTimeOffset.Now:O} [{GetLevelCode(level)}] Bootstrap - {message}{detail}");
            File.AppendAllText(path, line.ReplaceLineEndings("\\n") + Environment.NewLine, Utf8NoBom);
        }
        catch
        {
        }
    }

    private static string GetLevelCode(LogLevel level)
    {
        return level switch
        {
            LogLevel.Critical => "CRT",
            LogLevel.Error => "ERR",
            LogLevel.Warning => "WRN",
            LogLevel.Information => "INF",
            _ => "DBG"
        };
    }

    private sealed record BufferedEntry(
        DateTimeOffset Timestamp,
        LogLevel Level,
        string Message,
        Exception? Exception);
}

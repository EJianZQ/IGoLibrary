using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Logging;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Application.Services;

public sealed class ActivityLogService(IAppLogWriter? logWriter = null) : IActivityLogService
{
    private readonly List<AppLogEntry> _entries = [];
    private readonly object _gate = new();

    public event EventHandler<AppLogEntry>? EntryWritten;

    public IReadOnlyList<AppLogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }
    }

    public void Write(
        LogEntryKind kind,
        string category,
        string message,
        Exception? exception = null,
        EventId eventId = default)
    {
        var sanitizedCategory = AppLogSanitizer.Sanitize(category);
        var sanitizedMessage = AppLogSanitizer.Sanitize(message);
        var entry = new AppLogEntry(
            DateTimeOffset.Now,
            kind,
            sanitizedCategory,
            sanitizedMessage);
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > 500)
            {
                _entries.RemoveRange(0, _entries.Count - 500);
            }
        }

        List<Exception>? subscriberFailures = null;
        var handlers = EntryWritten;
        if (handlers is not null)
        {
            foreach (EventHandler<AppLogEntry> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, entry);
                }
                catch (Exception subscriberException)
                {
                    subscriberFailures ??= [];
                    subscriberFailures.Add(subscriberException);
                }
            }
        }

        try
        {
            logWriter?.Write(
                MapLogLevel(kind),
                $"Activity.{NormalizeCategory(sanitizedCategory)}",
                sanitizedMessage,
                exception,
                eventId,
                timestamp: entry.Timestamp);
        }
        catch
        {
        }

        if (subscriberFailures is null)
        {
            return;
        }

        foreach (var subscriberException in subscriberFailures)
        {
            try
            {
                logWriter?.Write(
                    LogLevel.Warning,
                    "ActivityLog",
                    "活动日志订阅者处理失败，已隔离该异常。",
                    subscriberException,
                    new EventId(1001, "ActivitySubscriberFailed"),
                    entry.Timestamp);
            }
            catch
            {
            }
        }
    }

    private static string NormalizeCategory(string category)
    {
        return string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
    }

    private static LogLevel MapLogLevel(LogEntryKind kind)
    {
        return kind switch
        {
            LogEntryKind.Success => LogLevel.Information,
            LogEntryKind.Warning => LogLevel.Warning,
            LogEntryKind.Error => LogLevel.Error,
            _ => LogLevel.Information
        };
    }
}

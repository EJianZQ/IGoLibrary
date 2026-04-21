using IGoLibrary.Ex.Application.Abstractions;
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

    public void Write(LogEntryKind kind, string category, string message)
    {
        var entry = new AppLogEntry(DateTimeOffset.Now, kind, category, message);
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > 500)
            {
                _entries.RemoveRange(0, _entries.Count - 500);
            }
        }

        EntryWritten?.Invoke(this, entry);

        try
        {
            logWriter?.Write(
                MapLogLevel(kind),
                $"Activity.{NormalizeCategory(category)}",
                message,
                timestamp: entry.Timestamp);
        }
        catch
        {
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

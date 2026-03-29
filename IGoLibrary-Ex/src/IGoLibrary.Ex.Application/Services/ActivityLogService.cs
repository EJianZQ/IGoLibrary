using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class ActivityLogService : IActivityLogService
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
    }
}

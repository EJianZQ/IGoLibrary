using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IActivityLogService
{
    event EventHandler<AppLogEntry>? EntryWritten;

    IReadOnlyList<AppLogEntry> Entries { get; }

    void Write(LogEntryKind kind, string category, string message);
}

using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IActivityLogService
{
    event EventHandler<AppLogEntry>? EntryWritten;

    IReadOnlyList<AppLogEntry> Entries { get; }

    void Write(
        LogEntryKind kind,
        string category,
        string message,
        Exception? exception = null,
        EventId eventId = default);
}

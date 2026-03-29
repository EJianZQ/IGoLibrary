using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record AppLogEntry(
    DateTimeOffset Timestamp,
    LogEntryKind Kind,
    string Category,
    string Message);

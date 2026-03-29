using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record SessionCredentials(
    string Cookie,
    SessionSource Source,
    DateTimeOffset SavedAt,
    bool CanAutoRestore);

using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record CheckInGuardPlan(
    DateTimeOffset Deadline,
    TimeSpan ReminderLeadTime,
    CheckInGuardMissedAction MissedAction,
    TimeSpan ReReserveDelay,
    int? FallbackLibraryId,
    string? FallbackLibraryName,
    TimeSpan CheckInRequiredWithin,
    TimeSpan MissedActionLeadTime);

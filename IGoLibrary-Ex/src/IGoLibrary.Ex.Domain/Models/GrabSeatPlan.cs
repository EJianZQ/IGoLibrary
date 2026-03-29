using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record GrabSeatPlan(
    int LibraryId,
    string LibraryName,
    IReadOnlyList<TrackedSeat> Seats,
    GrabMode Mode,
    GrabSeatPollingStrategy PollingStrategy,
    TimeOnly? ScheduledStart);

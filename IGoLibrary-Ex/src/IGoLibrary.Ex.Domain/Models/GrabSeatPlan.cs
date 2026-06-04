using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record GrabSeatPlan(
    int LibraryId,
    string LibraryName,
    IReadOnlyList<SeatReference> Seats,
    GrabPollingMode PollingMode,
    GrabSeatPollingStrategy PollingStrategy,
    TimeOnly? ScheduledStart);

namespace IGoLibrary.Ex.Domain.Models;

public sealed record TomorrowReservationPlan(
    int LibraryId,
    string LibraryName,
    SeatReference Seat,
    TimeOnly ScheduledStart,
    bool ExecuteImmediately);

namespace IGoLibrary.Ex.Application.Abstractions;

public abstract record CoordinatorEvent;

public sealed record GrabSucceededCoordinatorEvent(
    string LibraryName,
    string SeatName) : CoordinatorEvent;

public sealed record OccupyReReserveSucceededCoordinatorEvent(
    string SeatName) : CoordinatorEvent;

public sealed record TomorrowReservationSucceededCoordinatorEvent(
    string LibraryName,
    string SeatName,
    string? Day) : CoordinatorEvent;

public sealed record VenueAvailableCoordinatorEvent(
    string LibraryName,
    int AvailableSeats) : CoordinatorEvent;

public sealed record CheckInReminderCoordinatorEvent(
    string LibraryName,
    string SeatName,
    DateTimeOffset Deadline) : CoordinatorEvent;

public sealed record CheckInMissedCoordinatorEvent(
    string LibraryName,
    string SeatName,
    DateTimeOffset Deadline,
    string ActionText) : CoordinatorEvent;

public sealed record CheckInAutoRescueSucceededCoordinatorEvent(
    string LibraryName,
    string SeatName) : CoordinatorEvent;

public sealed record SessionInvalidCoordinatorEvent(
    string Source,
    string Reason) : CoordinatorEvent;

public sealed record TaskFailedCoordinatorEvent(
    string TaskName,
    string Reason) : CoordinatorEvent;

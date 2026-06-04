namespace IGoLibrary.Ex.Application.Abstractions;

public abstract record CoordinatorEvent;

public sealed record GrabSucceededCoordinatorEvent(
    string LibraryName,
    string SeatName) : CoordinatorEvent;

public sealed record OccupyReReserveSucceededCoordinatorEvent(
    string SeatName) : CoordinatorEvent;

public sealed record SessionInvalidCoordinatorEvent(
    string Source,
    string Reason) : CoordinatorEvent;

public sealed record TaskFailedCoordinatorEvent(
    string TaskName,
    string Reason) : CoordinatorEvent;

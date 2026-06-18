namespace IGoLibrary.Ex.Application.Abstractions;

public interface ITaskEventAlertDispatcher
{
    Task NotifySessionInvalidAsync(string source, string reason, CancellationToken cancellationToken = default);

    Task NotifyGrabSucceededAsync(string libraryName, string seatName, CancellationToken cancellationToken = default);

    Task NotifyOccupyReReserveSucceededAsync(string seatName, CancellationToken cancellationToken = default);

    Task NotifyTomorrowReservationSucceededAsync(
        string libraryName,
        string seatName,
        string? day,
        CancellationToken cancellationToken = default);

    Task NotifyVenueAvailableAsync(string libraryName, int availableSeats, CancellationToken cancellationToken = default);

    Task NotifyCheckInReminderAsync(
        string libraryName,
        string seatName,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default);

    Task NotifyCheckInMissedAsync(
        string libraryName,
        string seatName,
        DateTimeOffset deadline,
        string actionText,
        CancellationToken cancellationToken = default);

    Task NotifyCheckInAutoRescueSucceededAsync(
        string libraryName,
        string seatName,
        CancellationToken cancellationToken = default);

    Task NotifyTaskFailedAsync(string taskName, string reason, CancellationToken cancellationToken = default);
}

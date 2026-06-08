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

    Task NotifyTaskFailedAsync(string taskName, string reason, CancellationToken cancellationToken = default);
}

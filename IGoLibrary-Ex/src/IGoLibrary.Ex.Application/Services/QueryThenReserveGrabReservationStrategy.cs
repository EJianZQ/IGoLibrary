using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class QueryThenReserveGrabReservationStrategy(
    ITraceIntApiClient apiClient,
    IActivityLogService activityLogService) : IGrabReservationAttemptStrategy
{
    public GrabReservationStrategy Strategy => GrabReservationStrategy.QueryThenReserve;

    public async Task<GrabReservationAttemptResult> TryReserveAsync(
        GrabReservationAttemptContext context,
        CancellationToken cancellationToken)
    {
        context.MarkRequestSent();
        var layout = await apiClient.GetLibraryLayoutAsync(
            context.Cookie,
            context.Plan.LibraryId,
            cancellationToken);
        var availableSeat = layout.Seats
            .Where(seat => context.Plan.Seats.Any(target => target.SeatKey == seat.SeatKey))
            .FirstOrDefault(seat => seat.IsAvailable);

        if (availableSeat is null)
        {
            return new GrabReservationAttemptResult(null, false, false, 0, layout);
        }

        activityLogService.Write(LogEntryKind.Success, "Grab", $"{availableSeat.SeatName} 空闲，正在尝试预约。");
        context.MarkRequestSent();
        var reserved = await apiClient.ReserveSeatAsync(
            context.Cookie,
            context.Plan.LibraryId,
            availableSeat.SeatKey,
            cancellationToken);

        return reserved
            ? new GrabReservationAttemptResult(
                new SeatReference(availableSeat.SeatKey, availableSeat.SeatName),
                true,
                false,
                0,
                layout)
            : new GrabReservationAttemptResult(null, true, false, 0, layout);
    }
}

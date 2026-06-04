using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class QueryThenReserveGrabReservationStrategy(
    ITraceIntApiClient apiClient,
    IActivityLogService activityLogService,
    AppRuntimeState runtimeState) : IGrabReservationAttemptStrategy
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
        runtimeState.CurrentLayout = layout;

        var availableSeat = layout.Seats
            .Where(seat => context.Plan.Seats.Any(target => target.SeatKey == seat.SeatKey))
            .FirstOrDefault(seat => seat.IsAvailable);

        if (availableSeat is null)
        {
            return new GrabReservationAttemptResult(null, false, false, 0);
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
                new TrackedSeat(availableSeat.SeatKey, availableSeat.SeatName),
                true,
                false,
                0)
            : new GrabReservationAttemptResult(null, true, false, 0);
    }
}

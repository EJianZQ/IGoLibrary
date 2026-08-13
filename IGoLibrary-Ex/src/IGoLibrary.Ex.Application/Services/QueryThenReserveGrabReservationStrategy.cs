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
        var hadReservationAttempt = false;
        foreach (var availableSeat in layout.Seats)
        {
            if (!availableSeat.IsAvailable || !context.TargetSeatKeys.Contains(availableSeat.SeatKey))
            {
                continue;
            }

            hadReservationAttempt = true;
            activityLogService.Write(LogEntryKind.Success, "Grab", $"{availableSeat.SeatName} 空闲，正在尝试预约。");

            bool reserved;
            try
            {
                context.MarkRequestSent();
                reserved = await apiClient.ReserveSeatAsync(
                    context.Cookie,
                    context.Plan.LibraryId,
                    availableSeat.SeatKey,
                    cancellationToken);
            }
            catch (Exception ex) when (DirectReservationMissClassifier.TryClassify(ex, out var missKind))
            {
                activityLogService.Write(
                    LogEntryKind.Info,
                    "Grab",
                    DirectReservationMissClassifier.GetMessage(
                        missKind,
                        new SeatReference(availableSeat.SeatKey, availableSeat.SeatName)));
                if (missKind == DirectReservationMissKind.RetryRequested)
                {
                    return new GrabReservationAttemptResult(null, true, true, 0, layout);
                }

                continue;
            }

            if (reserved)
            {
                return new GrabReservationAttemptResult(
                    new SeatReference(availableSeat.SeatKey, availableSeat.SeatName),
                    true,
                    false,
                    0,
                    layout);
            }
        }

        return new GrabReservationAttemptResult(null, hadReservationAttempt, false, 0, layout);
    }
}

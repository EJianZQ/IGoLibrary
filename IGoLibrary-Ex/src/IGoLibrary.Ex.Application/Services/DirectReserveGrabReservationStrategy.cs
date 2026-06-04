using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class DirectReserveGrabReservationStrategy(
    ITraceIntApiClient apiClient,
    IActivityLogService activityLogService,
    ICoordinatorRuntime runtime) : IGrabReservationAttemptStrategy
{
    private static readonly TimeSpan DirectReserveAttemptInterval = TimeSpan.FromMilliseconds(2000);

    public GrabReservationStrategy Strategy => GrabReservationStrategy.ReserveDirectly;

    public async Task<GrabReservationAttemptResult> TryReserveAsync(
        GrabReservationAttemptContext context,
        CancellationToken cancellationToken)
    {
        if (context.Plan.Seats.Count == 0)
        {
            return new GrabReservationAttemptResult(null, false, false, 0);
        }

        for (var offset = 0; offset < context.Plan.Seats.Count; offset++)
        {
            var index = (context.StartIndex + offset) % context.Plan.Seats.Count;
            var seat = context.Plan.Seats[index];
            bool reserved;
            try
            {
                context.MarkRequestSent();
                reserved = await apiClient.ReserveSeatAsync(
                    context.Cookie,
                    context.Plan.LibraryId,
                    seat.SeatKey,
                    cancellationToken);
            }
            catch (Exception ex) when (DirectReservationMissClassifier.TryClassify(ex, out var missKind))
            {
                activityLogService.Write(LogEntryKind.Info, "Grab", DirectReservationMissClassifier.GetMessage(missKind, seat));
                if (missKind == DirectReservationMissKind.RetryRequested)
                {
                    return new GrabReservationAttemptResult(
                        null,
                        true,
                        true,
                        (index + 1) % context.Plan.Seats.Count);
                }

                await DelayBeforeNextAttemptAsync(
                    offset,
                    context.Plan.Seats.Count,
                    DirectReserveAttemptInterval,
                    cancellationToken);
                continue;
            }

            if (reserved)
            {
                return new GrabReservationAttemptResult(
                    seat,
                    true,
                    false,
                    (index + 1) % context.Plan.Seats.Count);
            }

            await DelayBeforeNextAttemptAsync(
                offset,
                context.Plan.Seats.Count,
                DirectReserveAttemptInterval,
                cancellationToken);
        }

        return new GrabReservationAttemptResult(null, false, false, context.StartIndex);
    }

    private async Task DelayBeforeNextAttemptAsync(
        int currentOffset,
        int seatCount,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (currentOffset >= seatCount - 1 || delay <= TimeSpan.Zero)
        {
            return;
        }

        await runtime.DelayAsync(delay, cancellationToken);
    }
}

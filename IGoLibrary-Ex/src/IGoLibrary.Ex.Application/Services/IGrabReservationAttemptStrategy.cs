using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Application.Services;

internal interface IGrabReservationAttemptStrategy
{
    GrabReservationStrategy Strategy { get; }

    Task<GrabReservationAttemptResult> TryReserveAsync(
        GrabReservationAttemptContext context,
        CancellationToken cancellationToken);
}

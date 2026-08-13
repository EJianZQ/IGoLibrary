using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal interface IOccupyReReservationExecutor
{
    Task<OccupyReReservationResult> ExecuteAsync(
        string cookie,
        ReservationInfo reservation,
        OccupySeatPlan plan,
        int maxAttempts,
        Action<string> markRequestSent,
        CancellationToken cancellationToken);
}

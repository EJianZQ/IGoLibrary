using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal static class OccupySeatStateMachine
{
    internal static bool ShouldReReserve(ReservationInfo reservation, DateTimeOffset now)
    {
        return ReservationTimeHelper.ShouldReReserve(reservation.ExpirationTime, now);
    }

    internal static TimeSpan ResolveCheckDelay(OccupyCheckIntervalMode mode, int randomSeconds)
    {
        return mode == OccupyCheckIntervalMode.FixedTenSeconds
            ? TimeSpan.FromSeconds(10)
            : TimeSpan.FromSeconds(randomSeconds);
    }
}

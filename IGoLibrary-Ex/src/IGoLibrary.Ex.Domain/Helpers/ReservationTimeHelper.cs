namespace IGoLibrary.Ex.Domain.Helpers;

public static class ReservationTimeHelper
{
    public static readonly TimeSpan OccupyReReserveLeadTime = TimeSpan.FromSeconds(60);

    public static DateTimeOffset FromUnixSeconds(long timestamp)
    {
        return DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime();
    }

    public static bool ShouldReReserve(DateTimeOffset expirationTime, DateTimeOffset now)
    {
        return expirationTime - now <= OccupyReReserveLeadTime;
    }

    public static TimeSpan GetReReserveTriggerCountdown(DateTimeOffset expirationTime, DateTimeOffset now)
    {
        var remaining = expirationTime - OccupyReReserveLeadTime - now;
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }
}

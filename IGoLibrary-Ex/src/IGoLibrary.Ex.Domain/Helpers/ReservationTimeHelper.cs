namespace IGoLibrary.Ex.Domain.Helpers;

public static class ReservationTimeHelper
{
    public static DateTimeOffset FromUnixSeconds(long timestamp)
    {
        return DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime();
    }

    public static bool ShouldReReserve(DateTimeOffset expirationTime, DateTimeOffset now)
    {
        return (expirationTime - now).TotalSeconds <= 60;
    }
}

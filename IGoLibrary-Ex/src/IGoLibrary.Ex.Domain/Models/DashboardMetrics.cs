namespace IGoLibrary.Ex.Domain.Models;

public sealed record DashboardMetrics
{
    public int SuccessfulReservationCount { get; init; }

    public long TotalGuardSeconds { get; init; }

    public DashboardMetrics()
    {
    }

    public DashboardMetrics(int successfulReservationCount, long totalGuardSeconds)
    {
        SuccessfulReservationCount = successfulReservationCount;
        TotalGuardSeconds = totalGuardSeconds;
    }

    public static DashboardMetrics Default { get; } = new();
}

using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal static class GlobalLeakStateMachine
{
    private static readonly TimeSpan DefaultScanInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinimumScanInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumScanInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryRequestedBackoff = TimeSpan.FromSeconds(2);

    internal static TimeSpan NormalizeScanInterval(TimeSpan scanInterval)
    {
        if (scanInterval <= TimeSpan.Zero)
        {
            return DefaultScanInterval;
        }

        if (scanInterval < MinimumScanInterval)
        {
            return MinimumScanInterval;
        }

        return scanInterval > MaximumScanInterval
            ? MaximumScanInterval
            : scanInterval;
    }

    internal static IReadOnlyList<SeatSnapshot> GetAvailableSeats(LibraryLayout layout)
    {
        return layout.Seats
            .Where(static seat => seat.IsAvailable)
            .ToArray();
    }

    internal static TimeSpan GetRetryRequestedBackoff()
    {
        return RetryRequestedBackoff;
    }
}

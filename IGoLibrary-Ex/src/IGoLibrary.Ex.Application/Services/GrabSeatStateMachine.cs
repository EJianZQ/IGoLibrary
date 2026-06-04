using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal static class GrabSeatStateMachine
{
    private static readonly TimeSpan DirectReserveRateLimitCycleDelay = TimeSpan.FromSeconds(3);

    internal static DateTimeOffset ResolveNextScheduledStart(TimeOnly scheduledStart, DateTimeOffset now)
    {
        var todayScheduledStart = new DateTimeOffset(
            now.Date.Add(scheduledStart.ToTimeSpan()),
            now.Offset);

        return todayScheduledStart < now
            ? todayScheduledStart.AddDays(1)
            : todayScheduledStart;
    }

    internal static TimeSpan GetDelayAfterRateLimit(GrabSeatPollingStrategy pollingStrategy)
    {
        if (pollingStrategy.MaximumDelay <= pollingStrategy.MinimumDelay)
        {
            return pollingStrategy.MinimumDelay > DirectReserveRateLimitCycleDelay
                ? pollingStrategy.MinimumDelay
                : DirectReserveRateLimitCycleDelay;
        }

        return pollingStrategy.MaximumDelay > DirectReserveRateLimitCycleDelay
            ? pollingStrategy.MaximumDelay
            : DirectReserveRateLimitCycleDelay;
    }
}

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

    internal static TimeSpan ResolveScheduledWaitDelay(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (remaining <= TimeSpan.FromSeconds(5))
        {
            return remaining < TimeSpan.FromSeconds(1)
                ? remaining
                : TimeSpan.FromSeconds(1);
        }

        var remainingSeconds = Math.Max(1, (long)Math.Ceiling(remaining.TotalSeconds));
        var nextLoggedRemainingSeconds = remainingSeconds - remainingSeconds % 30;
        if (nextLoggedRemainingSeconds == remainingSeconds)
        {
            nextLoggedRemainingSeconds -= 30;
        }

        nextLoggedRemainingSeconds = Math.Max(5, nextLoggedRemainingSeconds);
        var delay = remaining - TimeSpan.FromSeconds(nextLoggedRemainingSeconds);
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1);
    }
}

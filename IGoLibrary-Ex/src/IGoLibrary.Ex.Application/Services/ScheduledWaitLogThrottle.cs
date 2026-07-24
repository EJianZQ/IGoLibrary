namespace IGoLibrary.Ex.Application.Services;

internal sealed class ScheduledWaitLogThrottle
{
    private long? _lastLoggedRemainingSeconds;

    public bool ShouldLog(TimeSpan remaining)
    {
        var remainingSeconds = Math.Max(0, (long)Math.Ceiling(remaining.TotalSeconds));
        if (_lastLoggedRemainingSeconds == remainingSeconds)
        {
            return false;
        }

        if (_lastLoggedRemainingSeconds is not null &&
            remainingSeconds > 5 &&
            remainingSeconds % 30 != 0)
        {
            return false;
        }

        _lastLoggedRemainingSeconds = remainingSeconds;
        return true;
    }
}

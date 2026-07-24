namespace IGoLibrary.Ex.Infrastructure.Logging;

public sealed class AppLogWriterHealthChangedEventArgs(
    bool isHealthy,
    int consecutiveFailureCount,
    DateTimeOffset occurredAt,
    string operation,
    string? errorMessage = null) : EventArgs
{
    public bool IsHealthy { get; } = isHealthy;

    public int ConsecutiveFailureCount { get; } = consecutiveFailureCount;

    public DateTimeOffset OccurredAt { get; } = occurredAt;

    public string Operation { get; } = operation;

    public string? ErrorMessage { get; } = errorMessage;
}

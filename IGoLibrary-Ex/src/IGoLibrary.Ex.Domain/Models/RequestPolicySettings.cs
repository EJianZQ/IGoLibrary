namespace IGoLibrary.Ex.Domain.Models;

public sealed record RequestPolicySettings
{
    public int TimeoutSeconds { get; init; } = 5;

    public int RetryCount { get; init; } = 3;

    public RequestPolicySettings()
    {
    }

    public RequestPolicySettings(int timeoutSeconds, int retryCount)
    {
        TimeoutSeconds = timeoutSeconds;
        RetryCount = retryCount;
    }

    public static RequestPolicySettings Default { get; } = new();
}

namespace IGoLibrary.Ex.Application.Configuration;

public sealed record NetworkRequestSettings
{
    public int TimeoutSeconds { get; init; } = 5;

    public int MaxRetries { get; init; } = 3;

    public NetworkRequestSettings()
    {
    }

    public NetworkRequestSettings(int timeoutSeconds, int maxRetries)
    {
        TimeoutSeconds = timeoutSeconds;
        MaxRetries = maxRetries;
    }

    public static NetworkRequestSettings Default { get; } = new();
}

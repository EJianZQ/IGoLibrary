namespace IGoLibrary.Ex.Application.Configuration;

public sealed record UpdateCheckSettings
{
    public bool CheckOnStartup { get; init; } = true;

    public DateTimeOffset? LastCheckedAtUtc { get; init; }

    public string? SkippedVersion { get; init; }

    public string? LastReleaseETag { get; init; }

    public UpdateCheckSettings()
    {
    }

    public UpdateCheckSettings(
        bool checkOnStartup,
        DateTimeOffset? lastCheckedAtUtc,
        string? skippedVersion,
        string? lastReleaseETag)
    {
        CheckOnStartup = checkOnStartup;
        LastCheckedAtUtc = lastCheckedAtUtc;
        SkippedVersion = skippedVersion;
        LastReleaseETag = lastReleaseETag;
    }

    public static UpdateCheckSettings Default { get; } = new();
}

namespace IGoLibrary.Ex.Application.Configuration;

public sealed record UpdateCheckSettings
{
    public bool CheckOnStartup { get; init; } = true;

    public DateTimeOffset? LastCheckedAtUtc { get; init; }

    public string? SkippedVersion { get; init; }

    public string? LastReleaseETag { get; init; }

    public string? LastReleaseETagVersion { get; init; }

    public UpdateCheckSettings()
    {
    }

    public UpdateCheckSettings(
        bool checkOnStartup,
        DateTimeOffset? lastCheckedAtUtc,
        string? skippedVersion,
        string? lastReleaseETag,
        string? lastReleaseETagVersion = null)
    {
        CheckOnStartup = checkOnStartup;
        LastCheckedAtUtc = lastCheckedAtUtc;
        SkippedVersion = skippedVersion;
        LastReleaseETag = lastReleaseETag;
        LastReleaseETagVersion = lastReleaseETagVersion;
    }

    public static UpdateCheckSettings Default { get; } = new();
}

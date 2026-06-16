using IGoLibrary.Ex.Application.Updates;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(
        UpdateCheckMode mode,
        CancellationToken cancellationToken = default);

    Task SkipVersionAsync(
        ReleaseVersion version,
        CancellationToken cancellationToken = default);
}

public enum UpdateCheckMode
{
    Automatic,
    Manual
}

public enum UpdateCheckStatus
{
    UpdateAvailable,
    NoUpdate,
    SkippedDisabled,
    SkippedCooldown,
    SkippedVersion,
    NotModified,
    Failed
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    ReleaseUpdateInfo? Release,
    string Message)
{
    public bool HasUpdate => Status == UpdateCheckStatus.UpdateAvailable && Release is not null;

    public static UpdateCheckResult UpdateAvailable(ReleaseUpdateInfo release)
    {
        return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, release, "发现新版本");
    }

    public static UpdateCheckResult NoUpdate(string message)
    {
        return new UpdateCheckResult(UpdateCheckStatus.NoUpdate, null, message);
    }

    public static UpdateCheckResult Skipped(UpdateCheckStatus status, string message)
    {
        return new UpdateCheckResult(status, null, message);
    }

    public static UpdateCheckResult Failed(string message)
    {
        return new UpdateCheckResult(UpdateCheckStatus.Failed, null, message);
    }
}

public sealed record ReleaseUpdateInfo(
    ReleaseVersion Version,
    string TagName,
    string Name,
    string Body,
    Uri HtmlUrl,
    DateTimeOffset? PublishedAt,
    bool IsPrerelease);

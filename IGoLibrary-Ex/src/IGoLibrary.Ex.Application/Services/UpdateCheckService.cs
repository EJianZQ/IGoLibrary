using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Updates;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Application.Services;

public sealed class UpdateCheckService(
    ISettingsService settingsService,
    IGitHubReleaseClient releaseClient,
    IAppVersionProvider appVersionProvider,
    TimeProvider timeProvider,
    ILogger<UpdateCheckService> logger) : IUpdateCheckService
{
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);

    public async Task<UpdateCheckResult> CheckAsync(
        UpdateCheckMode mode,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        var updateSettings = settings.Updates ?? UpdateCheckSettings.Default;
        var now = timeProvider.GetUtcNow();

        if (mode == UpdateCheckMode.Automatic)
        {
            if (!updateSettings.CheckOnStartup)
            {
                return UpdateCheckResult.Skipped(
                    UpdateCheckStatus.SkippedDisabled,
                    "启动时检查更新已关闭");
            }

            if (updateSettings.LastCheckedAtUtc is { } lastCheckedAt &&
                now - lastCheckedAt < AutomaticCheckInterval)
            {
                return UpdateCheckResult.Skipped(
                    UpdateCheckStatus.SkippedCooldown,
                    "24 小时内已经检查过更新");
            }
        }

        GitHubReleaseQueryResult queryResult;
        var requestTimeout = TimeSpan.FromSeconds(Math.Max(
            3,
            (settings.Network ?? NetworkRequestSettings.Default).TimeoutSeconds));
        await SaveCheckStateAsync(now, etag: null, cancellationToken);
        try
        {
            queryResult = await releaseClient.GetReleasesAsync(
                updateSettings.LastReleaseETag,
                requestTimeout,
                cancellationToken);
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "GitHub release check timed out.");
            return UpdateCheckResult.Failed("检查更新超时，请稍后重试。");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to check GitHub releases.");
            return UpdateCheckResult.Failed($"检查更新失败：{ex.Message}");
        }

        if (queryResult.NotModified)
        {
            await SaveCheckStateAsync(now, queryResult.ETag, cancellationToken);
            return UpdateCheckResult.Skipped(
                UpdateCheckStatus.NotModified,
                "GitHub Release 列表没有变化");
        }

        var currentVersion = appVersionProvider.CurrentVersion;
        var includePrerelease = currentVersion.IsPrerelease;
        var latestRelease = SelectLatestRelease(
            queryResult.Releases,
            currentVersion,
            includePrerelease);
        if (latestRelease is null)
        {
            await SaveCheckStateAsync(now, queryResult.ETag, cancellationToken);
            return UpdateCheckResult.NoUpdate("当前已是最新版本");
        }

        if (ReleaseVersion.TryParse(updateSettings.SkippedVersion, out var skippedVersion) &&
            latestRelease.Version <= skippedVersion)
        {
            await SaveCheckStateAsync(now, queryResult.ETag, cancellationToken);
            return UpdateCheckResult.Skipped(
                UpdateCheckStatus.SkippedVersion,
                $"已跳过版本 {latestRelease.Version}");
        }

        await SaveCheckStateAsync(now, etag: null, cancellationToken);
        return UpdateCheckResult.UpdateAvailable(latestRelease);
    }

    public async Task SkipVersionAsync(
        ReleaseVersion version,
        CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateAsync(current => current with
        {
            Updates = (current.Updates ?? UpdateCheckSettings.Default) with
            {
                SkippedVersion = version.ToString()
            }
        }, cancellationToken);
    }

    private static ReleaseUpdateInfo? SelectLatestRelease(
        IReadOnlyList<GitHubReleaseItem> releases,
        ReleaseVersion currentVersion,
        bool includePrerelease)
    {
        return releases
            .Where(static release => !release.Draft)
            .Select(TryCreateReleaseInfo)
            .OfType<ReleaseUpdateInfo>()
            .Where(release => includePrerelease || !release.IsPrerelease)
            .Where(release => release.Version > currentVersion)
            .OrderByDescending(static release => release.Version)
            .FirstOrDefault();
    }

    private static ReleaseUpdateInfo? TryCreateReleaseInfo(GitHubReleaseItem release)
    {
        if (!ReleaseVersion.TryParse(release.TagName, out var version))
        {
            return null;
        }

        if (!IsProductRelease(release))
        {
            return null;
        }

        return new ReleaseUpdateInfo(
            version,
            release.TagName,
            string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name.Trim(),
            release.Body ?? string.Empty,
            release.HtmlUrl,
            release.PublishedAt,
            release.Prerelease || version.IsPrerelease);
    }

    private static bool IsProductRelease(GitHubReleaseItem release)
    {
        if (ContainsProductMarker(release.Name) ||
            release.AssetNames.Any(ContainsProductMarker))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsProductMarker(string? value)
    {
        return value?.Contains("IGoLibrary-Ex", StringComparison.OrdinalIgnoreCase) == true ||
               value?.Contains("IGoLibrary.Ex", StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task SaveCheckStateAsync(
        DateTimeOffset checkedAtUtc,
        string? etag,
        CancellationToken cancellationToken)
    {
        await settingsService.UpdateAsync(current =>
        {
            var updates = current.Updates ?? UpdateCheckSettings.Default;
            return current with
            {
                Updates = updates with
                {
                    LastCheckedAtUtc = checkedAtUtc,
                    LastReleaseETag = string.IsNullOrWhiteSpace(etag)
                        ? updates.LastReleaseETag
                        : etag
                }
            };
        }, cancellationToken);
    }
}

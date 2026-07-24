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
        logger.LogInformation(
            "开始检查更新。检查模式={CheckMode}，当前版本={CurrentVersion}。",
            mode,
            appVersionProvider.CurrentVersionText);

        if (mode == UpdateCheckMode.Automatic)
        {
            if (!updateSettings.CheckOnStartup)
            {
                logger.LogInformation("自动更新检查已跳过：启动检查已关闭。");
                return UpdateCheckResult.Skipped(
                    UpdateCheckStatus.SkippedDisabled,
                    "启动时检查更新已关闭");
            }

            if (updateSettings.LastCheckedAtUtc is { } lastCheckedAt &&
                now - lastCheckedAt < AutomaticCheckInterval)
            {
                logger.LogInformation(
                    "自动更新检查已跳过：仍在冷却时间内。上次检查时间（UTC）={LastCheckedAtUtc}。",
                    lastCheckedAt);
                return UpdateCheckResult.Skipped(
                    UpdateCheckStatus.SkippedCooldown,
                    "24 小时内已经检查过更新");
            }
        }

        GitHubReleaseQueryResult queryResult;
        var currentVersion = appVersionProvider.CurrentVersion;
        var currentVersionText = currentVersion.ToString();
        var requestEtag = mode == UpdateCheckMode.Automatic &&
                          string.Equals(
                              updateSettings.LastReleaseETagVersion,
                              currentVersionText,
                              StringComparison.Ordinal)
            ? updateSettings.LastReleaseETag
            : null;
        var requestTimeout = TimeSpan.FromSeconds(Math.Max(
            3,
            (settings.Network ?? NetworkRequestSettings.Default).TimeoutSeconds));
        await SaveCheckStateAsync(
            now,
            etag: null,
            etagVersion: null,
            cancellationToken);
        try
        {
            queryResult = await releaseClient.GetReleasesAsync(
                requestEtag,
                requestTimeout,
                cancellationToken);
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "GitHub 发布版本检查超时。");
            return UpdateCheckResult.Failed("检查更新超时，请稍后重试");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "检查 GitHub 发布版本失败。");
            return UpdateCheckResult.Failed($"检查更新失败：{ex.Message}");
        }

        if (queryResult.NotModified)
        {
            logger.LogInformation("更新检查完成：GitHub Release 列表未变化。");
            await SaveCheckStateAsync(
                now,
                queryResult.ETag,
                currentVersionText,
                cancellationToken);
            return UpdateCheckResult.Skipped(
                UpdateCheckStatus.NotModified,
                "GitHub Release 列表没有变化");
        }

        var latestRelease = SelectLatestRelease(
            queryResult.Releases,
            currentVersion);
        logger.LogInformation(
            "更新检查已取得发布列表。发布数量={ReleaseCount}，找到候选版本={CandidateFound}。",
            queryResult.Releases.Count,
            latestRelease is not null);
        if (latestRelease is null)
        {
            logger.LogInformation("更新检查完成：当前已是最新版本。");
            await SaveCheckStateAsync(
                now,
                queryResult.ETag,
                currentVersionText,
                cancellationToken);
            return UpdateCheckResult.NoUpdate("当前已是最新版本");
        }

        if (ReleaseVersion.TryParse(updateSettings.SkippedVersion, out var skippedVersion) &&
            latestRelease.Version <= skippedVersion)
        {
            logger.LogInformation(
                "更新候选版本已按用户设置跳过。候选版本={CandidateVersion}，已跳过版本={SkippedVersion}。",
                latestRelease.Version,
                skippedVersion);
            await SaveCheckStateAsync(
                now,
                queryResult.ETag,
                currentVersionText,
                cancellationToken);
            return UpdateCheckResult.Skipped(
                UpdateCheckStatus.SkippedVersion,
                $"已跳过版本 {latestRelease.Version}");
        }

        await SaveCheckStateAsync(
            now,
            etag: null,
            etagVersion: null,
            cancellationToken);
        logger.LogInformation(
            "发现可用更新。当前版本={CurrentVersion}，目标版本={TargetVersion}，包含已验证的 Windows 更新包={HasVerifiedWindowsPackage}。",
            currentVersion,
            latestRelease.Version,
            latestRelease.WindowsX64Package is not null);
        return UpdateCheckResult.UpdateAvailable(latestRelease);
    }

    public async Task SkipVersionAsync(
        ReleaseVersion version,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("用户选择跳过更新版本。版本={Version}。", version);
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
        ReleaseVersion currentVersion)
    {
        return releases
            .Where(static release => !release.Draft && !release.Prerelease)
            .Select(TryCreateReleaseInfo)
            .OfType<ReleaseUpdateInfo>()
            .Where(release => release.Version > currentVersion)
            .OrderByDescending(static release => release.Version)
            .FirstOrDefault();
    }

    private static ReleaseUpdateInfo? TryCreateReleaseInfo(GitHubReleaseItem release)
    {
        if (release.Draft || release.Prerelease ||
            !ReleaseVersion.TryParse(release.TagName, out var version))
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
            SelectWindowsX64Package(release, version));
    }

    private static bool IsProductRelease(GitHubReleaseItem release)
    {
        if (ContainsProductMarker(release.Name) ||
            release.Assets.Any(static asset => ContainsProductMarker(asset.Name)))
        {
            return true;
        }

        return false;
    }

    private static ReleaseAssetInfo? SelectWindowsX64Package(
        GitHubReleaseItem release,
        ReleaseVersion version)
    {
        var expectedDefaultPackageName = $"IGoLibrary-Ex-v{version}-windows-x64.zip";
        var matches = release.Assets
            .Where(asset => string.Equals(
                asset.Name,
                expectedDefaultPackageName,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            return null;
        }

        var asset = matches[0];
        if (!string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase) ||
            asset.Size <= 0 ||
            asset.BrowserDownloadUrl is not { Scheme: "https" } downloadUrl ||
            !string.Equals(downloadUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !downloadUrl.AbsolutePath.StartsWith(
                "/EJianZQ/IGoLibrary/releases/download/",
                StringComparison.OrdinalIgnoreCase) ||
            !TryNormalizeSha256Digest(asset.Digest, out var digest))
        {
            return null;
        }

        return new ReleaseAssetInfo(
            asset.Name,
            downloadUrl,
            asset.Size,
            digest,
            string.IsNullOrWhiteSpace(asset.ContentType)
                ? "application/zip"
                : asset.ContentType.Trim());
    }

    private static bool TryNormalizeSha256Digest(string? value, out string digest)
    {
        digest = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hash = value[7..].Trim();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            return false;
        }

        digest = "sha256:" + hash.ToLowerInvariant();
        return true;
    }

    private static bool ContainsProductMarker(string? value)
    {
        return value?.Contains("IGoLibrary-Ex", StringComparison.OrdinalIgnoreCase) == true ||
               value?.Contains("IGoLibrary.Ex", StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task SaveCheckStateAsync(
        DateTimeOffset checkedAtUtc,
        string? etag,
        string? etagVersion,
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
                        : etag,
                    LastReleaseETagVersion = string.IsNullOrWhiteSpace(etag)
                        ? updates.LastReleaseETagVersion
                        : etagVersion
                }
            };
        }, cancellationToken);
    }
}

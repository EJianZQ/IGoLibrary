using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class UpdateCheckServiceTests
{
    [Fact]
    public async Task CheckAsync_IncludesPrerelease_WhenCurrentVersionIsPrerelease()
    {
        var releaseClient = new FakeGitHubReleaseClient(
            Release("v0.4.0-beta.1", prerelease: true),
            Release("Public1.3"));
        var service = CreateService(
            currentVersion: Parse("0.3.0-beta"),
            releaseClient: releaseClient);

        var result = await service.CheckAsync(UpdateCheckMode.Automatic);

        Assert.True(result.HasUpdate);
        Assert.Equal("0.4.0-beta.1", result.Release?.Version.ToString());
    }

    [Fact]
    public async Task CheckAsync_IgnoresPrerelease_WhenCurrentVersionIsStable()
    {
        var releaseClient = new FakeGitHubReleaseClient(
            Release("v1.1.0-beta.1", prerelease: true),
            Release("v1.0.1"));
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: releaseClient);

        var result = await service.CheckAsync(UpdateCheckMode.Automatic);

        Assert.True(result.HasUpdate);
        Assert.Equal("1.0.1", result.Release?.Version.ToString());
    }

    [Fact]
    public async Task CheckAsync_SkipsAutomaticCheck_WhenLastCheckIsWithinTwentyFourHours()
    {
        var now = new DateTimeOffset(2026, 6, 9, 8, 0, 0, TimeSpan.Zero);
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Updates = UpdateCheckSettings.Default with
            {
                LastCheckedAtUtc = now.AddHours(-1)
            }
        });
        var releaseClient = new FakeGitHubReleaseClient(Release("v1.0.1"));
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: releaseClient,
            settingsService: settingsService,
            now: now);

        var result = await service.CheckAsync(UpdateCheckMode.Automatic);

        Assert.Equal(UpdateCheckStatus.SkippedCooldown, result.Status);
        Assert.Equal(0, releaseClient.CallCount);
    }

    [Fact]
    public async Task CheckAsync_ManualCheckBypassesCooldown_AndRefreshesLastCheckedAt()
    {
        var now = new DateTimeOffset(2026, 6, 9, 8, 0, 0, TimeSpan.Zero);
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Updates = UpdateCheckSettings.Default with
            {
                LastCheckedAtUtc = now.AddHours(-1)
            }
        });
        var releaseClient = new FakeGitHubReleaseClient(Release("v1.0.1"));
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: releaseClient,
            settingsService: settingsService,
            now: now);

        var result = await service.CheckAsync(UpdateCheckMode.Manual);

        Assert.True(result.HasUpdate);
        Assert.Equal(1, releaseClient.CallCount);
        Assert.Equal(now, settingsService.CurrentSettings.Updates.LastCheckedAtUtc);
        Assert.Null(settingsService.CurrentSettings.Updates.LastReleaseETag);
    }

    [Fact]
    public async Task CheckAsync_RefreshesAttemptTime_WhenAutomaticCheckFails()
    {
        var now = new DateTimeOffset(2026, 6, 9, 8, 0, 0, TimeSpan.Zero);
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var releaseClient = new FakeGitHubReleaseClient()
        {
            ExceptionToThrow = new HttpRequestException("offline")
        };
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: releaseClient,
            settingsService: settingsService,
            now: now);

        var failed = await service.CheckAsync(UpdateCheckMode.Automatic);
        var skipped = await service.CheckAsync(UpdateCheckMode.Automatic);

        Assert.Equal(UpdateCheckStatus.Failed, failed.Status);
        Assert.Equal(now, settingsService.CurrentSettings.Updates.LastCheckedAtUtc);
        Assert.Equal(UpdateCheckStatus.SkippedCooldown, skipped.Status);
        Assert.Equal(1, releaseClient.CallCount);
    }

    [Fact]
    public async Task CheckAsync_ReturnsTimeoutFailure_WhenReleaseClientTimesOut()
    {
        var now = new DateTimeOffset(2026, 6, 9, 8, 0, 0, TimeSpan.Zero);
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var releaseClient = new FakeGitHubReleaseClient()
        {
            ExceptionToThrow = new TimeoutException("timeout")
        };
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: releaseClient,
            settingsService: settingsService,
            now: now);

        var result = await service.CheckAsync(UpdateCheckMode.Manual);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains("超时", result.Message);
        Assert.Equal(now, settingsService.CurrentSettings.Updates.LastCheckedAtUtc);
    }

    [Fact]
    public async Task CheckAsync_CachesEtag_WhenNoUpdateIsAvailable()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: new FakeGitHubReleaseClient(Release("v0.9.9")),
            settingsService: settingsService);

        var result = await service.CheckAsync(UpdateCheckMode.Manual);

        Assert.Equal(UpdateCheckStatus.NoUpdate, result.Status);
        Assert.Equal("\"etag\"", settingsService.CurrentSettings.Updates.LastReleaseETag);
    }

    [Fact]
    public async Task CheckAsync_PreservesExistingEtag_WhenUpdateIsAvailable()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Updates = UpdateCheckSettings.Default with
            {
                LastReleaseETag = "\"old\""
            }
        });
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: new FakeGitHubReleaseClient(Release("v1.0.1")),
            settingsService: settingsService);

        var result = await service.CheckAsync(UpdateCheckMode.Manual);

        Assert.True(result.HasUpdate);
        Assert.Equal("\"old\"", settingsService.CurrentSettings.Updates.LastReleaseETag);
    }

    [Fact]
    public async Task CheckAsync_SuppressesSkippedVersion_ButPromptsForNewerVersion()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Updates = UpdateCheckSettings.Default with
            {
                SkippedVersion = "1.0.2"
            }
        });
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: new FakeGitHubReleaseClient(Release("v1.0.2")),
            settingsService: settingsService);

        var skipped = await service.CheckAsync(UpdateCheckMode.Manual);
        Assert.Equal(UpdateCheckStatus.SkippedVersion, skipped.Status);

        service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: new FakeGitHubReleaseClient(Release("v1.0.3")),
            settingsService: settingsService);
        var newer = await service.CheckAsync(UpdateCheckMode.Manual);

        Assert.True(newer.HasUpdate);
        Assert.Equal("1.0.3", newer.Release?.Version.ToString());
    }

    [Fact]
    public async Task SkipVersionAsync_PersistsNormalizedVersion()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: new FakeGitHubReleaseClient(),
            settingsService: settingsService);

        await service.SkipVersionAsync(Parse("v1.0.2"));

        Assert.Equal("1.0.2", settingsService.CurrentSettings.Updates.SkippedVersion);
    }

    [Fact]
    public async Task CheckAsync_IgnoresSemverRelease_WhenProductMarkerIsMissing()
    {
        var releaseClient = new FakeGitHubReleaseClient(new GitHubReleaseItem(
            "v2.0.0",
            "Another App v2.0.0",
            "Release notes",
            new Uri("https://github.com/EJianZQ/IGoLibrary/releases/tag/v2.0.0"),
            new DateTimeOffset(2026, 6, 9, 8, 0, 0, TimeSpan.Zero),
            Draft: false,
            Prerelease: false,
            AssetNames: []));
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: releaseClient);

        var result = await service.CheckAsync(UpdateCheckMode.Manual);

        Assert.Equal(UpdateCheckStatus.NoUpdate, result.Status);
    }

    private static UpdateCheckService CreateService(
        ReleaseVersion currentVersion,
        FakeGitHubReleaseClient releaseClient,
        FakeSettingsService? settingsService = null,
        DateTimeOffset? now = null)
    {
        return new UpdateCheckService(
            settingsService ?? new FakeSettingsService(AppSettings.Default),
            releaseClient,
            new FakeAppVersionProvider(currentVersion),
            new FixedTimeProvider(now ?? new DateTimeOffset(2026, 6, 9, 8, 0, 0, TimeSpan.Zero)),
            NullLogger<UpdateCheckService>.Instance);
    }

    private static GitHubReleaseItem Release(
        string tagName,
        bool prerelease = false,
        bool draft = false)
    {
        return new GitHubReleaseItem(
            tagName,
            $"IGoLibrary-Ex {tagName}",
            $"Release notes for {tagName}",
            new Uri($"https://github.com/EJianZQ/IGoLibrary/releases/tag/{tagName}"),
            new DateTimeOffset(2026, 6, 9, 8, 0, 0, TimeSpan.Zero),
            draft,
            prerelease,
            [$"IGoLibrary-Ex-{tagName}.zip"]);
    }

    private static ReleaseVersion Parse(string value)
    {
        Assert.True(ReleaseVersion.TryParse(value, out var version));
        return version;
    }

    private sealed class FakeGitHubReleaseClient(params GitHubReleaseItem[] releases) : IGitHubReleaseClient
    {
        public int CallCount { get; private set; }

        public Exception? ExceptionToThrow { get; init; }

        public Task<GitHubReleaseQueryResult> GetReleasesAsync(
            string? etag,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(new GitHubReleaseQueryResult(false, "\"etag\"", releases));
        }
    }

    private sealed class FakeAppVersionProvider(ReleaseVersion currentVersion) : IAppVersionProvider
    {
        public ReleaseVersion CurrentVersion { get; } = currentVersion;

        public string CurrentVersionText => CurrentVersion.ToString();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

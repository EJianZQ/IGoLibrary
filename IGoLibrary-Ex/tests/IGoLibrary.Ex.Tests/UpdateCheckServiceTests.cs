using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class UpdateCheckServiceTests
{
    [Fact]
    public async Task CheckAsync_IgnoresPrereleaseOnlyReleaseList()
    {
        var releaseClient = new FakeGitHubReleaseClient(
            Release("v1.1.0", prerelease: true),
            Release("Public1.3"));
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: releaseClient);

        var result = await service.CheckAsync(UpdateCheckMode.Automatic);

        Assert.Equal(UpdateCheckStatus.NoUpdate, result.Status);
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
    public async Task CheckAsync_ManualCheckBypassesCachedEtag_AndFindsUpdate()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Updates = UpdateCheckSettings.Default with
            {
                LastReleaseETag = "\"old\"",
                LastReleaseETagVersion = "1.0.0"
            }
        });
        var releaseClient = new FakeGitHubReleaseClient(Release("v1.0.1"));
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: releaseClient,
            settingsService: settingsService);

        var result = await service.CheckAsync(UpdateCheckMode.Manual);

        Assert.True(result.HasUpdate);
        Assert.Null(Assert.Single(releaseClient.RequestedEtags));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1.0.1")]
    public async Task CheckAsync_AutomaticCheckIgnoresEtagNotBoundToCurrentVersion(
        string? etagVersion)
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Updates = UpdateCheckSettings.Default with
            {
                LastReleaseETag = "\"old\"",
                LastReleaseETagVersion = etagVersion
            }
        });
        var releaseClient = new FakeGitHubReleaseClient(Release("v1.0.1"));
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: releaseClient,
            settingsService: settingsService);

        var result = await service.CheckAsync(UpdateCheckMode.Automatic);

        Assert.True(result.HasUpdate);
        Assert.Null(Assert.Single(releaseClient.RequestedEtags));
    }

    [Fact]
    public async Task CheckAsync_AutomaticCheckUsesEtagBoundToCurrentVersion()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Updates = UpdateCheckSettings.Default with
            {
                LastReleaseETag = "\"old\"",
                LastReleaseETagVersion = "1.0.0"
            }
        });
        var releaseClient = new FakeGitHubReleaseClient
        {
            ResultOverride = new GitHubReleaseQueryResult(
                NotModified: true,
                ETag: "\"old\"",
                Releases: [])
        };
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: releaseClient,
            settingsService: settingsService);

        var result = await service.CheckAsync(UpdateCheckMode.Automatic);

        Assert.Equal(UpdateCheckStatus.NotModified, result.Status);
        Assert.Equal("\"old\"", Assert.Single(releaseClient.RequestedEtags));
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
        Assert.Equal(
            "1.0.0",
            settingsService.CurrentSettings.Updates.LastReleaseETagVersion);
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
            Assets: []));
        var service = CreateService(
            currentVersion: Parse("1.0.0"),
            releaseClient: releaseClient);

        var result = await service.CheckAsync(UpdateCheckMode.Manual);

        Assert.Equal(UpdateCheckStatus.NoUpdate, result.Status);
    }

    [Fact]
    public async Task CheckAsync_SelectsExactUploadedWindowsX64Asset()
    {
        var release = Release("v1.0.1") with
        {
            Assets = [WindowsAsset("1.0.1")]
        };
        var service = CreateService(
            Parse("1.0.0"),
            new FakeGitHubReleaseClient(release));

        var result = await service.CheckAsync(UpdateCheckMode.Manual);

        var package = Assert.IsType<ReleaseAssetInfo>(result.Release?.WindowsX64Package);
        Assert.Equal("IGoLibrary-Ex-v1.0.1-windows-x64.zip", package.Name);
        Assert.Equal(123456, package.Size);
        Assert.Equal(
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            package.Digest);
    }

    [Fact]
    public async Task CheckAsync_IgnoresBundledCloudflaredAssetAndSelectsLightweightAsset()
    {
        var lightweight = WindowsAsset("1.0.1");
        var bundled = lightweight with
        {
            Name = "IGoLibrary-Ex-v1.0.1-windows-x64-with-cloudflared.zip",
            BrowserDownloadUrl = new Uri(
                "https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/" +
                "IGoLibrary-Ex-v1.0.1-windows-x64-with-cloudflared.zip")
        };
        var release = Release("v1.0.1") with { Assets = [bundled, lightweight] };
        var service = CreateService(Parse("1.0.0"), new FakeGitHubReleaseClient(release));

        var result = await service.CheckAsync(UpdateCheckMode.Manual);

        var package = Assert.IsType<ReleaseAssetInfo>(result.Release?.WindowsX64Package);
        Assert.Equal("IGoLibrary-Ex-v1.0.1-windows-x64.zip", package.Name);
    }

    [Theory]
    [InlineData("IGoLibrary-Ex-v1.0.1-windows-arm64.zip", "uploaded", "https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/file.zip", 123456, "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("IGoLibrary-Ex-v1.0.1-windows-x64.zip", "new", "https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/file.zip", 123456, "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("IGoLibrary-Ex-v1.0.1-windows-x64.zip", "uploaded", "http://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/file.zip", 123456, "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("IGoLibrary-Ex-v1.0.1-windows-x64.zip", "uploaded", "https://example.com/file.zip", 123456, "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("IGoLibrary-Ex-v1.0.1-windows-x64.zip", "uploaded", "https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/file.zip", 0, "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("IGoLibrary-Ex-v1.0.1-windows-x64.zip", "uploaded", "https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/file.zip", 123456, "sha256:bad")]
    public async Task CheckAsync_RejectsInvalidWindowsAsset(
        string name,
        string state,
        string url,
        long size,
        string digest)
    {
        var release = Release("v1.0.1") with
        {
            Assets =
            [
                new GitHubReleaseAssetItem(
                    name,
                    new Uri(url),
                    size,
                    digest,
                    state,
                    "application/zip")
            ]
        };
        var service = CreateService(Parse("1.0.0"), new FakeGitHubReleaseClient(release));

        var result = await service.CheckAsync(UpdateCheckMode.Manual);

        Assert.True(result.HasUpdate);
        Assert.Null(result.Release?.WindowsX64Package);
    }

    [Fact]
    public async Task CheckAsync_RejectsDuplicateMatchingWindowsAssets()
    {
        var asset = WindowsAsset("1.0.1");
        var release = Release("v1.0.1") with { Assets = [asset, asset] };
        var service = CreateService(Parse("1.0.0"), new FakeGitHubReleaseClient(release));

        var result = await service.CheckAsync(UpdateCheckMode.Manual);

        Assert.Null(result.Release?.WindowsX64Package);
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
            [new GitHubReleaseAssetItem(
                $"IGoLibrary-Ex-{tagName}.zip",
                null,
                0,
                null,
                null,
                null)]);
    }

    private static ReleaseVersion Parse(string value)
    {
        Assert.True(ReleaseVersion.TryParse(value, out var version));
        return version;
    }

    private static GitHubReleaseAssetItem WindowsAsset(string version)
    {
        return new GitHubReleaseAssetItem(
            $"IGoLibrary-Ex-v{version}-windows-x64.zip",
            new Uri($"https://github.com/EJianZQ/IGoLibrary/releases/download/v{version}/IGoLibrary-Ex-v{version}-windows-x64.zip"),
            123456,
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "uploaded",
            "application/zip");
    }

    private sealed class FakeGitHubReleaseClient(params GitHubReleaseItem[] releases) : IGitHubReleaseClient
    {
        public int CallCount { get; private set; }

        public List<string?> RequestedEtags { get; } = [];

        public Exception? ExceptionToThrow { get; init; }

        public GitHubReleaseQueryResult? ResultOverride { get; init; }

        public Task<GitHubReleaseQueryResult> GetReleasesAsync(
            string? etag,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            RequestedEtags.Add(etag);
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(ResultOverride ??
                new GitHubReleaseQueryResult(false, "\"etag\"", releases));
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

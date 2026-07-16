using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class CloudflareTunnelTests
{
    [Fact]
    public void BuildStartInfo_PreservesArgumentsAndIsolatesCloudflaredConfiguration()
    {
        var home = Path.Combine(Path.GetTempPath(), "cloudflared test home");
        var info = CloudflareQuickTunnelRunner.BuildStartInfo(
            OperatingSystem.IsWindows() ? @"C:\Program Files\cloudflared.exe" : "/Applications/cloudflared",
            new Uri("http://192.168.1.10:49153/?token=secret"),
            23001,
            home,
            new Uri("http://127.0.0.1:7897"));

        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        Assert.Equal(
            ["tunnel", "--no-autoupdate", "--protocol", "http2", "--metrics", "127.0.0.1:23001", "--url", "http://192.168.1.10:49153"],
            info.ArgumentList);
        Assert.Equal(home, info.Environment["HOME"]);
        Assert.Equal(home, info.Environment["USERPROFILE"]);
        Assert.Equal(home, info.Environment["XDG_CONFIG_HOME"]);
        Assert.Equal("http://127.0.0.1:7897", info.Environment["HTTP_PROXY"]);
        Assert.Equal("http://127.0.0.1:7897", info.Environment["HTTPS_PROXY"]);
        Assert.Contains("192.168.1.10", info.Environment["NO_PROXY"], StringComparison.Ordinal);
        Assert.DoesNotContain("secret", string.Join(' ', info.ArgumentList), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildStartInfo_DirectModeClearsInheritedProxyEnvironment()
    {
        var info = CloudflareQuickTunnelRunner.BuildStartInfo(
            OperatingSystem.IsWindows() ? @"C:\cloudflared.exe" : "/cloudflared",
            new Uri("http://192.168.1.20:49153/"),
            23002,
            Path.GetTempPath(),
            proxyUri: null);

        Assert.DoesNotContain("HTTP_PROXY", info.Environment.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("HTTPS_PROXY", info.Environment.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALL_PROXY", info.Environment.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("192.168.1.20", info.Environment["NO_PROXY"], StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureExecutableAvailable_UsesDedicatedMissingCloudflaredError()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            "IGoLibrary-Ex-missing-cloudflared-tests",
            Guid.NewGuid().ToString("N"),
            OperatingSystem.IsWindows() ? "cloudflared.exe" : "cloudflared");

        var exception = Assert.Throws<CloudflaredUnavailableException>(() =>
            CloudflareQuickTunnelRunner.EnsureExecutableAvailable(missingPath));

        Assert.Equal(CloudflaredUnavailableException.UserMessage, exception.Message);
        Assert.Equal(missingPath, exception.FileName);
        Assert.Contains("同版本、同架构", exception.Message, StringComparison.Ordinal);
        Assert.Contains("不带 -without-cloudflared", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("-with-cloudflared", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProxyResolver_AutoUsesSystemProxyAndManualModeIsNormalized()
    {
        var resolver = new CloudflareTunnelProxyResolver(
            new FakeSystemProxyProvider(new FakeWebProxy(new Uri("http://127.0.0.1:7897"))));

        var automatic = resolver.Resolve(new CloudflareTunnelProxyOptions(
            CloudflareTunnelProxyMode.Auto,
            string.Empty));
        var manual = resolver.Resolve(new CloudflareTunnelProxyOptions(
            CloudflareTunnelProxyMode.ManualHttpProxy,
            "http://127.0.0.1:7899/"));

        Assert.Equal(new Uri("http://127.0.0.1:7897"), automatic.ProxyUri);
        Assert.Equal(CloudflareTunnelProxyMode.SystemProxy, automatic.EffectiveMode);
        Assert.Equal(new Uri("http://127.0.0.1:7899"), manual.ProxyUri);
        Assert.Equal(CloudflareTunnelProxyMode.ManualHttpProxy, manual.EffectiveMode);
    }

    [Fact]
    public void ProxyResolver_SystemModeRequiresConfiguredHttpProxy()
    {
        var resolver = new CloudflareTunnelProxyResolver(
            new FakeSystemProxyProvider(new FakeWebProxy(proxyUri: null)));

        var exception = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(
            new CloudflareTunnelProxyOptions(CloudflareTunnelProxyMode.SystemProxy, string.Empty)));

        Assert.Contains("未检测到", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitProxyWithDirectCompatibility_IsRejectedWithUserMessage()
    {
        var logger = new CapturingQuickTunnelLogger();

        var exception = Assert.Throws<CloudflareTunnelProxyConflictException>(() =>
            CloudflareQuickTunnelRunner.ValidateResolvedProxyCompatibility(
                new CloudflareTunnelProxyResolution(
                    new Uri("http://127.0.0.1:7897"),
                    CloudflareTunnelProxyMode.SystemProxy),
                new ClashMihomoCompatibilityOptions(true, string.Empty, "DIRECT"),
                logger));

        Assert.Equal(
            "检测到系统已开启代理，但Clash/Mihomo 路由策略为 DIRECT，明显冲突" +
            "请填写正确的路由策略或把代理方式切换为不使用显式代理",
            exception.Message);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(CloudflareTunnelProxyConflictException.UserMessage, entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "DIRECT")]
    [InlineData(true, "Cloudflare Proxy")]
    public void CompatibleProxySettings_AreNotRejected(bool enabled, string routePolicy)
    {
        var logger = new CapturingQuickTunnelLogger();

        CloudflareQuickTunnelRunner.ValidateResolvedProxyCompatibility(
            new CloudflareTunnelProxyResolution(
                new Uri("http://127.0.0.1:7897"),
                CloudflareTunnelProxyMode.SystemProxy),
            new ClashMihomoCompatibilityOptions(enabled, string.Empty, routePolicy),
            logger);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void DirectConnectionWithDirectCompatibility_IsNotRejected()
    {
        var logger = new CapturingQuickTunnelLogger();

        CloudflareQuickTunnelRunner.ValidateResolvedProxyCompatibility(
            new CloudflareTunnelProxyResolution(
                ProxyUri: null,
                EffectiveMode: CloudflareTunnelProxyMode.Direct),
            new ClashMihomoCompatibilityOptions(true, string.Empty, "DIRECT"),
            logger);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task HealthProbe_PreservesConcreteConnectionError()
    {
        var socketError = new SocketException((int)SocketError.TimedOut);
        var connectionError = new HttpRequestException("proxy CONNECT timed out", socketError);
        using var probe = new CloudflareTunnelHealthProbeSession(
            new StubHttpMessageHandler((_, _) => throw connectionError));

        var result = await probe.ProbeAsync(
            new Uri("https://unit-test.trycloudflare.com/_health"),
            TimeSpan.FromSeconds(1));

        Assert.False(result.IsHealthy);
        Assert.Same(connectionError, result.Failure);
        Assert.Equal("proxy CONNECT timed out", CloudflareQuickTunnelRunner.DescribeHealthCheckFailure(result.Failure));
    }

    [Fact]
    public async Task HealthProbe_RecordsPerRequestTimeout()
    {
        using var probe = new CloudflareTunnelHealthProbeSession(
            new StubHttpMessageHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }));

        var result = await probe.ProbeAsync(
            new Uri("https://unit-test.trycloudflare.com/_health"),
            TimeSpan.FromMilliseconds(10));

        Assert.False(result.IsHealthy);
        var timeout = Assert.IsType<TimeoutException>(result.Failure);
        Assert.Contains("0.01", timeout.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<OperationCanceledException>(timeout.InnerException);
    }

    [Theory]
    [InlineData("INF Your quick Tunnel has been created! Visit it at https://quiet-unit-test.trycloudflare.com", true)]
    [InlineData("INF Requesting new quick Tunnel on https://api.trycloudflare.com...", false)]
    [InlineData("https://trycloudflare.com", false)]
    [InlineData("https://eviltrycloudflare.com", false)]
    [InlineData("http://quiet-unit-test.trycloudflare.com", false)]
    [InlineData("https://quiet-unit-test.trycloudflare.com.evil.test", false)]
    public void TryExtractPublicBaseUri_OnlyAcceptsValidatedTryCloudflareHttpsHost(
        string line,
        bool expected)
    {
        var result = CloudflareQuickTunnelRunner.TryExtractPublicBaseUri(line, out var uri);

        Assert.Equal(expected, result);
        if (expected)
        {
            Assert.Equal("https://quiet-unit-test.trycloudflare.com/", uri.ToString());
        }
    }

    [Fact]
    public void SanitizeDiagnosticLine_RedactsSensitiveValuesAndBoundsLength()
    {
        var line = "ERR request failed token=secret&code=oauth-code authorization=bearer " + new string('x', 600);

        var sanitized = CloudflareQuickTunnelRunner.SanitizeDiagnosticLine(line);

        Assert.DoesNotContain("secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("oauth-code", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer", sanitized, StringComparison.Ordinal);
        Assert.Contains("token=[redacted]", sanitized, StringComparison.Ordinal);
        Assert.Contains("code=[redacted]", sanitized, StringComparison.Ordinal);
        Assert.Contains("authorization=[redacted]", sanitized, StringComparison.Ordinal);
        Assert.True(sanitized.Length <= 501);
    }

    [Theory]
    [InlineData("failed to request quick Tunnel: Post https://api.trycloudflare.com/tunnel: EOF", true)]
    [InlineData("failed to request quick Tunnel: Post https://api.trycloudflare.com/tunnel: TLS handshake failed", true)]
    [InlineData("origin returned HTTP 503", false)]
    public void IsLikelyProxyOrTunFailure_ClassifiesControlPlaneTlsFailures(string diagnostic, bool expected)
    {
        Assert.Equal(expected, CloudflareQuickTunnelRunner.IsLikelyProxyOrTunFailure(diagnostic));
    }

    [Fact]
    public void HealthState_RequiresThreeConsecutiveFailuresAndSuccessResetsCounter()
    {
        var state = new CloudflareTunnelHealthState(3);

        Assert.False(state.RecordProbe(healthy: false));
        Assert.False(state.RecordProbe(healthy: false));
        Assert.False(state.RecordProbe(healthy: true));
        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.False(state.RecordProbe(healthy: false));
        Assert.False(state.RecordProbe(healthy: false));
        Assert.True(state.RecordProbe(healthy: false));
        Assert.Equal(3, state.ConsecutiveFailures);
    }

    [Fact]
    public async Task ExposureManager_LiveSwitchPreservesLanPathQueryAndDoesNotRestartOrigin()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.LocalNetwork);
        var runner = new FakeCloudflareQuickTunnelRunner();
        await using var manager = CreateManager(runner, settingsService);
        manager.Initialize(
            MobileControlNetworkMode.LocalNetwork,
            CloudflareTunnelProxyMode.Auto,
            string.Empty,
            clashMihomoCompatibilityEnabled: true,
            clashMihomoConfigPath: string.Empty,
            clashMihomoRoutePolicy: "Cloudflare Group");
        await using var lease = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49153/control?token=secret"),
            "/_igolibrary/health/test");

        var effective = await manager.SetModeAsync(MobileControlNetworkMode.CloudflareTunnel);

        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, effective);
        Assert.Equal("https://unit-test.trycloudflare.com/control?token=secret", lease.Url.ToString());
        Assert.Equal(new Uri("http://192.168.1.8:49153/control?token=secret"), lease.LanUrl);
        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, settingsService.CurrentSettings.MobileControl.NetworkMode);
        Assert.Equal([new Uri("http://192.168.1.8:49153/control?token=secret")], runner.Origins);
        var compatibility = Assert.Single(runner.CompatibilityOptions);
        Assert.True(compatibility.Enabled);
        Assert.Equal("Cloudflare Group", compatibility.RoutePolicy);

        effective = await manager.SetModeAsync(MobileControlNetworkMode.LocalNetwork);

        Assert.Equal(MobileControlNetworkMode.LocalNetwork, effective);
        Assert.Equal(lease.LanUrl, lease.Url);
        Assert.True(runner.Sessions.Single().Disposed);
        Assert.Equal(MobileControlNetworkMode.LocalNetwork, settingsService.CurrentSettings.MobileControl.NetworkMode);
    }

    [Fact]
    public async Task ExposureManager_ModePersistenceFailureDisposesAllPreparedTunnelsBeforeFallback()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.LocalNetwork);
        var persistenceFailure = new IOException("database unavailable");
        settingsService.UpdateExceptions.Enqueue(persistenceFailure);
        var runner = new FakeCloudflareQuickTunnelRunner();
        await using var manager = CreateManager(runner, settingsService);
        manager.Initialize(MobileControlNetworkMode.LocalNetwork, CloudflareTunnelProxyMode.Auto, string.Empty);
        await using var first = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49153/?token=one"),
            "/_igolibrary/health/one");
        await using var second = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49154/?token=two"),
            "/_igolibrary/health/two");

        var effective = await manager.SetModeAsync(MobileControlNetworkMode.CloudflareTunnel);

        Assert.Equal(MobileControlNetworkMode.LocalNetwork, effective);
        Assert.Equal(first.LanUrl, first.Url);
        Assert.Equal(second.LanUrl, second.Url);
        Assert.Equal(2, runner.Sessions.Count);
        Assert.All(runner.Sessions, session => Assert.True(session.Disposed));
        Assert.Equal(MobileControlNetworkMode.LocalNetwork, settingsService.CurrentSettings.MobileControl.NetworkMode);
    }

    [Fact]
    public async Task ExposureManager_ModePersistenceFailureDisposesAllPreparedTunnelsWithoutFallback()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.LocalNetwork);
        var persistenceFailure = new IOException("database unavailable");
        settingsService.UpdateExceptions.Enqueue(persistenceFailure);
        var runner = new FakeCloudflareQuickTunnelRunner();
        await using var manager = CreateManager(runner, settingsService);
        manager.Initialize(
            MobileControlNetworkMode.LocalNetwork,
            CloudflareTunnelProxyMode.Auto,
            string.Empty,
            fallbackToLocalNetworkOnTunnelFailure: false);
        await using var first = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49153/?token=one"),
            "/_igolibrary/health/one");
        await using var second = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49154/?token=two"),
            "/_igolibrary/health/two");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SetModeAsync(MobileControlNetworkMode.CloudflareTunnel));

        Assert.Same(persistenceFailure, exception.InnerException);
        Assert.Equal(MobileControlNetworkMode.LocalNetwork, manager.CurrentMode);
        Assert.Equal(first.LanUrl, first.Url);
        Assert.Equal(second.LanUrl, second.Url);
        Assert.Equal(2, runner.Sessions.Count);
        Assert.All(runner.Sessions, session => Assert.True(session.Disposed));
        Assert.Equal(MobileControlNetworkMode.LocalNetwork, settingsService.CurrentSettings.MobileControl.NetworkMode);
    }

    [Fact]
    public async Task ExposureManager_RuntimeFaultFallsBackAllLeasesAndPersistsLocalMode()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.CloudflareTunnel);
        var runner = new FakeCloudflareQuickTunnelRunner();
        await using var manager = CreateManager(runner, settingsService);
        manager.Initialize(MobileControlNetworkMode.CloudflareTunnel, CloudflareTunnelProxyMode.Auto, string.Empty);
        await using var first = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49153/?token=one"),
            "/_igolibrary/health/one");
        await using var second = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49154/?token=two"),
            "/_igolibrary/health/two");

        runner.Sessions[0].Fail("process exited");
        await WaitForAsync(() =>
            manager.CurrentMode == MobileControlNetworkMode.LocalNetwork &&
            settingsService.CurrentSettings.MobileControl.NetworkMode == MobileControlNetworkMode.LocalNetwork);

        Assert.Equal(first.LanUrl, first.Url);
        Assert.Equal(second.LanUrl, second.Url);
        Assert.All(runner.Sessions, session => Assert.True(session.Disposed));
        Assert.Equal(MobileControlNetworkMode.LocalNetwork, settingsService.CurrentSettings.MobileControl.NetworkMode);
    }

    [Fact]
    public async Task ExposureManager_TunnelStartupFailureKeepsLeaseOnLanAndPersistsFallback()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.CloudflareTunnel);
        var runner = new FakeCloudflareQuickTunnelRunner
        {
            StartException = new TimeoutException("not ready")
        };
        var logger = new CapturingNetworkExposureLogger();
        var notifications = new FakeNotificationService();
        await using var manager = CreateManager(runner, settingsService, logger, notifications);
        manager.Initialize(MobileControlNetworkMode.CloudflareTunnel, CloudflareTunnelProxyMode.Auto, string.Empty);

        await using var lease = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49153/?token=one"),
            "/_igolibrary/health/one");

        Assert.Equal(MobileControlNetworkMode.LocalNetwork, manager.CurrentMode);
        Assert.Equal(lease.LanUrl, lease.Url);
        Assert.Equal(MobileControlNetworkMode.LocalNetwork, settingsService.CurrentSettings.MobileControl.NetworkMode);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     ReferenceEquals(entry.Exception, runner.StartException) &&
                     entry.Message.Contains("publishing a service", StringComparison.Ordinal));
        var warning = Assert.Single(notifications.Warnings);
        Assert.Equal("Cloudflare Tunnel 已回退", warning.Title);
        Assert.Equal(
            "Cloudflare Tunnel 不可用，已自动回退到本机局域网。详情请查看日志",
            warning.Message);
        Assert.DoesNotContain("not ready", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExposureManager_TunnelStartupFailureDoesNotPublishLanWhenFallbackIsDisabled()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.CloudflareTunnel);
        var runner = new FakeCloudflareQuickTunnelRunner
        {
            StartException = new TimeoutException("not ready")
        };
        var notifications = new FakeNotificationService();
        await using var manager = CreateManager(runner, settingsService, notificationService: notifications);
        manager.Initialize(
            MobileControlNetworkMode.CloudflareTunnel,
            CloudflareTunnelProxyMode.Auto,
            string.Empty,
            fallbackToLocalNetworkOnTunnelFailure: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PublishAsync(
            new Uri("http://192.168.1.8:49153/?token=one"),
            "/_igolibrary/health/one"));

        Assert.Equal(NetworkExposureManager.TunnelStartupFailureUserMessage, exception.Message);
        Assert.Same(runner.StartException, exception.InnerException);
        Assert.DoesNotContain("not ready", exception.Message, StringComparison.Ordinal);
        Assert.Empty(notifications.Warnings);
        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, manager.CurrentMode);
        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, settingsService.CurrentSettings.MobileControl.NetworkMode);
    }

    [Fact]
    public async Task ExposureManager_ProxyConflictBlocksPublishWithoutStartingOrFallingBack()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.CloudflareTunnel);
        var conflict = new CloudflareTunnelProxyConflictException();
        var runner = new FakeCloudflareQuickTunnelRunner
        {
            ValidationException = conflict
        };
        var notifications = new FakeNotificationService();
        await using var manager = CreateManager(runner, settingsService, notificationService: notifications);
        manager.Initialize(
            MobileControlNetworkMode.CloudflareTunnel,
            CloudflareTunnelProxyMode.Auto,
            string.Empty,
            clashMihomoCompatibilityEnabled: true,
            clashMihomoRoutePolicy: "DIRECT");

        var exception = await Assert.ThrowsAsync<CloudflareTunnelProxyConflictException>(() => manager.PublishAsync(
            new Uri("http://192.168.1.8:49153/?token=one"),
            "/_igolibrary/health/one"));

        Assert.Same(conflict, exception);
        Assert.Equal(CloudflareTunnelProxyConflictException.UserMessage, exception.Message);
        Assert.Equal(1, runner.ValidationCallCount);
        Assert.Equal(0, runner.StartCallCount);
        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, manager.CurrentMode);
        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, settingsService.CurrentSettings.MobileControl.NetworkMode);
        Assert.Empty(notifications.Warnings);
    }

    [Fact]
    public async Task ExposureManager_ProxyConflictBlocksModeSwitchWithoutFallback()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.LocalNetwork);
        var conflict = new CloudflareTunnelProxyConflictException();
        var runner = new FakeCloudflareQuickTunnelRunner
        {
            ValidationException = conflict
        };
        var notifications = new FakeNotificationService();
        await using var manager = CreateManager(runner, settingsService, notificationService: notifications);
        manager.Initialize(
            MobileControlNetworkMode.LocalNetwork,
            CloudflareTunnelProxyMode.Auto,
            string.Empty,
            clashMihomoCompatibilityEnabled: true,
            clashMihomoRoutePolicy: "DIRECT");

        var exception = await Assert.ThrowsAsync<CloudflareTunnelProxyConflictException>(() =>
            manager.SetModeAsync(MobileControlNetworkMode.CloudflareTunnel));

        Assert.Same(conflict, exception);
        Assert.Equal(CloudflareTunnelProxyConflictException.UserMessage, exception.Message);
        Assert.Equal(1, runner.ValidationCallCount);
        Assert.Equal(0, runner.StartCallCount);
        Assert.Equal(MobileControlNetworkMode.LocalNetwork, manager.CurrentMode);
        Assert.Equal(MobileControlNetworkMode.LocalNetwork, settingsService.CurrentSettings.MobileControl.NetworkMode);
        Assert.Empty(notifications.Warnings);
    }

    [Fact]
    public async Task ExposureManager_MissingCloudflaredBlocksModeSwitchWithoutPersistenceOrFallback()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.LocalNetwork);
        var unavailable = new CloudflaredUnavailableException("missing-cloudflared.exe");
        var runner = new FakeCloudflareQuickTunnelRunner
        {
            ValidationException = unavailable
        };
        var notifications = new FakeNotificationService();
        await using var manager = CreateManager(runner, settingsService, notificationService: notifications);
        manager.Initialize(MobileControlNetworkMode.LocalNetwork, CloudflareTunnelProxyMode.Auto, string.Empty);

        var exception = await Assert.ThrowsAsync<CloudflaredUnavailableException>(() =>
            manager.SetModeAsync(MobileControlNetworkMode.CloudflareTunnel));

        Assert.Same(unavailable, exception);
        Assert.Equal(1, runner.ValidationCallCount);
        Assert.Equal(0, runner.StartCallCount);
        Assert.Equal(MobileControlNetworkMode.LocalNetwork, manager.CurrentMode);
        Assert.Equal(MobileControlNetworkMode.LocalNetwork, settingsService.CurrentSettings.MobileControl.NetworkMode);
        Assert.Empty(notifications.Warnings);
    }

    [Fact]
    public async Task ExposureManager_MissingCloudflaredBlocksPublishWithoutAutomaticFallback()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.CloudflareTunnel);
        var unavailable = new CloudflaredUnavailableException("missing-cloudflared.exe");
        var runner = new FakeCloudflareQuickTunnelRunner
        {
            ValidationException = unavailable
        };
        var notifications = new FakeNotificationService();
        await using var manager = CreateManager(runner, settingsService, notificationService: notifications);
        manager.Initialize(MobileControlNetworkMode.CloudflareTunnel, CloudflareTunnelProxyMode.Auto, string.Empty);

        var exception = await Assert.ThrowsAsync<CloudflaredUnavailableException>(() => manager.PublishAsync(
            new Uri("http://192.168.1.8:49153/?token=one"),
            "/_igolibrary/health/one"));

        Assert.Same(unavailable, exception);
        Assert.Equal(1, runner.ValidationCallCount);
        Assert.Equal(0, runner.StartCallCount);
        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, manager.CurrentMode);
        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, settingsService.CurrentSettings.MobileControl.NetworkMode);
        Assert.Empty(notifications.Warnings);
    }

    [Fact]
    public async Task ExposureManager_RuntimeFaultKeepsTunnelModeWhenFallbackIsDisabled()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.CloudflareTunnel);
        var runner = new FakeCloudflareQuickTunnelRunner();
        var notifications = new FakeNotificationService();
        await using var manager = CreateManager(runner, settingsService, notificationService: notifications);
        manager.Initialize(
            MobileControlNetworkMode.CloudflareTunnel,
            CloudflareTunnelProxyMode.Auto,
            string.Empty,
            fallbackToLocalNetworkOnTunnelFailure: false);
        await using var lease = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49153/?token=one"),
            "/_igolibrary/health/one");
        var tunnelUrl = lease.Url;

        runner.Sessions[0].Fail("process exited");
        await WaitForAsync(() => notifications.Warnings.Count == 1);

        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, manager.CurrentMode);
        Assert.Equal(tunnelUrl, lease.Url);
        Assert.True(runner.Sessions[0].Disposed);
        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, settingsService.CurrentSettings.MobileControl.NetworkMode);
        var warning = Assert.Single(notifications.Warnings);
        Assert.Equal("Cloudflare Tunnel 不可用", warning.Title);
        Assert.Equal(NetworkExposureManager.TunnelUnavailableWithoutFallbackUserMessage, warning.Message);
        Assert.DoesNotContain("process exited", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExposureManager_UpdatesAndPersistsTunnelFallbackPreference()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.LocalNetwork);
        await using var manager = CreateManager(new FakeCloudflareQuickTunnelRunner(), settingsService);
        manager.Initialize(MobileControlNetworkMode.LocalNetwork, CloudflareTunnelProxyMode.Auto, string.Empty);

        var saved = await manager.SetCloudflareTunnelFallbackAsync(false);

        Assert.False(saved.FallbackToLocalNetworkOnTunnelFailure);
        Assert.False(settingsService.CurrentSettings.MobileControl.FallbackToLocalNetworkOnTunnelFailure);
    }

    [Fact]
    public async Task ExposureManager_ProxyChangeAtomicallyReplacesActiveTunnelsAndPersistsSettings()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.CloudflareTunnel);
        var runner = new FakeCloudflareQuickTunnelRunner();
        await using var manager = CreateManager(runner, settingsService);
        manager.Initialize(MobileControlNetworkMode.CloudflareTunnel, CloudflareTunnelProxyMode.Auto, string.Empty);
        await using var first = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49153/control?token=one"),
            "/_igolibrary/health/one");
        await using var second = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49154/control?token=two"),
            "/_igolibrary/health/two");
        var oldSessions = runner.Sessions.ToArray();

        var saved = await manager.SetCloudflareTunnelProxyAsync(
            CloudflareTunnelProxyMode.ManualHttpProxy,
            "http://127.0.0.1:7897/");

        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, manager.CurrentMode);
        Assert.Equal(CloudflareTunnelProxyMode.ManualHttpProxy, saved.TunnelProxyMode);
        Assert.Equal("http://127.0.0.1:7897", saved.TunnelManualProxyUrl);
        Assert.All(oldSessions, session => Assert.True(session.Disposed));
        Assert.All(
            runner.ProxyOptions.TakeLast(2),
            options => Assert.Equal(CloudflareTunnelProxyMode.ManualHttpProxy, options.Mode));
        Assert.Contains("unit-test-2.trycloudflare.com", first.Url.Host, StringComparison.Ordinal);
        Assert.Contains("unit-test-3.trycloudflare.com", second.Url.Host, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExposureManager_ProxyChangeFailureKeepsOldTunnelsAndSettings()
    {
        var settingsService = CreateSettingsService(MobileControlNetworkMode.CloudflareTunnel);
        var runner = new FakeCloudflareQuickTunnelRunner();
        await using var manager = CreateManager(runner, settingsService);
        manager.Initialize(MobileControlNetworkMode.CloudflareTunnel, CloudflareTunnelProxyMode.Auto, string.Empty);
        await using var first = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49153/control?token=one"),
            "/_igolibrary/health/one");
        await using var second = await manager.PublishAsync(
            new Uri("http://192.168.1.8:49154/control?token=two"),
            "/_igolibrary/health/two");
        var oldUrls = new[] { first.Url, second.Url };
        var oldSessions = runner.Sessions.ToArray();
        runner.FailOnStartCall = runner.StartCallCount + 1;

        await Assert.ThrowsAsync<TimeoutException>(() => manager.SetCloudflareTunnelProxyAsync(
            CloudflareTunnelProxyMode.ManualHttpProxy,
            "http://127.0.0.1:7897"));

        Assert.Equal(oldUrls[0], first.Url);
        Assert.Equal(oldUrls[1], second.Url);
        Assert.All(oldSessions, session => Assert.False(session.Disposed));
        Assert.Equal(CloudflareTunnelProxyMode.Auto, settingsService.CurrentSettings.MobileControl.TunnelProxyMode);
    }

    private static FakeSettingsService CreateSettingsService(MobileControlNetworkMode mode)
    {
        return new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(49153, "test-token", NetworkMode: mode)
        });
    }

    private static NetworkExposureManager CreateManager(
        ICloudflareQuickTunnelRunner runner,
        FakeSettingsService settingsService,
        ILogger<NetworkExposureManager>? logger = null,
        INotificationService? notificationService = null)
    {
        return new NetworkExposureManager(
            runner,
            new SettingsWorkflowService(settingsService),
            new ActivityLogService(),
            notificationService ?? new FakeNotificationService(),
            logger ?? NullLogger<NetworkExposureManager>.Instance);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!predicate())
        {
            Assert.True(DateTime.UtcNow < timeout, "Timed out waiting for asynchronous fallback.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeCloudflareQuickTunnelRunner : ICloudflareQuickTunnelRunner
    {
        public Exception? ValidationException { get; set; }

        public Exception? StartException { get; set; }

        public int? FailOnStartCall { get; set; }

        public int StartCallCount { get; private set; }

        public int ValidationCallCount { get; private set; }

        public List<Uri> Origins { get; } = [];

        public List<CloudflareTunnelProxyOptions> ProxyOptions { get; } = [];

        public List<ClashMihomoCompatibilityOptions> CompatibilityOptions { get; } = [];

        public List<FakeCloudflareQuickTunnelSession> Sessions { get; } = [];

        public void ValidateConfiguration(
            CloudflareTunnelProxyOptions proxyOptions,
            ClashMihomoCompatibilityOptions compatibilityOptions)
        {
            ValidationCallCount++;
            if (ValidationException is not null)
            {
                throw ValidationException;
            }
        }

        public Task<ICloudflareQuickTunnelSession> StartAsync(
            Uri originBaseUri,
            string healthCheckPath,
            CloudflareTunnelProxyOptions proxyOptions,
            ClashMihomoCompatibilityOptions compatibilityOptions,
            CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            Origins.Add(originBaseUri);
            ProxyOptions.Add(proxyOptions);
            CompatibilityOptions.Add(compatibilityOptions);
            if (StartException is not null || StartCallCount == FailOnStartCall)
            {
                throw StartException ?? new TimeoutException("proxy replacement failed");
            }

            var session = new FakeCloudflareQuickTunnelSession(
                new Uri($"https://{(Sessions.Count == 0 ? "unit-test" : $"unit-test-{Sessions.Count}")}.trycloudflare.com/"));
            Sessions.Add(session);
            return Task.FromResult<ICloudflareQuickTunnelSession>(session);
        }
    }

    private sealed class FakeCloudflareQuickTunnelSession(Uri publicBaseUri) : ICloudflareQuickTunnelSession
    {
        private readonly TaskCompletionSource<CloudflareTunnelFault?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Uri PublicBaseUri { get; } = publicBaseUri;

        public Task<CloudflareTunnelFault?> Completion => _completion.Task;

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _completion.TrySetResult(null);
            return ValueTask.CompletedTask;
        }

        public void Fail(string message)
        {
            _completion.TrySetResult(new CloudflareTunnelFault(message));
        }
    }

    private sealed class CapturingNetworkExposureLogger : ILogger<NetworkExposureManager>
    {
        public List<CapturedLogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record CapturedLogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class FakeSystemProxyProvider(IWebProxy proxy) : ICloudflareSystemProxyProvider
    {
        public IWebProxy GetDefaultProxy() => proxy;
    }

    private sealed class FakeWebProxy(Uri? proxyUri) : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        public Uri? GetProxy(Uri destination) => proxyUri ?? destination;

        public bool IsBypassed(Uri host) => proxyUri is null;
    }

    private sealed class CapturingQuickTunnelLogger : ILogger<CloudflareQuickTunnelRunner>
    {
        public List<CapturedLogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return sendAsync(request, cancellationToken);
        }
    }
}

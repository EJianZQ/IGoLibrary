using System.Security.Cryptography;
using System.Text;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class CookieExpirationAlertMonitorTests
{
    [Fact]
    public async Task CheckAsync_IgnoresMissingAndUnparseableCookies_AndLogsWithoutSecretsOnce()
    {
        var now = CreateTimestamp();
        var state = new AppRuntimeState();
        var dispatcher = new FakeTaskEventAlertDispatcher();
        var activityLog = new ActivityLogService();
        var monitor = CreateMonitor(state, dispatcher, activityLog, new FakeTimeProvider(now));

        await monitor.CheckAsync();

        const string cookie = "Authorization=opaque-secret-value; SERVERID=private";
        state.Session = CreateSession(cookie, now);
        await monitor.CheckAsync();
        await monitor.CheckAsync();

        Assert.Empty(dispatcher.CookieExpiringNotifications);
        var warning = Assert.Single(activityLog.Entries, entry => entry.Kind == LogEntryKind.Warning);
        Assert.Contains("无法解析当前 Cookie 的到期时间", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(cookie, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization=", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ComputeFingerprint(cookie), warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_LogsEachUnparseableCookieOnlyOncePerRun_AcrossCookieChanges()
    {
        var now = CreateTimestamp();
        var state = new AppRuntimeState();
        var activityLog = new ActivityLogService();
        var monitor = CreateMonitor(
            state,
            new FakeTaskEventAlertDispatcher(),
            activityLog,
            new FakeTimeProvider(now));
        const string firstCookie = "Authorization=first-opaque-secret; SERVERID=private";
        const string secondCookie = "Authorization=second-opaque-secret; SERVERID=private";

        state.Session = CreateSession(firstCookie, now);
        await monitor.CheckAsync();
        state.Session = CreateSession(secondCookie, now);
        await monitor.CheckAsync();
        state.Session = null;
        await monitor.CheckAsync();
        state.Session = CreateSession(firstCookie, now);
        await monitor.CheckAsync();

        var warnings = activityLog.Entries
            .Where(entry => entry.Kind == LogEntryKind.Warning)
            .ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, warning =>
        {
            Assert.DoesNotContain(firstCookie, warning.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secondCookie, warning.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task CheckAsync_NotifiesWhenRemainingTimeReachesExactlyTenMinutes()
    {
        var now = CreateTimestamp();
        var timeProvider = new FakeTimeProvider(now);
        var expirationTime = now.AddMinutes(10).AddSeconds(1);
        var state = new AppRuntimeState
        {
            Session = CreateSession(BuildAuthorizationCookie(expirationTime), now)
        };
        var dispatcher = new FakeTaskEventAlertDispatcher();
        var monitor = CreateMonitor(state, dispatcher, new ActivityLogService(), timeProvider);

        await monitor.CheckAsync();
        Assert.Empty(dispatcher.CookieExpiringNotifications);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await monitor.CheckAsync();

        var notification = Assert.Single(dispatcher.CookieExpiringNotifications);
        Assert.Equal(expirationTime.ToUnixTimeSeconds(), notification.ExpirationTime.ToUnixTimeSeconds());
        Assert.Equal(TimeSpan.FromMinutes(10), notification.Remaining);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task CheckAsync_DoesNotNotifyWhenCookieIsAlreadyExpired(int secondsUntilExpiration)
    {
        var now = CreateTimestamp();
        var state = new AppRuntimeState
        {
            Session = CreateSession(BuildAuthorizationCookie(now.AddSeconds(secondsUntilExpiration)), now)
        };
        var dispatcher = new FakeTaskEventAlertDispatcher();
        var monitor = CreateMonitor(state, dispatcher, new ActivityLogService(), new FakeTimeProvider(now));

        await monitor.CheckAsync();

        Assert.Empty(dispatcher.CookieExpiringNotifications);
    }

    [Fact]
    public async Task CheckAsync_NotifiesImmediatelyInsideWindow_AndOncePerCookiePerRun()
    {
        var now = CreateTimestamp();
        var state = new AppRuntimeState
        {
            Session = CreateSession(BuildAuthorizationCookie(now.AddMinutes(5), "first"), now)
        };
        var dispatcher = new FakeTaskEventAlertDispatcher();
        var monitor = CreateMonitor(state, dispatcher, new ActivityLogService(), new FakeTimeProvider(now));

        await monitor.CheckAsync();
        await monitor.CheckAsync();
        state.Session = CreateSession(BuildAuthorizationCookie(now.AddMinutes(5), "second"), now);
        await monitor.CheckAsync();

        Assert.Equal(2, dispatcher.CookieExpiringNotifications.Count);
    }

    [Fact]
    public async Task CheckAsync_DisabledEventDoesNotConsumeEligibility_AndRetriesWithBackoff()
    {
        var now = CreateTimestamp();
        var timeProvider = new FakeTimeProvider(now);
        var state = new AppRuntimeState
        {
            Session = CreateSession(BuildAuthorizationCookie(now.AddMinutes(5)), now)
        };
        var dispatcher = new FakeTaskEventAlertDispatcher { CookieExpiringAccepted = false };
        var monitor = CreateMonitor(state, dispatcher, new ActivityLogService(), timeProvider);

        await monitor.CheckAsync();
        dispatcher.CookieExpiringAccepted = true;
        timeProvider.Advance(TimeSpan.FromSeconds(14));
        await monitor.CheckAsync();
        Assert.Single(dispatcher.CookieExpiringNotifications);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await monitor.CheckAsync();
        await monitor.CheckAsync();

        Assert.Equal(2, dispatcher.CookieExpiringNotifications.Count);
    }

    [Fact]
    public async Task CheckAsync_DisabledEventRetriesBeforeExpiration_WhenLessThanBackoffRemains()
    {
        var now = CreateTimestamp();
        var timeProvider = new FakeTimeProvider(now);
        var state = new AppRuntimeState
        {
            Session = CreateSession(BuildAuthorizationCookie(now.AddSeconds(10)), now)
        };
        var dispatcher = new FakeTaskEventAlertDispatcher { CookieExpiringAccepted = false };
        var monitor = CreateMonitor(state, dispatcher, new ActivityLogService(), timeProvider);

        await monitor.CheckAsync();
        dispatcher.CookieExpiringAccepted = true;
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await monitor.CheckAsync();

        Assert.Equal(2, dispatcher.CookieExpiringNotifications.Count);
    }

    [Fact]
    public async Task CheckAsync_DispatchFailureCanRetryWithoutDuplicateDelivery()
    {
        var now = CreateTimestamp();
        var timeProvider = new FakeTimeProvider(now);
        var state = new AppRuntimeState
        {
            Session = CreateSession(BuildAuthorizationCookie(now.AddMinutes(5)), now)
        };
        var dispatcher = new FakeTaskEventAlertDispatcher
        {
            NotifyCookieExpiringException = new InvalidOperationException("settings unavailable")
        };
        var monitor = CreateMonitor(state, dispatcher, new ActivityLogService(), timeProvider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.CheckAsync());
        dispatcher.NotifyCookieExpiringException = null;
        timeProvider.Advance(TimeSpan.FromSeconds(15));

        await monitor.CheckAsync();
        await monitor.CheckAsync();

        Assert.Single(dispatcher.CookieExpiringNotifications);
    }

    [Fact]
    public async Task HostedService_DetectsSessionAfterStartup_AndStopsWithoutFailureLog()
    {
        var now = CreateTimestamp();
        var timeProvider = new FakeTimeProvider(now);
        var state = new AppRuntimeState();
        var dispatcher = new FakeTaskEventAlertDispatcher();
        var activityLog = new ActivityLogService();
        var monitor = CreateMonitor(state, dispatcher, activityLog, timeProvider);
        var hostedService = new CookieExpirationAlertHostedService(monitor, activityLog, timeProvider);

        await hostedService.StartAsync(CancellationToken.None);
        state.Session = CreateSession(BuildAuthorizationCookie(now.AddMinutes(5)), now);
        timeProvider.Advance(CookieExpirationAlertHostedService.CheckInterval);
        await dispatcher.CookieExpiringNotificationReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(CancellationToken.None);

        Assert.Single(dispatcher.CookieExpiringNotifications);
        Assert.DoesNotContain(
            activityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning &&
                     entry.Message.Contains("后台监测失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostedService_StopsDuringDispatch_WithoutFailureOrAcceptedLogs()
    {
        var now = CreateTimestamp();
        var state = new AppRuntimeState
        {
            Session = CreateSession(BuildAuthorizationCookie(now.AddMinutes(5)), now)
        };
        var dispatchStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatchBlocker = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new FakeTaskEventAlertDispatcher
        {
            CookieExpiringStarted = dispatchStarted,
            CookieExpiringBlocker = dispatchBlocker.Task
        };
        var activityLog = new ActivityLogService();
        var monitor = CreateMonitor(state, dispatcher, activityLog, new FakeTimeProvider(now));
        var hostedService = new CookieExpirationAlertHostedService(
            monitor,
            activityLog,
            new FakeTimeProvider(now));

        await hostedService.StartAsync(CancellationToken.None);
        await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hostedService.StopAsync(CancellationToken.None);

        Assert.Empty(dispatcher.CookieExpiringNotifications);
        Assert.DoesNotContain(
            activityLog.Entries,
            entry => entry.Kind is LogEntryKind.Warning or LogEntryKind.Success);
    }

    private static CookieExpirationAlertMonitor CreateMonitor(
        AppRuntimeState state,
        FakeTaskEventAlertDispatcher dispatcher,
        ActivityLogService activityLog,
        TimeProvider timeProvider)
    {
        return new CookieExpirationAlertMonitor(state, dispatcher, activityLog, timeProvider);
    }

    private static SessionCredentials CreateSession(string cookie, DateTimeOffset savedAt)
        => new(cookie, SessionSource.ManualCookie, savedAt, true);

    private static DateTimeOffset CreateTimestamp()
        => new(2026, 7, 16, 10, 0, 0, TimeSpan.FromHours(8));

    private static string BuildAuthorizationCookie(DateTimeOffset expiresAt, string tag = "cookie-monitor-test")
    {
        var header = Base64Url("""{"typ":"JWT","alg":"RS256"}""");
        var payload = Base64Url($$"""{"expireAt":{{expiresAt.ToUnixTimeSeconds()}},"tag":"{{tag}}"}""");
        return $"Authorization={header}.{payload}.signature; SERVERID=private";
    }

    private static string Base64Url(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ComputeFingerprint(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

}

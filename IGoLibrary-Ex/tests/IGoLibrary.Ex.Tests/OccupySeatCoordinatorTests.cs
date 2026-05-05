using System.Net;
using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class OccupySeatCoordinatorTests
{
    [Fact]
    public async Task StartAsync_RetriesReserveAfterCancellation_UntilSuccess()
    {
        var reserveSucceeded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reserveAttempts = 0;
        var reservation = new ReservationInfo(
            "token",
            1,
            "自科阅览区一",
            "seat-1",
            "1号座",
            DateTimeOffset.Now.AddSeconds(15));

        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) => Task.FromResult<ReservationInfo?>(reservation),
            OnCancelReservationAsync = (_, _, _) => Task.FromResult(true),
            OnReserveSeatAsync = (_, _, _, _) =>
            {
                reserveAttempts++;
                if (reserveAttempts >= 2)
                {
                    reserveSucceeded.TrySetResult();
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
        };

        var notificationService = new FakeNotificationService();
        var activityLogService = new ActivityLogService();
        var settingsService = new FakeSettingsService(AppSettings.Default with { RetryCount = 2 });
        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = new OccupySeatCoordinator(
            apiClient,
            settingsService,
            notificationService,
            new FakeTaskAlertService(),
            activityLogService,
            runtimeState);

        using var cts = new CancellationTokenSource();
        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, RefreshMode.FixedTenSeconds), cts.Token);

        await reserveSucceeded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await coordinator.StopAsync();

        Assert.Equal(2, reserveAttempts);
        Assert.Contains(activityLogService.Entries, entry => entry.Category == "Occupy" && entry.Message.Contains("重新预约尝试成功"));
        Assert.Contains(notificationService.Successes, x => x.Title == "占座成功");
        Assert.NotEqual(CoordinatorTaskState.Failed, coordinator.GetStatus().State);
    }

    [Fact]
    public async Task StartAsync_NotifiesCookieExpiry_WhenReservationRefreshReturnsUnauthorized()
    {
        var notificationService = new FakeNotificationService();
        var alertService = new FakeTaskAlertService();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) => Task.FromException<ReservationInfo?>(
                new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized))
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = new OccupySeatCoordinator(
            apiClient,
            new FakeSettingsService(AppSettings.Default),
            notificationService,
            alertService,
            new ActivityLogService(),
            runtimeState);

        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, RefreshMode.FixedTenSeconds));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);

        var alert = Assert.Single(alertService.CookieExpiredNotifications);
        Assert.Equal("占座轮询", alert.Source);
        Assert.Empty(notificationService.Warnings);
    }

    [Fact]
    public async Task StartAsync_NotifiesCookieExpiry_FromExpiredJwt_WithoutRefreshingReservation()
    {
        var reservationInfoCallCount = 0;
        var alertService = new FakeTaskAlertService();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) =>
            {
                reservationInfoCallCount++;
                throw new InvalidOperationException("过期 JWT 不应继续刷新预约状态。");
            }
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials(
                BuildAuthorizationCookie(DateTimeOffset.Now.AddSeconds(-1)),
                SessionSource.ManualCookie,
                DateTimeOffset.Now,
                true)
        };
        var coordinator = new OccupySeatCoordinator(
            apiClient,
            new FakeSettingsService(AppSettings.Default),
            new FakeNotificationService(),
            alertService,
            new ActivityLogService(),
            runtimeState);

        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, RefreshMode.FixedTenSeconds));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);

        Assert.Equal(0, reservationInfoCallCount);
        var alert = Assert.Single(alertService.CookieExpiredNotifications);
        Assert.Equal("占座轮询", alert.Source);
        Assert.Contains("Cookie 已过期", alert.Reason);
    }

    [Fact]
    public async Task StartAsync_NotifiesTaskFailure_WhenReservationRefreshFailsWithoutCookieExpiry()
    {
        var notificationService = new FakeNotificationService();
        var alertService = new FakeTaskAlertService();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) => Task.FromException<ReservationInfo?>(
                new InvalidOperationException("预约状态获取失败"))
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = new OccupySeatCoordinator(
            apiClient,
            new FakeSettingsService(AppSettings.Default),
            notificationService,
            alertService,
            new ActivityLogService(),
            runtimeState);

        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, RefreshMode.FixedTenSeconds));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);

        var failure = Assert.Single(alertService.TaskFailedNotifications);
        Assert.Equal("占座", failure.TaskName);
        Assert.Equal("预约状态获取失败", failure.Reason);
        Assert.Empty(notificationService.Warnings);
    }

    private static async Task WaitForStatusAsync(IOccupySeatCoordinator coordinator, CoordinatorTaskState expectedState)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!timeout.IsCancellationRequested)
        {
            if (coordinator.GetStatus().State == expectedState)
            {
                return;
            }

            await Task.Delay(50, timeout.Token);
        }

        throw new TimeoutException($"Expected status {expectedState} was not observed.");
    }

    private static string BuildAuthorizationCookie(DateTimeOffset expiresAt)
    {
        var header = Base64Url("""{"typ":"JWT","alg":"RS256"}""");
        var payload = Base64Url($$"""{"userId":37580434,"schId":20175,"expireAt":{{expiresAt.ToUnixTimeSeconds()}},"tag":"cookie-test"}""");
        return $"Authorization={header}.{payload}.signature; SERVERID=d3936289adfff6c3874a2579058ac651|1777956374|1777956374";
    }

    private static string Base64Url(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

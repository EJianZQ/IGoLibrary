using System.Net;
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
            new FakeCookieExpiryAlertService(),
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
        var alertService = new FakeCookieExpiryAlertService();
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
}

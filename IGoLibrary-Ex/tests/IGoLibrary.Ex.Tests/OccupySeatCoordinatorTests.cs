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
}

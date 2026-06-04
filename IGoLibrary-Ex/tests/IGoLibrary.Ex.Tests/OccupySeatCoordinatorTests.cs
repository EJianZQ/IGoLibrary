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

        var eventPublisher = new FakeCoordinatorEventPublisher();
        var activityLogService = new ActivityLogService();
        var runtime = new FakeCoordinatorRuntime
        {
            BlockDelaysStartingAtCall = 3
        };
        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default with
            {
                Network = AppSettings.Default.Network with { MaxRetries = 2 }
            },
            eventPublisher: eventPublisher,
            activityLogService: activityLogService,
            runtimeState: runtimeState,
            runtime: runtime);

        using var cts = new CancellationTokenSource();
        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds), cts.Token);

        await reserveSucceeded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await coordinator.StopAsync();

        Assert.Equal(2, reserveAttempts);
        Assert.Contains(activityLogService.Entries, entry => entry.Category == "Occupy" && entry.Message.Contains("重新预约尝试成功"));
        await WaitForAsync(() => eventPublisher.EventsOf<OccupyReReserveSucceededCoordinatorEvent>().Count > 0);
        Assert.Contains(eventPublisher.EventsOf<OccupyReReserveSucceededCoordinatorEvent>(), x => x.SeatName == "1号座");
        Assert.Equal(CoordinatorStatusReason.Stopped, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task StartAsync_NotifiesSessionInvalid_WhenReservationRefreshReturnsUnauthorized()
    {
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) => Task.FromException<ReservationInfo?>(
                new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized))
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default,
            eventPublisher: eventPublisher,
            runtimeState: runtimeState);

        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);
        await WaitForAsync(() => eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>().Count == 1);

        var alert = Assert.Single(eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>());
        Assert.Equal("占座轮询", alert.Source);
        Assert.Equal(CoordinatorStatusReason.SessionInvalid, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task StartAsync_NotifiesSessionInvalid_FromExpiredJwt_WithoutRefreshingReservation()
    {
        var reservationInfoCallCount = 0;
        var eventPublisher = new FakeCoordinatorEventPublisher();
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
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default,
            eventPublisher: eventPublisher,
            runtimeState: runtimeState);

        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);

        Assert.Equal(0, reservationInfoCallCount);
        await WaitForAsync(() => eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>().Count == 1);
        var alert = Assert.Single(eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>());
        Assert.Equal("占座轮询", alert.Source);
        Assert.Contains("Cookie 已过期", alert.Reason);
        Assert.Equal(CoordinatorStatusReason.SessionInvalid, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task StartAsync_NotifiesTaskFailure_WhenReservationRefreshFailsWithoutSessionInvalid()
    {
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) => Task.FromException<ReservationInfo?>(
                new InvalidOperationException("预约状态获取失败"))
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default,
            eventPublisher: eventPublisher,
            runtimeState: runtimeState);

        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);
        await WaitForAsync(() => eventPublisher.EventsOf<TaskFailedCoordinatorEvent>().Count == 1);

        var failure = Assert.Single(eventPublisher.EventsOf<TaskFailedCoordinatorEvent>());
        Assert.Equal("占座", failure.TaskName);
        Assert.Equal("预约状态获取失败", failure.Reason);
        Assert.Equal(CoordinatorStatusReason.TaskFailed, coordinator.GetStatus().Reason);
    }

    private static OccupySeatCoordinator CreateCoordinator(
        FakeTraceIntApiClient apiClient,
        AppSettings settings,
        FakeCoordinatorEventPublisher? eventPublisher = null,
        ActivityLogService? activityLogService = null,
        AppRuntimeState? runtimeState = null,
        ICoordinatorRuntime? runtime = null)
    {
        activityLogService ??= new ActivityLogService();
        runtime ??= new FakeCoordinatorRuntime();
        eventPublisher ??= new FakeCoordinatorEventPublisher();
        runtimeState ??= new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var reReservationExecutor = new OccupyReReservationExecutor(
            apiClient,
            activityLogService,
            runtime);

        var stateMachine = new OccupySeatWorkflowRunner(
            new FakeSettingsService(settings),
            apiClient,
            reReservationExecutor,
            eventPublisher,
            activityLogService,
            runtimeState,
            runtimeState,
            runtime);

        return new OccupySeatCoordinator(
            stateMachine,
            runtime);
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

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!timeout.IsCancellationRequested)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(25, timeout.Token);
        }

        throw new TimeoutException("Expected condition was not observed.");
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

using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class OccupySeatWorkflowRunnerTests
{
    [Fact]
    public async Task FixedCheckIntervalMode_UsesTenSecondRuntimeDelay_WhenReservationIsNotNearExpiration()
    {
        var runtime = CreateBlockingRuntime();
        var reservation = CreateReservation(runtime.Now.AddMinutes(2));
        var coordinator = CreateCoordinator(
            new FakeTraceIntApiClient
            {
                OnGetReservationInfoAsync = (_, _) => Task.FromResult<ReservationInfo?>(reservation)
            },
            runtime);

        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds));
        await runtime.DelayStarted!.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.StopAsync();

        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(runtime.DelayRequests));
        Assert.Equal(CoordinatorStatusReason.Stopped, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task RandomCheckIntervalMode_UsesRuntimeNextIntBetweenTenAndTwentySeconds()
    {
        var runtime = CreateBlockingRuntime();
        runtime.EnqueueNextInt(17);
        var reservation = CreateReservation(runtime.Now.AddMinutes(2));
        var coordinator = CreateCoordinator(
            new FakeTraceIntApiClient
            {
                OnGetReservationInfoAsync = (_, _) => Task.FromResult<ReservationInfo?>(reservation)
            },
            runtime);

        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.RandomTenToTwentySeconds));
        await runtime.DelayStarted!.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.StopAsync();

        Assert.Equal(TimeSpan.FromSeconds(17), Assert.Single(runtime.DelayRequests));
    }

    [Fact]
    public async Task ReReserveSuccess_PublishesEventAndKeepsTaskRunning()
    {
        var runtime = new FakeCoordinatorRuntime
        {
            BlockDelaysStartingAtCall = 2
        };
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var reservation = CreateReservation(runtime.Now.AddSeconds(15));
        var coordinator = CreateCoordinator(
            new FakeTraceIntApiClient
            {
                OnGetReservationInfoAsync = (_, _) => Task.FromResult<ReservationInfo?>(reservation),
                OnCancelReservationAsync = (_, _, _) => Task.FromResult(true),
                OnReserveSeatAsync = (_, _, _, _) => Task.FromResult(true)
            },
            runtime,
            eventPublisher);

        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds));
        await WaitForReasonAsync(coordinator, CoordinatorStatusReason.OccupyReReserveSucceeded);
        await WaitForAsync(() => eventPublisher.EventsOf<OccupyReReserveSucceededCoordinatorEvent>().Count == 1);

        Assert.Equal(CoordinatorTaskState.Running, coordinator.GetStatus().State);
        Assert.Contains(eventPublisher.EventsOf<OccupyReReserveSucceededCoordinatorEvent>(), x => x.SeatName == "1号座");

        await coordinator.StopAsync();
        Assert.Equal(CoordinatorStatusReason.Stopped, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task ReReserveSuccess_ReturnsReasonToRunningAfterSuccessPause()
    {
        var runtime = new FakeCoordinatorRuntime
        {
            BlockDelaysStartingAtCall = 3
        };
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var reservationCalls = 0;
        var coordinator = CreateCoordinator(
            new FakeTraceIntApiClient
            {
                OnGetReservationInfoAsync = (_, _) =>
                {
                    reservationCalls++;
                    return Task.FromResult<ReservationInfo?>(CreateReservation(
                        reservationCalls == 1
                            ? runtime.Now.AddSeconds(15)
                            : runtime.Now.AddMinutes(2)));
                },
                OnCancelReservationAsync = (_, _, _) => Task.FromResult(true),
                OnReserveSeatAsync = (_, _, _, _) => Task.FromResult(true)
            },
            runtime,
            eventPublisher);

        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds));
        await WaitForAsync(() =>
            eventPublisher.EventsOf<OccupyReReserveSucceededCoordinatorEvent>().Count == 1 &&
            runtime.DelayRequests.Count >= 3 &&
            coordinator.GetStatus().Reason == CoordinatorStatusReason.Running);

        Assert.Equal(CoordinatorTaskState.Running, coordinator.GetStatus().State);
        Assert.Single(eventPublisher.EventsOf<OccupyReReserveSucceededCoordinatorEvent>());

        await coordinator.StopAsync();
    }

    [Fact]
    public async Task ExpiredJwtClassification_UsesRuntimeClock()
    {
        var expiresAt = DateTimeOffset.Now.AddHours(1);
        var runtime = new FakeCoordinatorRuntime
        {
            Now = expiresAt.AddSeconds(1)
        };
        var reservationInfoCalls = 0;
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var coordinator = CreateCoordinator(
            new FakeTraceIntApiClient
            {
                OnGetReservationInfoAsync = (_, _) =>
                {
                    reservationInfoCalls++;
                    throw new InvalidOperationException("过期 JWT 不应继续刷新预约状态。");
                }
            },
            runtime,
            eventPublisher,
            new SessionCredentials(BuildAuthorizationCookie(expiresAt), SessionSource.ManualCookie, DateTimeOffset.Now, true));

        await coordinator.StartAsync(new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);

        Assert.Equal(0, reservationInfoCalls);
        Assert.Equal(CoordinatorStatusReason.SessionInvalid, coordinator.GetStatus().Reason);
        await WaitForAsync(() => eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>().Count == 1);
        Assert.Single(eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>());
    }

    private static FakeCoordinatorRuntime CreateBlockingRuntime()
    {
        return new FakeCoordinatorRuntime
        {
            CompleteDelaysImmediately = false,
            DelayStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
    }

    private static OccupySeatCoordinator CreateCoordinator(
        FakeTraceIntApiClient apiClient,
        ICoordinatorRuntime runtime,
        FakeCoordinatorEventPublisher? eventPublisher = null,
        SessionCredentials? session = null)
    {
        var activityLogService = new ActivityLogService();
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var runtimeState = new AppRuntimeState
        {
            Session = session ?? new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var reReservationExecutor = new OccupyReReservationExecutor(
            apiClient,
            activityLogService,
            runtime);
        var stateMachine = new OccupySeatWorkflowRunner(
            settingsService,
            apiClient,
            reReservationExecutor,
            eventPublisher ?? new FakeCoordinatorEventPublisher(),
            activityLogService,
            runtimeState,
            runtimeState,
            runtime);

        return new OccupySeatCoordinator(stateMachine, runtime);
    }

    private static ReservationInfo CreateReservation(DateTimeOffset expirationTime)
    {
        return new ReservationInfo(
            "token",
            1,
            "自科阅览区一",
            "seat-1",
            "1号座",
            expirationTime);
    }

    private static async Task WaitForReasonAsync(
        IOccupySeatCoordinator coordinator,
        CoordinatorStatusReason expectedReason)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!timeout.IsCancellationRequested)
        {
            if (coordinator.GetStatus().Reason == expectedReason)
            {
                return;
            }

            await Task.Delay(25, timeout.Token);
        }

        throw new TimeoutException($"Expected reason {expectedReason} was not observed.");
    }

    private static async Task WaitForStatusAsync(
        IOccupySeatCoordinator coordinator,
        CoordinatorTaskState expectedState)
    {
        await WaitForAsync(() => coordinator.GetStatus().State == expectedState);
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
        return $"Authorization={header}.{payload}.signature";
    }

    private static string Base64Url(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

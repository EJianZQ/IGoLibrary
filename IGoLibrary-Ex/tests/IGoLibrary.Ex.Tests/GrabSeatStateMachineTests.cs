using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class GrabSeatStateMachineTests
{
    [Fact]
    public async Task ScheduledStart_UsesRuntimeDelayBeforePolling()
    {
        var runtime = new FakeCoordinatorRuntime
        {
            Now = new DateTimeOffset(2026, 5, 8, 21, 30, 0, TimeSpan.FromHours(8)),
            AdvanceOnDelay = true
        };
        var apiClient = new FakeTraceIntApiClient
        {
            OnReserveSeatAsync = (_, _, _, _) => Task.FromResult(true)
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default with { Tasks = new TaskExecutionSettings(GrabReservationStrategy.ReserveDirectly) },
            runtime);

        await coordinator.StartAsync(CreatePlan(
            new GrabSeatPollingStrategy(TimeSpan.Zero, TimeSpan.Zero, 0, TimeSpan.Zero, TimeSpan.Zero),
            scheduledStart: new TimeOnly(21, 30, 2)));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)], runtime.DelayRequests.Take(2));
        Assert.Equal(CoordinatorStatusReason.GrabSucceeded, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task RateLimitBackoff_UsesMaximumOfPollingDelayAndThreeSeconds()
    {
        var runtime = new FakeCoordinatorRuntime();
        var reserveCalls = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnReserveSeatAsync = (_, _, _, _) =>
            {
                reserveCalls++;
                return reserveCalls == 1
                    ? Task.FromException<bool>(new InvalidOperationException("GraphQL 错误(code=1): 请重新尝试"))
                    : Task.FromResult(true);
            }
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default with { Tasks = new TaskExecutionSettings(GrabReservationStrategy.ReserveDirectly) },
            runtime);

        await coordinator.StartAsync(CreatePlan(
            new GrabSeatPollingStrategy(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(1),
                0,
                TimeSpan.Zero,
                TimeSpan.Zero)));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Contains(TimeSpan.FromSeconds(3), runtime.DelayRequests);
        Assert.Equal(CoordinatorStatusReason.GrabSucceeded, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task CancellationDuringPollingDelay_CompletesWithStoppedReason()
    {
        var runtime = new FakeCoordinatorRuntime
        {
            CompleteDelaysImmediately = false,
            DelayStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var layout = new LibraryLayout(
            1,
            "自科阅览区一",
            "3层",
            true,
            10,
            0,
            0,
            [new SeatSnapshot("seat-1", "1号座", false, 0, 0)]);
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) => Task.FromResult(layout)
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default with { Tasks = new TaskExecutionSettings(GrabReservationStrategy.QueryThenReserve) },
            runtime);

        await coordinator.StartAsync(CreatePlan(
            new GrabSeatPollingStrategy(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7), 0, TimeSpan.Zero, TimeSpan.Zero)));
        await runtime.DelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.StopAsync();

        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(runtime.DelayRequests));
        Assert.Equal(CoordinatorTaskState.Completed, coordinator.GetStatus().State);
        Assert.Equal(CoordinatorStatusReason.Stopped, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task ExpiredJwtClassification_UsesRuntimeClock()
    {
        var expiresAt = DateTimeOffset.Now.AddHours(1);
        var runtime = new FakeCoordinatorRuntime
        {
            Now = expiresAt.AddSeconds(1)
        };
        var layoutCalls = 0;
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) =>
            {
                layoutCalls++;
                throw new InvalidOperationException("过期 JWT 不应继续请求场馆布局。");
            }
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default with { Tasks = new TaskExecutionSettings(GrabReservationStrategy.QueryThenReserve) },
            runtime,
            eventPublisher,
            new SessionCredentials(BuildAuthorizationCookie(expiresAt), SessionSource.ManualCookie, DateTimeOffset.Now, true));

        await coordinator.StartAsync(CreatePlan(
            new GrabSeatPollingStrategy(TimeSpan.Zero, TimeSpan.Zero, 0, TimeSpan.Zero, TimeSpan.Zero)));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);

        Assert.Equal(0, layoutCalls);
        Assert.Equal(CoordinatorStatusReason.SessionInvalid, coordinator.GetStatus().Reason);
        await WaitForAsync(() => eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>().Count == 1);
        Assert.Single(eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>());
    }

    private static GrabSeatCoordinator CreateCoordinator(
        FakeTraceIntApiClient apiClient,
        AppSettings settings,
        ICoordinatorRuntime runtime,
        FakeCoordinatorEventPublisher? eventPublisher = null,
        SessionCredentials? session = null)
    {
        var activityLogService = new ActivityLogService();
        var runtimeState = new AppRuntimeState
        {
            Session = session ?? new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var strategySelector = new GrabReservationStrategySelector(
            [
                new QueryThenReserveGrabReservationStrategy(apiClient, activityLogService, runtimeState),
                new DirectReserveGrabReservationStrategy(apiClient, activityLogService, runtime)
            ]);
        var stateMachine = new GrabSeatStateMachine(
            new FakeSettingsService(settings),
            strategySelector,
            eventPublisher ?? new FakeCoordinatorEventPublisher(),
            activityLogService,
            runtimeState,
            runtime);

        return new GrabSeatCoordinator(stateMachine, runtime);
    }

    private static GrabSeatPlan CreatePlan(
        GrabSeatPollingStrategy pollingStrategy,
        TimeOnly? scheduledStart = null)
    {
        return new GrabSeatPlan(
            1,
            "自科阅览区一",
            [new TrackedSeat("seat-1", "1号座")],
            GrabPollingMode.Aggressive,
            pollingStrategy,
            scheduledStart);
    }

    private static async Task WaitForStatusAsync(
        IGrabSeatCoordinator coordinator,
        CoordinatorTaskState expectedState)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!timeout.IsCancellationRequested)
        {
            if (coordinator.GetStatus().State == expectedState)
            {
                return;
            }

            await Task.Delay(25, timeout.Token);
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

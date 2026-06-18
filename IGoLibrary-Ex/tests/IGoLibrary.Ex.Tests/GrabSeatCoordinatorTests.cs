using System.Net;
using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class GrabSeatCoordinatorTests
{
    [Fact]
    public async Task StartAsync_UsesDirectReservationStrategy_WithoutLoadingLayout()
    {
        var layoutCallCount = 0;
        var reserveCallCount = 0;
        var reserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventPublisher = new FakeCoordinatorEventPublisher();

        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) =>
            {
                layoutCallCount++;
                throw new InvalidOperationException("直接预约策略不应加载场馆布局。");
            },
            OnReserveSeatAsync = (_, _, seatKey, _) =>
            {
                reserveCallCount++;
                if (seatKey == "seat-2")
                {
                    reserved.TrySetResult();
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default with { Tasks = new TaskExecutionSettings(GrabReservationStrategy.ReserveDirectly) },
            eventPublisher: eventPublisher,
            runtimeState: runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [
                new SeatReference("seat-1", "1号座"),
                new SeatReference("seat-2", "2号座")
            ],
            GrabPollingMode.Aggressive,
            GrabPollingStrategyFactory.FromMode(GrabPollingMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await reserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal(0, layoutCallCount);
        Assert.Equal(2, reserveCallCount);
        var status = coordinator.GetStatus();
        Assert.Equal(CoordinatorTaskState.Completed, status.State);
        Assert.Equal(CoordinatorStatusReason.GrabSucceeded, status.Reason);
        await WaitForAsync(() => eventPublisher.EventsOf<GrabSucceededCoordinatorEvent>().Count > 0);
        Assert.Contains(
            eventPublisher.EventsOf<GrabSucceededCoordinatorEvent>(),
            item => item.LibraryName == "自科阅览区一" && item.SeatName == "2号座");
    }

    [Fact]
    public async Task StartAsync_MarksCompletedBeforeSlowSuccessAlertFinishes()
    {
        var alertCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventPublisher = new FakeCoordinatorEventPublisher
        {
            GrabSucceededCompletion = alertCompletion
        };
        var apiClient = new FakeTraceIntApiClient
        {
            OnReserveSeatAsync = (_, _, _, _) => Task.FromResult(true)
        };
        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default with { Tasks = new TaskExecutionSettings(GrabReservationStrategy.ReserveDirectly) },
            eventPublisher: eventPublisher,
            runtimeState: runtimeState);
        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [new SeatReference("seat-1", "1号座")],
            GrabPollingMode.Aggressive,
            GrabPollingStrategyFactory.FromMode(GrabPollingMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);
        await WaitForAsync(() => eventPublisher.EventsOf<GrabSucceededCoordinatorEvent>().Count == 1);

        Assert.Single(eventPublisher.EventsOf<GrabSucceededCoordinatorEvent>());
        alertCompletion.SetResult();
    }

    [Fact]
    public async Task StartAsync_DirectReservationStrategy_DoesNotFailWhenSeatAlreadyBooked()
    {
        var layoutCallCount = 0;
        var reserveCallCount = 0;
        var reserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activityLogService = new ActivityLogService();

        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) =>
            {
                layoutCallCount++;
                throw new InvalidOperationException("直接预约策略不应加载场馆布局。");
            },
            OnReserveSeatAsync = (_, _, seatKey, _) =>
            {
                reserveCallCount++;
                if (seatKey == "seat-1")
                {
                    return Task.FromException<bool>(new InvalidOperationException("GraphQL 错误(code=1): 该座位已经被人预定了!"));
                }

                reserved.TrySetResult();
                return Task.FromResult(true);
            }
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default with { Tasks = new TaskExecutionSettings(GrabReservationStrategy.ReserveDirectly) },
            activityLogService: activityLogService,
            runtimeState: runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [
                new SeatReference("seat-1", "1号座"),
                new SeatReference("seat-2", "2号座")
            ],
            GrabPollingMode.Aggressive,
            GrabPollingStrategyFactory.FromMode(GrabPollingMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await reserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal(0, layoutCallCount);
        Assert.Equal(2, reserveCallCount);
        var status = coordinator.GetStatus();
        Assert.Equal(CoordinatorTaskState.Completed, status.State);
        Assert.Equal(CoordinatorStatusReason.GrabSucceeded, status.Reason);
        Assert.Contains(activityLogService.Entries, entry => entry.Category == "Grab" && entry.Message.Contains("继续尝试下一个目标座位"));
    }

    [Fact]
    public async Task StartAsync_DirectReservationStrategy_DoesNotFailWhenApiAsksToRetry()
    {
        var reserveCallCount = 0;
        var reserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activityLogService = new ActivityLogService();

        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) =>
                throw new InvalidOperationException("直接预约策略不应加载场馆布局。"),
            OnReserveSeatAsync = (_, _, seatKey, _) =>
            {
                reserveCallCount++;
                if (seatKey == "seat-1")
                {
                    return Task.FromException<bool>(new InvalidOperationException("GraphQL 错误(code=1): 请重新尝试"));
                }

                reserved.TrySetResult();
                return Task.FromResult(true);
            }
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default with { Tasks = new TaskExecutionSettings(GrabReservationStrategy.ReserveDirectly) },
            activityLogService: activityLogService,
            runtimeState: runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [
                new SeatReference("seat-1", "12"),
                new SeatReference("seat-2", "18")
            ],
            GrabPollingMode.Aggressive,
            GrabPollingStrategyFactory.FromMode(GrabPollingMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await reserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal(2, reserveCallCount);
        var status = coordinator.GetStatus();
        Assert.Equal(CoordinatorTaskState.Completed, status.State);
        Assert.Equal(CoordinatorStatusReason.GrabSucceeded, status.Reason);
        Assert.Contains(activityLogService.Entries, entry => entry.Category == "Grab" && entry.Message.Contains("请重新尝试"));
    }

    [Fact]
    public async Task StartAsync_UsesQueryThenReserveStrategy_BeforeCallingReserve()
    {
        var layoutCallCount = 0;
        var reserveCallCount = 0;
        var reserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) =>
            {
                layoutCallCount++;
                return Task.FromResult(new LibraryLayout(
                    1,
                    "自科阅览区一",
                    "3层",
                    true,
                    10,
                    0,
                    0,
                    [
                        new SeatSnapshot("seat-1", "1号座", true, 0, 0),
                        new SeatSnapshot("seat-2", "2号座", false, 1, 0)
                    ]));
            },
            OnReserveSeatAsync = (_, _, seatKey, _) =>
            {
                reserveCallCount++;
                if (seatKey == "seat-2")
                {
                    reserved.TrySetResult();
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default with { Tasks = new TaskExecutionSettings(GrabReservationStrategy.QueryThenReserve) },
            runtimeState: runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [
                new SeatReference("seat-1", "1号座"),
                new SeatReference("seat-2", "2号座")
            ],
            GrabPollingMode.Aggressive,
            GrabPollingStrategyFactory.FromMode(GrabPollingMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await reserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal(1, layoutCallCount);
        Assert.Equal(1, reserveCallCount);
        Assert.NotNull(runtimeState.CurrentLayout);
        var status = coordinator.GetStatus();
        Assert.Equal(CoordinatorTaskState.Completed, status.State);
        Assert.Equal(CoordinatorStatusReason.GrabSucceeded, status.Reason);
    }

    [Fact]
    public async Task StartAsync_NotifiesSessionInvalid_WhenPollingReceivesUnauthorized()
    {
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) => Task.FromException<LibraryLayout>(
                new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized))
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default with { Tasks = new TaskExecutionSettings(GrabReservationStrategy.QueryThenReserve) },
            eventPublisher: eventPublisher,
            runtimeState: runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [new SeatReference("seat-1", "1号座")],
            GrabPollingMode.Aggressive,
            GrabPollingStrategyFactory.FromMode(GrabPollingMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);
        await WaitForAsync(() => eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>().Count == 1);

        var alert = Assert.Single(eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>());
        Assert.Equal("抢座轮询", alert.Source);
        Assert.Equal(CoordinatorStatusReason.SessionInvalid, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task StartAsync_NotifiesSessionInvalid_FromApiAuthorizationFailure_EvenWhenJwtLooksExpired()
    {
        var layoutCallCount = 0;
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) =>
            {
                layoutCallCount++;
                throw new TraceIntApiException("access denied!", 40001, "access denied!", isAuthorizationDenied: true);
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
            AppSettings.Default with { Tasks = new TaskExecutionSettings(GrabReservationStrategy.QueryThenReserve) },
            eventPublisher: eventPublisher,
            runtimeState: runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [new SeatReference("seat-1", "1号座")],
            GrabPollingMode.Aggressive,
            GrabPollingStrategyFactory.FromMode(GrabPollingMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);
        await WaitForAsync(() => eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>().Count == 1);

        Assert.Equal(1, layoutCallCount);
        var alert = Assert.Single(eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>());
        Assert.Equal("抢座轮询", alert.Source);
        Assert.Contains("access denied", alert.Reason);
        Assert.Equal(CoordinatorStatusReason.SessionInvalid, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task StartAsync_NotifiesTaskFailure_WhenPollingFailsWithoutSessionInvalid()
    {
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) => Task.FromException<LibraryLayout>(
                new InvalidOperationException("场馆接口暂时不可用"))
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = CreateCoordinator(
            apiClient,
            AppSettings.Default with { Tasks = new TaskExecutionSettings(GrabReservationStrategy.QueryThenReserve) },
            eventPublisher: eventPublisher,
            runtimeState: runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [new SeatReference("seat-1", "1号座")],
            GrabPollingMode.Aggressive,
            GrabPollingStrategyFactory.FromMode(GrabPollingMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);
        await WaitForAsync(() => eventPublisher.EventsOf<TaskFailedCoordinatorEvent>().Count == 1);

        var failure = Assert.Single(eventPublisher.EventsOf<TaskFailedCoordinatorEvent>());
        Assert.Equal("抢座", failure.TaskName);
        Assert.Equal("场馆接口暂时不可用", failure.Reason);
        Assert.Equal(CoordinatorStatusReason.TaskFailed, coordinator.GetStatus().Reason);
    }

    [Fact]
    public void ResolveNextScheduledStart_ReturnsToday_WhenScheduledTimeIsLater()
    {
        var now = new DateTimeOffset(2026, 5, 8, 21, 30, 15, TimeSpan.FromHours(8));

        var target = GrabSeatCoordinator.ResolveNextScheduledStart(new TimeOnly(22, 0, 0), now);

        Assert.Equal(new DateTimeOffset(2026, 5, 8, 22, 0, 0, TimeSpan.FromHours(8)), target);
    }

    [Fact]
    public void ResolveNextScheduledStart_ReturnsTomorrow_WhenScheduledTimeAlreadyPassed()
    {
        var now = new DateTimeOffset(2026, 5, 8, 21, 30, 15, TimeSpan.FromHours(8));

        var target = GrabSeatCoordinator.ResolveNextScheduledStart(new TimeOnly(20, 0, 0), now);

        Assert.Equal(new DateTimeOffset(2026, 5, 9, 20, 0, 0, TimeSpan.FromHours(8)), target);
    }

    [Fact]
    public void ResolveNextScheduledStart_ReturnsNow_WhenScheduledTimeMatchesCurrentInstant()
    {
        var now = new DateTimeOffset(2026, 5, 8, 21, 30, 0, TimeSpan.FromHours(8));

        var target = GrabSeatCoordinator.ResolveNextScheduledStart(new TimeOnly(21, 30, 0), now);

        Assert.Equal(now, target);
    }

    private static GrabSeatCoordinator CreateCoordinator(
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
        var strategySelector = new GrabReservationStrategySelector(
            [
                new QueryThenReserveGrabReservationStrategy(apiClient, activityLogService),
                new DirectReserveGrabReservationStrategy(apiClient, activityLogService, runtime)
            ]);

        var stateMachine = new GrabSeatWorkflowRunner(
            new FakeSettingsService(settings),
            strategySelector,
            eventPublisher,
            activityLogService,
            runtimeState,
            runtimeState,
            runtime);

        return new GrabSeatCoordinator(
            stateMachine,
            runtime);
    }

    private static async Task WaitForStatusAsync(IGrabSeatCoordinator coordinator, CoordinatorTaskState expectedState)
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

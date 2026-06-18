using System.Net;
using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class TomorrowReservationCoordinatorTests
{
    [Fact]
    public async Task StartAsync_ContinuesWhenWarmUpFails_AndPublishesSuccess()
    {
        var saveCalls = 0;
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var apiClient = new FakeTraceIntApiClient
        {
            OnEnterTomorrowReservationQueueAsync = (_, _) =>
                Task.FromResult(TomorrowReservationQueueResult.Continue("排队成功")),
            OnWarmUpTomorrowReservationAsync = (_, _, _) =>
                Task.FromException(new InvalidOperationException("预热暂时失败")),
            OnSaveTomorrowReservationAsync = (_, libraryId, seatKey, _) =>
            {
                saveCalls++;
                Assert.Equal(117580, libraryId);
                Assert.Equal("seat-001", seatKey);
                return Task.FromResult(true);
            },
            OnGetTomorrowReservationInfoAsync = (_, _) =>
                Task.FromResult<TomorrowReservationInfo?>(new TomorrowReservationInfo(
                    "2026-06-09",
                    117580,
                    "seat-001",
                    "001",
                    false))
        };

        var coordinator = CreateCoordinator(apiClient, eventPublisher: eventPublisher);

        await coordinator.StartAsync(CreatePlan(executeImmediately: true));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);
        await WaitForAsync(() => eventPublisher.EventsOf<TomorrowReservationSucceededCoordinatorEvent>().Count == 1);

        Assert.Equal(1, saveCalls);
        Assert.Equal(CoordinatorStatusReason.TomorrowReservationSucceeded, coordinator.GetStatus().Reason);
        var success = Assert.Single(eventPublisher.EventsOf<TomorrowReservationSucceededCoordinatorEvent>());
        Assert.Equal("自科阅览区一", success.LibraryName);
        Assert.Equal("001", success.SeatName);
        Assert.Equal("2026-06-09", success.Day);
    }

    [Fact]
    public async Task StartAsync_FailsWithoutSaving_WhenQueueIntercepts()
    {
        var saveCalls = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnEnterTomorrowReservationQueueAsync = (_, _) =>
                Task.FromResult(TomorrowReservationQueueResult.Stop("未开始")),
            OnSaveTomorrowReservationAsync = (_, _, _, _) =>
            {
                saveCalls++;
                return Task.FromResult(true);
            }
        };
        var coordinator = CreateCoordinator(apiClient);

        await coordinator.StartAsync(CreatePlan(executeImmediately: true));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);

        Assert.Equal(0, saveCalls);
        Assert.Equal(CoordinatorStatusReason.TaskFailed, coordinator.GetStatus().Reason);
        Assert.Contains("排队被拦截", coordinator.GetStatus().Message);
    }

    [Fact]
    public async Task StartAsync_NotifiesSessionInvalid_FromApiAuthorizationFailure_EvenWhenJwtLooksExpired()
    {
        var queueCalls = 0;
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var apiClient = new FakeTraceIntApiClient
        {
            OnEnterTomorrowReservationQueueAsync = (_, _) =>
            {
                queueCalls++;
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
            eventPublisher: eventPublisher,
            runtimeState: runtimeState);

        await coordinator.StartAsync(CreatePlan(executeImmediately: true));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);
        await WaitForAsync(() => eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>().Count == 1);

        Assert.Equal(1, queueCalls);
        Assert.Equal(CoordinatorStatusReason.SessionInvalid, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task StartAsync_CompletesWithoutConfirmedText_WhenVerificationRecordDoesNotMatchPlan()
    {
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var activityLogService = new ActivityLogService();
        var apiClient = new FakeTraceIntApiClient
        {
            OnEnterTomorrowReservationQueueAsync = (_, _) =>
                Task.FromResult(TomorrowReservationQueueResult.Continue("排队成功")),
            OnSaveTomorrowReservationAsync = (_, _, _, _) => Task.FromResult(true),
            OnGetTomorrowReservationInfoAsync = (_, _) =>
                Task.FromResult<TomorrowReservationInfo?>(new TomorrowReservationInfo(
                    "2026-06-09",
                    999001,
                    "other-seat",
                    "其他座位",
                    false))
        };
        var coordinator = CreateCoordinator(
            apiClient,
            eventPublisher: eventPublisher,
            activityLogService: activityLogService);

        await coordinator.StartAsync(CreatePlan(executeImmediately: true));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);
        await WaitForAsync(() => eventPublisher.EventsOf<TomorrowReservationSucceededCoordinatorEvent>().Count == 1);

        Assert.Equal(CoordinatorStatusReason.TomorrowReservationSucceeded, coordinator.GetStatus().Reason);
        Assert.Contains("验证记录与本次目标不一致", coordinator.GetStatus().Message);
        Assert.DoesNotContain("已确认", coordinator.GetStatus().Message);
        var success = Assert.Single(eventPublisher.EventsOf<TomorrowReservationSucceededCoordinatorEvent>());
        Assert.Equal("001", success.SeatName);
        Assert.Null(success.Day);
        Assert.Contains(activityLogService.Entries, entry =>
            entry.Kind == LogEntryKind.Warning &&
            entry.Category == "Tomorrow" &&
            entry.Message.Contains("验证记录与本次目标不一致", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_Completes_WhenVerificationQueryFails()
    {
        var activityLogService = new ActivityLogService();
        var apiClient = new FakeTraceIntApiClient
        {
            OnEnterTomorrowReservationQueueAsync = (_, _) =>
                Task.FromResult(TomorrowReservationQueueResult.Continue("排队成功")),
            OnSaveTomorrowReservationAsync = (_, _, _, _) => Task.FromResult(true),
            OnGetTomorrowReservationInfoAsync = (_, _) =>
                Task.FromException<TomorrowReservationInfo?>(new InvalidOperationException("验证接口暂不可用"))
        };
        var coordinator = CreateCoordinator(apiClient, activityLogService: activityLogService);

        await coordinator.StartAsync(CreatePlan(executeImmediately: true));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal(CoordinatorStatusReason.TomorrowReservationSucceeded, coordinator.GetStatus().Reason);
        Assert.Contains("验证记录暂未返回", coordinator.GetStatus().Message);
        Assert.Contains(activityLogService.Entries, entry =>
            entry.Kind == LogEntryKind.Warning &&
            entry.Category == "Tomorrow" &&
            entry.Message.Contains("明日预约结果验证失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_DoesNotQueue_WhenScheduledWaitIsAlreadyCanceled()
    {
        var queueCalls = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnEnterTomorrowReservationQueueAsync = (_, _) =>
            {
                queueCalls++;
                return Task.FromResult(TomorrowReservationQueueResult.Continue());
            }
        };
        var runtime = new FakeCoordinatorRuntime
        {
            Now = new DateTimeOffset(2026, 6, 8, 20, 0, 0, TimeSpan.FromHours(8))
        };
        var runner = new TomorrowReservationWorkflowRunner(
            apiClient,
            new FakeCoordinatorEventPublisher(),
            new ActivityLogService(),
            new AppRuntimeState
            {
                Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
            },
            runtime);
        var controller = new CoordinatorRunController("明日预约", runtime);
        var context = new CoordinatorRunContext(controller);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            runner.RunAsync(CreatePlan(executeImmediately: false), context, cts.Token));

        Assert.Equal(0, queueCalls);
    }

    [Fact]
    public void ResolveNextScheduledStart_ReturnsTomorrow_WhenScheduledTimeEqualsNow()
    {
        var now = new DateTimeOffset(2026, 6, 8, 21, 48, 0, TimeSpan.FromHours(8));

        var target = TomorrowReservationCoordinator.ResolveNextScheduledStart(new TimeOnly(21, 48, 0), now);

        Assert.Equal(new DateTimeOffset(2026, 6, 9, 21, 48, 0, TimeSpan.FromHours(8)), target);
    }

    private static TomorrowReservationCoordinator CreateCoordinator(
        FakeTraceIntApiClient apiClient,
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

        var workflowRunner = new TomorrowReservationWorkflowRunner(
            apiClient,
            eventPublisher,
            activityLogService,
            runtimeState,
            runtime);

        return new TomorrowReservationCoordinator(workflowRunner, runtime);
    }

    private static TomorrowReservationPlan CreatePlan(bool executeImmediately)
    {
        return new TomorrowReservationPlan(
            117580,
            "自科阅览区一",
            new SeatReference("seat-001", "001"),
            new TimeOnly(21, 48, 0),
            executeImmediately);
    }

    private static async Task WaitForStatusAsync(ITomorrowReservationCoordinator coordinator, CoordinatorTaskState expectedState)
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

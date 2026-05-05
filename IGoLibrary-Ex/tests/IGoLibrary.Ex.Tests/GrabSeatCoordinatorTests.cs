using System.Net;
using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
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
        var alertService = new FakeTaskAlertService();

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
        var coordinator = new GrabSeatCoordinator(
            apiClient,
            new FakeSettingsService(AppSettings.Default with { GrabReservationStrategy = GrabReservationStrategy.ReserveDirectly }),
            alertService,
            new ActivityLogService(),
            runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [
                new TrackedSeat("seat-1", "1号座"),
                new TrackedSeat("seat-2", "2号座")
            ],
            GrabMode.Aggressive,
            GrabStrategyFactory.FromMode(GrabMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await reserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal(0, layoutCallCount);
        Assert.Equal(2, reserveCallCount);
        Assert.Equal(CoordinatorTaskState.Completed, coordinator.GetStatus().State);
        Assert.Contains(alertService.GrabSucceededNotifications, item => item.LibraryName == "自科阅览区一" && item.SeatName == "2号座");
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
        var coordinator = new GrabSeatCoordinator(
            apiClient,
            new FakeSettingsService(AppSettings.Default with { GrabReservationStrategy = GrabReservationStrategy.ReserveDirectly }),
            new FakeTaskAlertService(),
            activityLogService,
            runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [
                new TrackedSeat("seat-1", "1号座"),
                new TrackedSeat("seat-2", "2号座")
            ],
            GrabMode.Aggressive,
            GrabStrategyFactory.FromMode(GrabMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await reserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal(0, layoutCallCount);
        Assert.Equal(2, reserveCallCount);
        Assert.Equal(CoordinatorTaskState.Completed, coordinator.GetStatus().State);
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
        var coordinator = new GrabSeatCoordinator(
            apiClient,
            new FakeSettingsService(AppSettings.Default with { GrabReservationStrategy = GrabReservationStrategy.ReserveDirectly }),
            new FakeTaskAlertService(),
            activityLogService,
            runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [
                new TrackedSeat("seat-1", "12"),
                new TrackedSeat("seat-2", "18")
            ],
            GrabMode.Aggressive,
            GrabStrategyFactory.FromMode(GrabMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await reserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal(2, reserveCallCount);
        Assert.Equal(CoordinatorTaskState.Completed, coordinator.GetStatus().State);
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
        var coordinator = new GrabSeatCoordinator(
            apiClient,
            new FakeSettingsService(AppSettings.Default with { GrabReservationStrategy = GrabReservationStrategy.QueryThenReserve }),
            new FakeTaskAlertService(),
            new ActivityLogService(),
            runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [
                new TrackedSeat("seat-1", "1号座"),
                new TrackedSeat("seat-2", "2号座")
            ],
            GrabMode.Aggressive,
            GrabStrategyFactory.FromMode(GrabMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await reserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal(1, layoutCallCount);
        Assert.Equal(1, reserveCallCount);
        Assert.NotNull(runtimeState.CurrentLayout);
        Assert.Equal(CoordinatorTaskState.Completed, coordinator.GetStatus().State);
    }

    [Fact]
    public async Task StartAsync_NotifiesCookieExpiry_WhenPollingReceivesUnauthorized()
    {
        var notificationService = new FakeNotificationService();
        var alertService = new FakeTaskAlertService();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) => Task.FromException<LibraryLayout>(
                new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized))
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = new GrabSeatCoordinator(
            apiClient,
            new FakeSettingsService(AppSettings.Default with { GrabReservationStrategy = GrabReservationStrategy.QueryThenReserve }),
            alertService,
            new ActivityLogService(),
            runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [new TrackedSeat("seat-1", "1号座")],
            GrabMode.Aggressive,
            GrabStrategyFactory.FromMode(GrabMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);

        var alert = Assert.Single(alertService.CookieExpiredNotifications);
        Assert.Equal("抢座轮询", alert.Source);
        Assert.Empty(notificationService.Warnings);
    }

    [Fact]
    public async Task StartAsync_NotifiesCookieExpiry_FromExpiredJwt_WithoutPollingApi()
    {
        var layoutCallCount = 0;
        var alertService = new FakeTaskAlertService();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) =>
            {
                layoutCallCount++;
                throw new InvalidOperationException("过期 JWT 不应继续请求场馆布局。");
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
        var coordinator = new GrabSeatCoordinator(
            apiClient,
            new FakeSettingsService(AppSettings.Default with { GrabReservationStrategy = GrabReservationStrategy.QueryThenReserve }),
            alertService,
            new ActivityLogService(),
            runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [new TrackedSeat("seat-1", "1号座")],
            GrabMode.Aggressive,
            GrabStrategyFactory.FromMode(GrabMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);

        Assert.Equal(0, layoutCallCount);
        var alert = Assert.Single(alertService.CookieExpiredNotifications);
        Assert.Equal("抢座轮询", alert.Source);
        Assert.Contains("Cookie 已过期", alert.Reason);
    }

    [Fact]
    public async Task StartAsync_NotifiesTaskFailure_WhenPollingFailsWithoutCookieExpiry()
    {
        var notificationService = new FakeNotificationService();
        var alertService = new FakeTaskAlertService();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) => Task.FromException<LibraryLayout>(
                new InvalidOperationException("场馆接口暂时不可用"))
        };

        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var coordinator = new GrabSeatCoordinator(
            apiClient,
            new FakeSettingsService(AppSettings.Default with { GrabReservationStrategy = GrabReservationStrategy.QueryThenReserve }),
            alertService,
            new ActivityLogService(),
            runtimeState);

        var plan = new GrabSeatPlan(
            1,
            "自科阅览区一",
            [new TrackedSeat("seat-1", "1号座")],
            GrabMode.Aggressive,
            GrabStrategyFactory.FromMode(GrabMode.Aggressive),
            null);

        await coordinator.StartAsync(plan);
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);

        var failure = Assert.Single(alertService.TaskFailedNotifications);
        Assert.Equal("抢座", failure.TaskName);
        Assert.Equal("场馆接口暂时不可用", failure.Reason);
        Assert.Empty(notificationService.Warnings);
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

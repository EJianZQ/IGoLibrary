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
            new FakeNotificationService(),
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
            new FakeNotificationService(),
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
            new FakeNotificationService(),
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
            new FakeNotificationService(),
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
}

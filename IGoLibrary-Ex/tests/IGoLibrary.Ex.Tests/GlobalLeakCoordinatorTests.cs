using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class GlobalLeakCoordinatorTests
{
    [Fact]
    public async Task StartAsync_ScansSelectedLibrariesInOrder_AndWaitsForNextRound_WhenNoSeatAvailable()
    {
        var layoutCalls = new List<int>();
        var runtime = new FakeCoordinatorRuntime
        {
            BlockDelaysStartingAtCall = 1,
            DelayStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, libraryId, _) =>
            {
                layoutCalls.Add(libraryId);
                return Task.FromResult(CreateLayout(libraryId, $"场馆{libraryId}", []));
            }
        };
        var coordinator = CreateCoordinator(apiClient, runtime: runtime);

        await coordinator.StartAsync(CreatePlan(TimeSpan.FromSeconds(3)));
        await runtime.DelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2], layoutCalls);
        Assert.Equal([TimeSpan.FromSeconds(3)], runtime.DelayRequests);
        var status = coordinator.GetStatus();
        Assert.Equal(CoordinatorTaskState.Running, status.State);
        Assert.Equal(1, status.PollCount);
        Assert.Equal(2, status.RequestCount);

        await coordinator.StopAsync();
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);
        Assert.Equal(CoordinatorStatusReason.Stopped, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task StartAsync_ReservesFirstAvailableSeat_AndPublishesSuccess()
    {
        var layoutCalls = 0;
        var reserveCalls = 0;
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, libraryId, _) =>
            {
                layoutCalls++;
                return Task.FromResult(CreateLayout(
                    libraryId,
                    "自科阅览区一",
                    [new SeatSnapshot("seat-1", "001", false, 0, 0)]));
            },
            OnReserveSeatAsync = (_, libraryId, seatKey, _) =>
            {
                reserveCalls++;
                Assert.Equal(1, libraryId);
                Assert.Equal("seat-1", seatKey);
                return Task.FromResult(true);
            }
        };
        var coordinator = CreateCoordinator(apiClient, eventPublisher: eventPublisher);

        await coordinator.StartAsync(CreatePlan(TimeSpan.FromSeconds(10)));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);
        await WaitForAsync(() => eventPublisher.EventsOf<GlobalLeakSucceededCoordinatorEvent>().Count == 1);

        Assert.Equal(1, layoutCalls);
        Assert.Equal(1, reserveCalls);
        var status = coordinator.GetStatus();
        Assert.Equal(CoordinatorStatusReason.GlobalLeakSucceeded, status.Reason);
        Assert.Equal(1, status.PollCount);
        Assert.Equal(2, status.RequestCount);
        var success = Assert.Single(eventPublisher.EventsOf<GlobalLeakSucceededCoordinatorEvent>());
        Assert.Equal("自科阅览区一", success.LibraryName);
        Assert.Equal("001", success.SeatName);
    }

    [Fact]
    public async Task StartAsync_ContinuesWithNextSeat_WhenFirstReservationMisses()
    {
        var reserveSeatKeys = new List<string>();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, libraryId, _) =>
                Task.FromResult(CreateLayout(
                    libraryId,
                    "自科阅览区一",
                    [
                        new SeatSnapshot("seat-1", "001", false, 0, 0),
                        new SeatSnapshot("seat-2", "002", false, 1, 0)
                    ])),
            OnReserveSeatAsync = (_, _, seatKey, _) =>
            {
                reserveSeatKeys.Add(seatKey);
                return seatKey == "seat-1"
                    ? Task.FromException<bool>(new InvalidOperationException("GraphQL 错误(code=1): 该座位已经被人预定了!"))
                    : Task.FromResult(true);
            }
        };
        var coordinator = CreateCoordinator(apiClient);

        await coordinator.StartAsync(CreatePlan(TimeSpan.FromSeconds(10)));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal(["seat-1", "seat-2"], reserveSeatKeys);
        Assert.Equal(CoordinatorStatusReason.GlobalLeakSucceeded, coordinator.GetStatus().Reason);
        Assert.Equal(3, coordinator.GetStatus().RequestCount);
    }

    [Fact]
    public async Task StartAsync_BacksOffBeforeContinuing_WhenReservationRequestsRetry()
    {
        var reserveSeatKeys = new List<string>();
        var runtime = new FakeCoordinatorRuntime();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, libraryId, _) =>
                Task.FromResult(CreateLayout(
                    libraryId,
                    "自科阅览区一",
                    [
                        new SeatSnapshot("seat-1", "001", false, 0, 0),
                        new SeatSnapshot("seat-2", "002", false, 1, 0)
                    ])),
            OnReserveSeatAsync = (_, _, seatKey, _) =>
            {
                reserveSeatKeys.Add(seatKey);
                return seatKey == "seat-1"
                    ? Task.FromException<bool>(new InvalidOperationException("GraphQL 错误(code=1): 请重新尝试"))
                    : Task.FromResult(true);
            }
        };
        var coordinator = CreateCoordinator(apiClient, runtime: runtime);

        await coordinator.StartAsync(CreatePlan(TimeSpan.FromSeconds(10)));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal(["seat-1", "seat-2"], reserveSeatKeys);
        Assert.Equal([TimeSpan.FromSeconds(2)], runtime.DelayRequests);
        Assert.Equal(CoordinatorStatusReason.GlobalLeakSucceeded, coordinator.GetStatus().Reason);
        Assert.Equal(3, coordinator.GetStatus().RequestCount);
    }

    [Fact]
    public async Task StartAsync_NotifiesSessionInvalid_FromExpiredCookie_WithoutScanning()
    {
        var layoutCalls = 0;
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) =>
            {
                layoutCalls++;
                return Task.FromResult(CreateLayout(1, "自科阅览区一", []));
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

        await coordinator.StartAsync(CreatePlan(TimeSpan.FromSeconds(10)));
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Failed);
        await WaitForAsync(() => eventPublisher.EventsOf<SessionInvalidCoordinatorEvent>().Count == 1);

        Assert.Equal(0, layoutCalls);
        Assert.Equal(CoordinatorStatusReason.SessionInvalid, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task StopAsync_CancelsPollingWait()
    {
        var runtime = new FakeCoordinatorRuntime
        {
            BlockDelaysStartingAtCall = 1,
            DelayStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, libraryId, _) =>
                Task.FromResult(CreateLayout(libraryId, $"场馆{libraryId}", []))
        };
        var coordinator = CreateCoordinator(apiClient, runtime: runtime);

        await coordinator.StartAsync(CreatePlan(TimeSpan.FromSeconds(1)));
        await runtime.DelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await coordinator.StopAsync();
        await WaitForStatusAsync(coordinator, CoordinatorTaskState.Completed);

        Assert.Equal(CoordinatorStatusReason.Stopped, coordinator.GetStatus().Reason);
    }

    private static GlobalLeakCoordinator CreateCoordinator(
        FakeTraceIntApiClient apiClient,
        FakeCoordinatorEventPublisher? eventPublisher = null,
        ActivityLogService? activityLogService = null,
        AppRuntimeState? runtimeState = null,
        ICoordinatorRuntime? runtime = null)
    {
        activityLogService ??= new ActivityLogService();
        eventPublisher ??= new FakeCoordinatorEventPublisher();
        runtime ??= new FakeCoordinatorRuntime();
        runtimeState ??= new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };

        var runner = new GlobalLeakWorkflowRunner(
            apiClient,
            eventPublisher,
            activityLogService,
            runtimeState,
            runtime);

        return new GlobalLeakCoordinator(runner, runtime);
    }

    private static GlobalLeakPlan CreatePlan(TimeSpan scanInterval)
    {
        return new GlobalLeakPlan(
            [
                new GlobalLeakLibraryTarget(1, "自科阅览区一", "3层"),
                new GlobalLeakLibraryTarget(2, "社科阅览区二", "5层")
            ],
            scanInterval);
    }

    private static LibraryLayout CreateLayout(int libraryId, string name, IReadOnlyList<SeatSnapshot> seats)
    {
        var usedSeats = seats.Count(seat => seat.IsOccupied);
        return new LibraryLayout(
            libraryId,
            name,
            "3层",
            true,
            seats.Count,
            0,
            usedSeats,
            seats);
    }

    private static async Task WaitForStatusAsync(IGlobalLeakCoordinator coordinator, CoordinatorTaskState expectedState)
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

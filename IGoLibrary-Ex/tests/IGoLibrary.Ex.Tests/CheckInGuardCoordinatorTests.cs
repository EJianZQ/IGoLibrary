using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class CheckInGuardCoordinatorTests
{
    [Fact]
    public async Task StartAsync_SendsReminderBeforeDeadline_WithoutCancellingReservation()
    {
        var runtime = new FakeCoordinatorRuntime
        {
            Now = DateTimeOffset.Now,
            CompleteDelaysImmediately = false,
            DelayStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var reservation = CreateReservation(runtime.Now.AddMinutes(20));
        var cancelCalls = 0;
        var coordinator = CreateCoordinator(
            new FakeTraceIntApiClient
            {
                OnGetReservationInfoAsync = (_, _) => Task.FromResult<ReservationInfo?>(reservation),
                OnCancelReservationAsync = (_, _, _) =>
                {
                    cancelCalls++;
                    return Task.FromResult(true);
                }
            },
            runtime,
            eventPublisher);

        await coordinator.StartAsync(new CheckInGuardPlan(
            runtime.Now.AddMinutes(4),
            TimeSpan.FromMinutes(5),
            CheckInGuardMissedAction.NotifyOnly,
            TimeSpan.Zero,
            null,
            null,
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(1)));
        await WaitForAsync(() => eventPublisher.EventsOf<CheckInReminderCoordinatorEvent>().Count == 1);
        await runtime.DelayStarted!.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.StopAsync();

        Assert.Equal(0, cancelCalls);
        Assert.Equal(CoordinatorStatusReason.Stopped, coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task DeadlinePassed_ReleasesSeatAndReservesRandomFallback_WhenOriginalSeatIsUnavailable()
    {
        var runtime = new FakeCoordinatorRuntime
        {
            Now = DateTimeOffset.Now,
            BlockDelaysStartingAtCall = 2
        };
        runtime.EnqueueNextInt(1);
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var reserveAttempts = new List<(int LibraryId, string SeatKey)>();
        var reservationInfoCalls = 0;
        var coordinator = CreateCoordinator(
            new FakeTraceIntApiClient
            {
                OnGetReservationInfoAsync = (_, _) =>
                {
                    reservationInfoCalls++;
                    return Task.FromResult<ReservationInfo?>(reservationInfoCalls >= 3
                        ? new ReservationInfo("token-2", 9, "锁定场馆", "seat-3", "3号座", runtime.Now.AddMinutes(20))
                        : CreateReservation(runtime.Now.AddMinutes(20)));
                },
                OnCancelReservationAsync = (_, token, _) => Task.FromResult(token == "token"),
                OnReserveSeatAsync = (_, libraryId, seatKey, _) =>
                {
                    reserveAttempts.Add((libraryId, seatKey));
                    return Task.FromResult(seatKey == "seat-3");
                },
                OnGetLibraryLayoutAsync = (_, libraryId, _) => Task.FromResult(new LibraryLayout(
                    libraryId,
                    "锁定场馆",
                    "一楼",
                    true,
                    3,
                    0,
                    1,
                    [
                        new SeatSnapshot("seat-2", "2号座", false, 1, 1),
                        new SeatSnapshot("seat-3", "3号座", false, 2, 1)
                    ]))
            },
            runtime,
            eventPublisher);

        await coordinator.StartAsync(new CheckInGuardPlan(
            runtime.Now,
            TimeSpan.FromMinutes(5),
            CheckInGuardMissedAction.CancelAndReserveSameSeatOrRandomInLibrary,
            TimeSpan.Zero,
            9,
            "锁定场馆",
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(1)));
        await WaitForAsync(() => coordinator.GetStatus().State == CoordinatorTaskState.Running &&
                                eventPublisher.EventsOf<CheckInAutoRescueSucceededCoordinatorEvent>().Count == 1);

        Assert.Equal(CoordinatorStatusReason.Running, coordinator.GetStatus().Reason);
        Assert.Equal([(1, "seat-1"), (9, "seat-3")], reserveAttempts);
        Assert.Single(eventPublisher.EventsOf<CheckInMissedCoordinatorEvent>());
        Assert.Contains(eventPublisher.EventsOf<CheckInAutoRescueSucceededCoordinatorEvent>(), x => x.SeatName == "3号座");

        await coordinator.StopAsync();
    }

    [Fact]
    public async Task DeadlinePassed_RepeatsGuardAfterSuccessfulAutoRescue()
    {
        var runtime = new FakeCoordinatorRuntime
        {
            Now = DateTimeOffset.Now,
            BlockDelaysStartingAtCall = 2
        };
        runtime.EnqueueNextInt(0);
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var reservationInfoCalls = 0;
        var coordinator = CreateCoordinator(
            new FakeTraceIntApiClient
            {
                OnGetReservationInfoAsync = (_, _) =>
                {
                    reservationInfoCalls++;
                    return Task.FromResult<ReservationInfo?>(reservationInfoCalls >= 3
                        ? new ReservationInfo("token-2", 9, "锁定场馆", "seat-9", "9号座", runtime.Now.AddMinutes(20))
                        : CreateReservation(runtime.Now.AddMinutes(20)));
                },
                OnCancelReservationAsync = (_, token, _) => Task.FromResult(token == "token"),
                OnReserveSeatAsync = (_, _, _, _) => Task.FromResult(true),
                OnGetLibraryLayoutAsync = (_, libraryId, _) => Task.FromResult(new LibraryLayout(
                    libraryId,
                    "锁定场馆",
                    "一楼",
                    true,
                    1,
                    0,
                    1,
                    [
                        new SeatSnapshot("seat-9", "9号座", false, 1, 1)
                    ]))
            },
            runtime,
            eventPublisher);

        await coordinator.StartAsync(new CheckInGuardPlan(
            runtime.Now,
            TimeSpan.FromMinutes(5),
            CheckInGuardMissedAction.CancelAndReserveSameSeatOrRandomInLibrary,
            TimeSpan.Zero,
            9,
            "锁定场馆",
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(1)));
        await WaitForAsync(() => coordinator.GetStatus().State == CoordinatorTaskState.Running &&
                                eventPublisher.EventsOf<CheckInAutoRescueSucceededCoordinatorEvent>().Count == 1);

        Assert.Single(eventPublisher.EventsOf<CheckInMissedCoordinatorEvent>());
        Assert.Single(eventPublisher.EventsOf<CheckInAutoRescueSucceededCoordinatorEvent>());
        Assert.True(runtime.DelayRequests.Count >= 2);
        Assert.NotEqual(CoordinatorStatusReason.CheckInGuardCompleted, coordinator.GetStatus().Reason);

        await coordinator.StopAsync();
    }

    [Fact]
    public async Task StartsMissedActionOneMinuteBeforeCheckInDeadline()
    {
        var runtime = new FakeCoordinatorRuntime
        {
            Now = DateTimeOffset.Now,
            BlockDelaysStartingAtCall = 2
        };
        var eventPublisher = new FakeCoordinatorEventPublisher();
        var cancelCalls = 0;
        var coordinator = CreateCoordinator(
            new FakeTraceIntApiClient
            {
                OnGetReservationInfoAsync = (_, _) => Task.FromResult<ReservationInfo?>(CreateReservation(runtime.Now.AddMinutes(20))),
                OnCancelReservationAsync = (_, _, _) =>
                {
                    cancelCalls++;
                    return Task.FromResult(true);
                },
                OnReserveSeatAsync = (_, _, _, _) => Task.FromResult(true)
            },
            runtime,
            eventPublisher);

        await coordinator.StartAsync(new CheckInGuardPlan(
            runtime.Now.AddMinutes(1),
            TimeSpan.FromMinutes(5),
            CheckInGuardMissedAction.CancelAndReserveSameSeat,
            TimeSpan.Zero,
            null,
            null,
            TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(1)));
        await WaitForAsync(() => cancelCalls == 1);

        Assert.Single(eventPublisher.EventsOf<CheckInMissedCoordinatorEvent>());
        Assert.Single(eventPublisher.EventsOf<CheckInAutoRescueSucceededCoordinatorEvent>());

        await coordinator.StopAsync();
    }

    private static CheckInGuardCoordinator CreateCoordinator(
        FakeTraceIntApiClient apiClient,
        ICoordinatorRuntime runtime,
        FakeCoordinatorEventPublisher? eventPublisher = null)
    {
        var activityLogService = new ActivityLogService();
        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var stateMachine = new CheckInGuardWorkflowRunner(
            apiClient,
            eventPublisher ?? new FakeCoordinatorEventPublisher(),
            activityLogService,
            runtimeState,
            runtimeState,
            runtime);

        return new CheckInGuardCoordinator(stateMachine, runtime);
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
}

using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;

namespace IGoLibrary.Ex.Tests;

public sealed class MobileControlTaskStartServiceTests
{
    [Fact]
    public async Task StartGrabAsync_ReplaysFrozenConfigurationImmediately()
    {
        var recordId = Guid.NewGuid().ToString("N");
        var history = new FakeHistoryService
        {
            Grab = new GrabTaskLaunchRecord(
                recordId,
                DateTimeOffset.UtcNow,
                7,
                "电子阅览室A",
                [new SeatReference("seat-27", "27"), new SeatReference("seat-38", "38")],
                GrabPollingMode.Randomized,
                GrabReservationStrategy.ReserveDirectly)
        };
        var launcher = new FakeTaskLaunchService();
        var service = CreateService(history, launcher);

        var result = await service.StartTaskAsync("grab", recordId);

        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(launcher.LastGrabPlan);
        Assert.Null(launcher.LastGrabPlan.ScheduledStart);
        Assert.Equal(GrabReservationStrategy.ReserveDirectly, launcher.LastGrabPlan.ReservationStrategy);
        Assert.Equal(["seat-27", "seat-38"], launcher.LastGrabPlan.Seats.Select(static seat => seat.SeatKey));
        Assert.Equal(TaskLaunchSource.MobileControl, launcher.LastSource);
    }

    [Fact]
    public async Task StartGlobalLeakAsync_PreservesPriorityOrderAndInterval()
    {
        var recordId = Guid.NewGuid().ToString("N");
        var history = new FakeHistoryService
        {
            GlobalLeak = new GlobalLeakTaskLaunchRecord(
                recordId,
                DateTimeOffset.UtcNow,
                [
                    new GlobalLeakLibraryTarget(9, "高优先级", "3层"),
                    new GlobalLeakLibraryTarget(3, "低优先级", "1层")
                ],
                TimeSpan.FromSeconds(17))
        };
        var launcher = new FakeTaskLaunchService();
        var service = CreateService(history, launcher);

        var result = await service.StartTaskAsync("globalLeak", recordId);

        Assert.True(result.Success);
        Assert.Equal([9, 3], launcher.LastGlobalLeakPlan!.Libraries.Select(static library => library.LibraryId));
        Assert.Equal(TimeSpan.FromSeconds(17), launcher.LastGlobalLeakPlan.ScanInterval);
    }

    [Fact]
    public async Task StartOccupyAsync_UsesCurrentDesktopPlanSnapshot()
    {
        var launcher = new FakeTaskLaunchService();
        var expectedPlan = new OccupySeatPlan(
            TimeSpan.FromSeconds(88),
            OccupyCheckIntervalMode.RandomTenToTwentySeconds);
        var service = CreateService(
            new FakeHistoryService(),
            launcher,
            reservation: CreateReservation(),
            occupyPlan: expectedPlan);

        var result = await service.StartTaskAsync("occupy", null);

        Assert.True(result.Success);
        Assert.Equal(expectedPlan, launcher.LastOccupyPlan);
        Assert.Equal(TaskLaunchSource.MobileControl, launcher.LastSource);
    }

    [Fact]
    public async Task StartTaskAsync_RejectsMissingSessionMissingRecordAndActiveTask()
    {
        var recordId = Guid.NewGuid().ToString("N");
        var noSession = CreateService(new FakeHistoryService(), new FakeTaskLaunchService(), hasSession: false);
        var missingRecord = CreateService(new FakeHistoryService(), new FakeTaskLaunchService());
        var activeCoordinator = new FakeGrabSeatCoordinator();
        activeCoordinator.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "抢座",
            "运行中",
            DateTimeOffset.Now,
            DateTimeOffset.Now));
        var active = CreateService(
            new FakeHistoryService
            {
                Grab = new GrabTaskLaunchRecord(
                    recordId,
                    DateTimeOffset.UtcNow,
                    1,
                    "场馆",
                    [new SeatReference("seat", "1")],
                    GrabPollingMode.Relaxed,
                    GrabReservationStrategy.QueryThenReserve)
            },
            new FakeTaskLaunchService(),
            grabCoordinator: activeCoordinator);

        Assert.Equal(StatusCodes.Status409Conflict, (await noSession.StartTaskAsync("grab", recordId)).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, (await missingRecord.StartTaskAsync("grab", recordId)).StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, (await active.StartTaskAsync("grab", recordId)).StatusCode);
    }

    [Fact]
    public async Task StartTaskAsync_RejectsInvalidKindRecordAndOccupyWithoutReservation()
    {
        var service = CreateService(new FakeHistoryService(), new FakeTaskLaunchService());

        Assert.Equal(StatusCodes.Status400BadRequest, (await service.StartTaskAsync("tomorrow", null)).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, (await service.StartTaskAsync("grab", "not-a-record-id")).StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, (await service.StartTaskAsync("occupy", null)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentGrabStarts_MapsCoordinatorRejectionToConflict()
    {
        var recordId = Guid.NewGuid().ToString("N");
        var history = new FakeHistoryService
        {
            Grab = new GrabTaskLaunchRecord(
                recordId,
                DateTimeOffset.UtcNow,
                1,
                "场馆",
                [new SeatReference("seat", "1")],
                GrabPollingMode.Relaxed,
                GrabReservationStrategy.QueryThenReserve)
        };
        var service = CreateService(history, new AtomicallyRejectingTaskLaunchService());

        var results = await Task.WhenAll(
            service.StartTaskAsync("grab", recordId),
            service.StartTaskAsync("grab", recordId));

        Assert.Equal(
            [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
            results.Select(static result => result.StatusCode).Order().ToArray());
    }

    [Fact]
    public async Task StartTaskAsync_DoesNotMapUnexpectedInvalidOperationToConflict()
    {
        var recordId = Guid.NewGuid().ToString("N");
        var history = new FakeHistoryService
        {
            Grab = new GrabTaskLaunchRecord(
                recordId,
                DateTimeOffset.UtcNow,
                1,
                "场馆",
                [new SeatReference("seat", "1")],
                GrabPollingMode.Relaxed,
                GrabReservationStrategy.QueryThenReserve)
        };
        var expected = new InvalidOperationException("unexpected internal failure");
        var service = CreateService(history, new FailingTaskLaunchService(expected));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StartTaskAsync("grab", recordId));

        Assert.Same(expected, actual);
    }

    private static MobileControlTaskStartService CreateService(
        FakeHistoryService history,
        ITaskLaunchService launcher,
        bool hasSession = true,
        ReservationInfo? reservation = null,
        OccupySeatPlan? occupyPlan = null,
        FakeGrabSeatCoordinator? grabCoordinator = null)
    {
        var state = new AppRuntimeState
        {
            Session = hasSession
                ? new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
                : null,
            CurrentReservation = reservation
        };
        return new MobileControlTaskStartService(
            history,
            launcher,
            grabCoordinator ?? new FakeGrabSeatCoordinator(),
            new FakeGlobalLeakCoordinator(),
            new FakeOccupySeatCoordinator(),
            state,
            state,
            new ShellWorkflowState { CurrentReservation = reservation },
            new FakeOccupyPlanProvider(occupyPlan ?? new OccupySeatPlan(
                TimeSpan.FromSeconds(60),
                OccupyCheckIntervalMode.FixedTenSeconds)),
            new ActivityLogService(),
            new FakeTimeProvider(DateTimeOffset.UtcNow));
    }

    private static ReservationInfo CreateReservation()
    {
        return new ReservationInfo(
            "reservation-token",
            1,
            "场馆",
            "seat-1",
            "1",
            DateTimeOffset.Now.AddMinutes(30));
    }

    private sealed class FakeOccupyPlanProvider(OccupySeatPlan plan) : IMobileControlOccupyPlanProvider
    {
        public Task<OccupySeatPlan> CreatePlanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(plan);
    }

    private sealed class FakeHistoryService : ITaskLaunchHistoryService
    {
        public GrabTaskLaunchRecord? Grab { get; init; }

        public GlobalLeakTaskLaunchRecord? GlobalLeak { get; init; }

        public Task<IReadOnlyList<GrabTaskLaunchRecord>> GetRecentGrabAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GrabTaskLaunchRecord>>(Grab is null ? [] : [Grab]);

        public Task<IReadOnlyList<GlobalLeakTaskLaunchRecord>> GetRecentGlobalLeakAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GlobalLeakTaskLaunchRecord>>(GlobalLeak is null ? [] : [GlobalLeak]);

        public Task<GrabTaskLaunchRecord?> GetGrabAsync(string recordId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Grab?.RecordId == recordId ? Grab : null);

        public Task<GlobalLeakTaskLaunchRecord?> GetGlobalLeakAsync(string recordId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GlobalLeak?.RecordId == recordId ? GlobalLeak : null);

        public Task<TaskLaunchHistorySaveResult> RecordGrabAsync(GrabSeatPlan plan, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskLaunchHistorySaveResult> RecordGlobalLeakAsync(GlobalLeakPlan plan, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class AtomicallyRejectingTaskLaunchService : ITaskLaunchService
    {
        private int _grabStarts;

        public Task StartGrabAsync(
            GrabSeatPlan plan,
            TaskLaunchSource source,
            CancellationToken cancellationToken = default)
        {
            return Interlocked.Increment(ref _grabStarts) == 1
                ? Task.CompletedTask
                : Task.FromException(new TaskLaunchConflictException("抢座任务已在运行"));
        }

        public Task StartGlobalLeakAsync(
            GlobalLeakPlan plan,
            TaskLaunchSource source,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task StartOccupyAsync(
            OccupySeatPlan plan,
            TaskLaunchSource source,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FailingTaskLaunchService(Exception exception) : ITaskLaunchService
    {
        public Task StartGrabAsync(
            GrabSeatPlan plan,
            TaskLaunchSource source,
            CancellationToken cancellationToken = default) => Task.FromException(exception);

        public Task StartGlobalLeakAsync(
            GlobalLeakPlan plan,
            TaskLaunchSource source,
            CancellationToken cancellationToken = default) => Task.FromException(exception);

        public Task StartOccupyAsync(
            OccupySeatPlan plan,
            TaskLaunchSource source,
            CancellationToken cancellationToken = default) => Task.FromException(exception);
    }
}

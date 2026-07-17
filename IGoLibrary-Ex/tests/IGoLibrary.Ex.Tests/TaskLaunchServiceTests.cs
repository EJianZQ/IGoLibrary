using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class TaskLaunchServiceTests
{
    [Fact]
    public async Task DesktopStart_RecordsAcceptedGrabWithFrozenStrategy()
    {
        var grab = new FakeGrabSeatCoordinator();
        var history = new FakeTaskLaunchHistoryService();
        var service = CreateService(grab, history);
        var plan = CreateGrabPlan(GrabReservationStrategy.ReserveDirectly, new TimeOnly(20, 0));

        await service.StartGrabAsync(plan, TaskLaunchSource.Desktop);

        Assert.Same(plan, grab.LastPlan);
        Assert.Same(plan, Assert.Single(history.RecordedGrabPlans));
        Assert.Equal(GrabReservationStrategy.ReserveDirectly, grab.LastPlan!.ReservationStrategy);
    }

    [Fact]
    public async Task MobileStart_DoesNotCreateOrPromoteHistory()
    {
        var history = new FakeTaskLaunchHistoryService();
        var service = CreateService(new FakeGrabSeatCoordinator(), history);

        await service.StartGrabAsync(CreateGrabPlan(GrabReservationStrategy.QueryThenReserve, null), TaskLaunchSource.MobileControl);

        Assert.Empty(history.RecordedGrabPlans);
    }

    [Fact]
    public async Task HistoryFailure_DoesNotTurnAcceptedTaskIntoStartFailure()
    {
        var grab = new FakeGrabSeatCoordinator();
        var history = new FakeTaskLaunchHistoryService
        {
            RecordGrabException = new IOException("database unavailable")
        };
        var logs = new ActivityLogService();
        var structuredLogger = new CapturingLogger<TaskLaunchService>();
        var service = CreateService(grab, history, logs, structuredLogger);

        await service.StartGrabAsync(CreateGrabPlan(GrabReservationStrategy.QueryThenReserve, null), TaskLaunchSource.Desktop);

        Assert.NotNull(grab.LastPlan);
        Assert.Contains(logs.Entries, entry =>
            entry.Category == "TaskLaunchHistory" && entry.Kind == LogEntryKind.Warning);
        Assert.Same(history.RecordGrabException, Assert.Single(structuredLogger.Exceptions));
    }

    [Fact]
    public async Task RejectedStart_DoesNotWriteHistory()
    {
        var history = new FakeTaskLaunchHistoryService();
        var service = CreateService(new RejectingGrabCoordinator(), history);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartGrabAsync(
            CreateGrabPlan(GrabReservationStrategy.QueryThenReserve, null),
            TaskLaunchSource.Desktop));

        Assert.Empty(history.RecordedGrabPlans);
    }

    private static TaskLaunchService CreateService(
        IGrabSeatCoordinator grab,
        FakeTaskLaunchHistoryService history,
        ActivityLogService? logs = null,
        ILogger<TaskLaunchService>? logger = null)
    {
        return new TaskLaunchService(
            grab,
            new FakeGlobalLeakCoordinator(),
            new FakeOccupySeatCoordinator(),
            history,
            logs ?? new ActivityLogService(),
            logger ?? NullLogger<TaskLaunchService>.Instance);
    }

    private static GrabSeatPlan CreateGrabPlan(
        GrabReservationStrategy strategy,
        TimeOnly? scheduledStart)
    {
        return new GrabSeatPlan(
            1,
            "电子阅览室A",
            [new SeatReference("seat-27", "27")],
            GrabPollingMode.Randomized,
            GrabPollingStrategyFactory.FromMode(GrabPollingMode.Randomized),
            scheduledStart,
            strategy);
    }

    private sealed class FakeTaskLaunchHistoryService : ITaskLaunchHistoryService
    {
        public List<GrabSeatPlan> RecordedGrabPlans { get; } = [];

        public Exception? RecordGrabException { get; init; }

        public Task<IReadOnlyList<GrabTaskLaunchRecord>> GetRecentGrabAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GrabTaskLaunchRecord>>([]);

        public Task<IReadOnlyList<GlobalLeakTaskLaunchRecord>> GetRecentGlobalLeakAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GlobalLeakTaskLaunchRecord>>([]);

        public Task<GrabTaskLaunchRecord?> GetGrabAsync(string recordId, CancellationToken cancellationToken = default) =>
            Task.FromResult<GrabTaskLaunchRecord?>(null);

        public Task<GlobalLeakTaskLaunchRecord?> GetGlobalLeakAsync(string recordId, CancellationToken cancellationToken = default) =>
            Task.FromResult<GlobalLeakTaskLaunchRecord?>(null);

        public Task<TaskLaunchHistorySaveResult> RecordGrabAsync(GrabSeatPlan plan, CancellationToken cancellationToken = default)
        {
            RecordedGrabPlans.Add(plan);
            return RecordGrabException is null
                ? Task.FromResult(new TaskLaunchHistorySaveResult(Guid.NewGuid().ToString("N"), false, 0))
                : Task.FromException<TaskLaunchHistorySaveResult>(RecordGrabException);
        }

        public Task<TaskLaunchHistorySaveResult> RecordGlobalLeakAsync(GlobalLeakPlan plan, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TaskLaunchHistorySaveResult(Guid.NewGuid().ToString("N"), false, 0));
    }

    private sealed class RejectingGrabCoordinator : IGrabSeatCoordinator
    {
        public event EventHandler<CoordinatorStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(GrabSeatPlan plan, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("抢座任务已在运行"));

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public CoordinatorStatus GetStatus() => CoordinatorStatus.Idle("抢座");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<Exception> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                Exceptions.Add(exception);
            }
        }
    }
}

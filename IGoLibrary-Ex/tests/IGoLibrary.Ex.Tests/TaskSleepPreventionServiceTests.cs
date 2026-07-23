using System.Collections.Concurrent;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Platform.Power;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

public sealed class TaskSleepPreventionServiceTests
{
    [Theory]
    [InlineData(CoordinatorTaskState.Idle, false)]
    [InlineData(CoordinatorTaskState.Starting, true)]
    [InlineData(CoordinatorTaskState.Running, true)]
    [InlineData(CoordinatorTaskState.Stopping, true)]
    [InlineData(CoordinatorTaskState.Completed, false)]
    [InlineData(CoordinatorTaskState.Failed, false)]
    public void CoordinatorStatus_IsActive_UsesSharedTaskLifecycleDefinition(
        CoordinatorTaskState state,
        bool expected)
    {
        Assert.Equal(expected, CreateStatus(state, "测试任务").IsActive);
    }

    [Theory]
    [InlineData("grab")]
    [InlineData("global-leak")]
    [InlineData("occupy")]
    [InlineData("tomorrow")]
    public async Task EachSupportedTask_AcquiresAndReleasesSleepPrevention(string task)
    {
        await using var context = CreateContext(enabled: true);
        await context.Service.StartAsync(CancellationToken.None);

        await context.EmitAndWaitForReconciliationAsync(task, CoordinatorTaskState.Starting);
        Assert.True(context.Inhibitor.IsActive);
        await context.EmitAndWaitForReconciliationAsync(task, CoordinatorTaskState.Stopping);
        Assert.Equal(1, context.Inhibitor.ActivateCalls);
        Assert.Equal(0, context.Inhibitor.DeactivateCalls);

        await context.EmitAndWaitForReconciliationAsync(task, CoordinatorTaskState.Completed);
        Assert.False(context.Inhibitor.IsActive);

        Assert.Equal(TaskSleepPreventionService.PowerRequestReason, context.Inhibitor.LastReason);
        Assert.Equal(1, context.Inhibitor.ActivateCalls);
        Assert.Equal(1, context.Inhibitor.DeactivateCalls);
    }

    [Fact]
    public async Task StartAsync_ReconcilesTaskThatWasAlreadyActive()
    {
        await using var context = CreateContext(enabled: true);
        context.Grab.EmitStatus(CreateStatus(CoordinatorTaskState.Running, "抢座"));

        await context.Service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => context.Inhibitor.IsActive);

        Assert.Equal(1, context.Inhibitor.ActivateCalls);
        Assert.Contains(
            context.ActivityLog.Entries,
            entry => entry.Category == "Power" && entry.Message.Contains("抢座", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OverlappingTasks_HoldOneRequestUntilLastTaskEnds()
    {
        await using var context = CreateContext(enabled: true);
        await context.Service.StartAsync(CancellationToken.None);

        await context.EmitAndWaitForReconciliationAsync("grab", CoordinatorTaskState.Starting);
        Assert.True(context.Inhibitor.IsActive);
        context.Emit("global-leak", CoordinatorTaskState.Running);
        context.Emit("grab", CoordinatorTaskState.Completed);
        await context.WaitForRequestedReconciliationAsync();

        Assert.True(context.Inhibitor.IsActive);
        Assert.Equal(1, context.Inhibitor.ActivateCalls);
        Assert.Equal(0, context.Inhibitor.DeactivateCalls);

        await context.EmitAndWaitForReconciliationAsync("global-leak", CoordinatorTaskState.Failed);
        Assert.False(context.Inhibitor.IsActive);

        Assert.Equal(1, context.Inhibitor.ActivateCalls);
        Assert.Equal(1, context.Inhibitor.DeactivateCalls);
    }

    [Fact]
    public async Task SetEnabled_DuringActiveTask_AcquiresAndReleasesImmediately()
    {
        await using var context = CreateContext(enabled: false);
        await context.Service.StartAsync(CancellationToken.None);
        await context.EmitAndWaitForReconciliationAsync("tomorrow", CoordinatorTaskState.Running);
        Assert.False(context.Inhibitor.IsActive);

        await context.SetEnabledAndWaitForReconciliationAsync(true);
        Assert.True(context.Inhibitor.IsActive);
        await context.SetEnabledAndWaitForReconciliationAsync(false);
        Assert.False(context.Inhibitor.IsActive);

        Assert.Equal(1, context.Inhibitor.ActivateCalls);
        Assert.Equal(1, context.Inhibitor.DeactivateCalls);
        Assert.Contains(
            context.ActivityLog.Entries,
            entry => entry.Message == "已开启任务进行时阻止系统自动休眠");
        Assert.Contains(
            context.ActivityLog.Entries,
            entry => entry.Message == "已关闭任务进行时阻止系统自动休眠");
    }

    [Fact]
    public async Task NativeActivationFailure_RetriesWithBackoffAndLogsRecovery()
    {
        var timeProvider = new FakeTimeProvider();
        await using var context = CreateContext(enabled: true, timeProvider: timeProvider);
        var firstFailure = CreateNativeFailure("PowerSetRequest", 5);
        context.Inhibitor.ActivateExceptions.Enqueue(firstFailure);
        context.Inhibitor.ActivateExceptions.Enqueue(CreateNativeFailure("PowerSetRequest", 5));
        var retryDelays = new ConcurrentQueue<TimeSpan>();
        context.Service.RetryDelayScheduled += retryDelays.Enqueue;
        await context.Service.StartAsync(CancellationToken.None);

        context.Emit("occupy", CoordinatorTaskState.Starting);
        await WaitForAsync(() => retryDelays.Count == 1);
        Assert.Equal(TimeSpan.FromSeconds(5), retryDelays.ElementAt(0));
        var firstError = Assert.Single(
            context.Logger.Entries,
            entry => entry.Level == LogLevel.Error &&
                     entry.Message.Contains("任务休眠阻止的原生操作失败", StringComparison.Ordinal));
        Assert.Same(firstFailure, firstError.Exception);
        Assert.Equal("Test", firstError.Properties["Platform"]);
        Assert.Equal("申请阻止系统自动休眠", firstError.Properties["Operation"]);
        Assert.Equal("PowerSetRequest", firstError.Properties["NativeOperation"]);
        Assert.Equal(5, firstError.Properties["NativeErrorCode"]);
        Assert.Equal(1, firstError.Properties["RetryAttempt"]);
        Assert.Equal(5d, firstError.Properties["RetryDelaySeconds"]);

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await WaitForAsync(() => retryDelays.Count == 2);
        Assert.Equal(TimeSpan.FromSeconds(10), retryDelays.ElementAt(1));

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await WaitForAsync(() => context.Inhibitor.IsActive);

        Assert.Equal(3, context.Inhibitor.ActivateCalls);
        Assert.Contains(
            context.ActivityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning && entry.Message.Contains("5 秒后重试", StringComparison.Ordinal));
        Assert.Contains(
            context.ActivityLog.Entries,
            entry => entry.Kind == LogEntryKind.Success && entry.Message.Contains("恢复", StringComparison.Ordinal));
        Assert.Contains(
            context.Logger.Entries,
            entry => entry.Level == LogLevel.Information &&
                     entry.Message.Contains("已从先前的原生电源管理故障中恢复", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NewSettingSignal_InterruptsRetryDelay()
    {
        var timeProvider = new FakeTimeProvider();
        var initialTime = timeProvider.GetUtcNow();
        await using var context = CreateContext(enabled: true, timeProvider: timeProvider);
        context.Inhibitor.ActivateExceptions.Enqueue(CreateNativeFailure("PowerSetRequest", 5));
        var retryScheduled = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Service.RetryDelayScheduled += _ => retryScheduled.TrySetResult(null);
        await context.Service.StartAsync(CancellationToken.None);
        context.Emit("grab", CoordinatorTaskState.Starting);
        await retryScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        context.Service.SetEnabled(false);
        context.Service.SetEnabled(true);
        await WaitForAsync(() => context.Inhibitor.IsActive);

        Assert.Equal(2, context.Inhibitor.ActivateCalls);
        Assert.Equal(initialTime, timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task NativeReleaseFailure_RetriesUntilRequestIsReleased()
    {
        var timeProvider = new FakeTimeProvider();
        await using var context = CreateContext(enabled: true, timeProvider: timeProvider);
        context.Inhibitor.DeactivateExceptions.Enqueue(CreateNativeFailure("PowerClearRequest", 31));
        var retryScheduled = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Service.RetryDelayScheduled += delay => retryScheduled.TrySetResult(delay);
        await context.Service.StartAsync(CancellationToken.None);
        context.Emit("grab", CoordinatorTaskState.Running);
        await WaitForAsync(() => context.Inhibitor.IsActive);

        context.Emit("grab", CoordinatorTaskState.Completed);
        Assert.Equal(TimeSpan.FromSeconds(5), await retryScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(context.Inhibitor.IsActive);

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await WaitForAsync(() => !context.Inhibitor.IsActive);

        Assert.Equal(2, context.Inhibitor.DeactivateCalls);
        Assert.Contains(
            context.ActivityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning && entry.Message.Contains("释放系统自动休眠阻止失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SettingLoadFailure_UsesEnabledDefaultAndDoesNotBlockStartup()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        settingsService.LoadExceptions.Enqueue(new InvalidOperationException("settings unavailable"));
        await using var context = CreateContext(enabled: true, settingsService: settingsService);
        context.Grab.EmitStatus(CreateStatus(CoordinatorTaskState.Running, "抢座"));

        await context.Service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => context.Inhibitor.IsActive);

        Assert.Contains(
            context.ActivityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning && entry.Message.Contains("默认开启状态", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnsupportedPlatform_LogsOnceAndNeverCallsNativeOperations()
    {
        var inhibitor = new RecordingSystemIdleSleepInhibitor(isSupported: false);
        await using var context = CreateContext(enabled: true, inhibitor: inhibitor);
        await context.Service.StartAsync(CancellationToken.None);
        context.Emit("grab", CoordinatorTaskState.Running);
        context.Emit("grab", CoordinatorTaskState.Completed);
        context.Service.SetEnabled(false);
        context.Service.SetEnabled(true);
        await context.WaitForRequestedReconciliationAsync();

        Assert.Equal(0, inhibitor.ActivateCalls);
        Assert.Equal(0, inhibitor.DeactivateCalls);
        Assert.Single(
            context.ActivityLog.Entries,
            entry => entry.Category == "Power" && entry.Message.Contains("不支持任务防休眠", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepeatedRunningUpdates_DoNotDuplicateNativeCallsOrActivityLogs()
    {
        await using var context = CreateContext(enabled: true);
        await context.Service.StartAsync(CancellationToken.None);
        await context.EmitAndWaitForReconciliationAsync("grab", CoordinatorTaskState.Starting);
        Assert.True(context.Inhibitor.IsActive);

        context.Emit("grab", CoordinatorTaskState.Running);
        context.Emit("grab", CoordinatorTaskState.Running);
        context.Emit("grab", CoordinatorTaskState.Running);
        await context.WaitForRequestedReconciliationAsync();

        Assert.Equal(1, context.Inhibitor.ActivateCalls);
        Assert.Single(
            context.ActivityLog.Entries,
            entry => entry.Category == "Power" && entry.Message.StartsWith("已阻止系统自动休眠", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopAsync_ReleasesRequestDisposesInhibitorAndUnsubscribes()
    {
        var context = CreateContext(enabled: true);
        await context.Service.StartAsync(CancellationToken.None);
        await context.EmitAndWaitForReconciliationAsync("grab", CoordinatorTaskState.Running);
        Assert.True(context.Inhibitor.IsActive);

        await context.Service.StopAsync(CancellationToken.None);

        Assert.False(context.Inhibitor.IsActive);
        Assert.Equal(1, context.Inhibitor.DeactivateCalls);
        Assert.Equal(1, context.Inhibitor.DisposeCalls);
        Assert.Equal(0, context.Grab.StatusChangedSubscriberCount);
        Assert.Equal(0, context.GlobalLeak.StatusChangedSubscriberCount);
        Assert.Equal(0, context.Occupy.StatusChangedSubscriberCount);
        Assert.Equal(0, context.Tomorrow.StatusChangedSubscriberCount);
        Assert.Equal(0, context.Inhibitor.CleanupFailedSubscriberCount);
        await context.DisposeAsync();
    }

    [Fact]
    public async Task NativeCleanupFailure_LogsStructuredErrorAndPowerActivity()
    {
        await using var context = CreateContext(enabled: true);
        await context.Service.StartAsync(CancellationToken.None);
        var failure = CreateNativeFailure("CloseHandle", 6);

        context.Inhibitor.EmitCleanupFailure(failure);

        var log = await WaitForValueAsync(() => context.Logger.Entries.FirstOrDefault(
            entry => entry.Level == LogLevel.Error &&
                     entry.Message.Contains("清理任务休眠阻止的原生资源失败", StringComparison.Ordinal)));
        Assert.Same(failure, log.Exception);
        Assert.Equal("Windows", log.Properties["Platform"]);
        Assert.Equal("CloseHandle", log.Properties["NativeOperation"]);
        Assert.Equal(6, log.Properties["NativeErrorCode"]);
        Assert.Contains(
            context.ActivityLog.Entries,
            entry => entry.Category == "Power" &&
                     entry.Kind == LogEntryKind.Error &&
                     entry.Message.Contains("CloseHandle", StringComparison.Ordinal) &&
                     entry.Message.Contains("错误码 6", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentCoordinatorEvents_AreSerializedIntoOneHoldAndOneRelease()
    {
        await using var context = CreateContext(enabled: true);
        await context.Service.StartAsync(CancellationToken.None);

        await Task.WhenAll(
            Task.Run(() => context.Emit("grab", CoordinatorTaskState.Running)),
            Task.Run(() => context.Emit("global-leak", CoordinatorTaskState.Running)),
            Task.Run(() => context.Emit("occupy", CoordinatorTaskState.Running)),
            Task.Run(() => context.Emit("tomorrow", CoordinatorTaskState.Running)));
        await context.WaitForRequestedReconciliationAsync();
        Assert.True(context.Inhibitor.IsActive);

        Assert.Equal(1, context.Inhibitor.ActivateCalls);
        await Task.WhenAll(
            Task.Run(() => context.Emit("grab", CoordinatorTaskState.Completed)),
            Task.Run(() => context.Emit("global-leak", CoordinatorTaskState.Completed)),
            Task.Run(() => context.Emit("occupy", CoordinatorTaskState.Completed)),
            Task.Run(() => context.Emit("tomorrow", CoordinatorTaskState.Completed)));
        await context.WaitForRequestedReconciliationAsync();
        Assert.False(context.Inhibitor.IsActive);

        Assert.Equal(1, context.Inhibitor.ActivateCalls);
        Assert.Equal(1, context.Inhibitor.DeactivateCalls);
    }

    [Fact]
    public async Task ConcurrentEventsDuringStop_DoNotReacquireOrEscapeCancellation()
    {
        var context = CreateContext(enabled: true);
        await context.Service.StartAsync(CancellationToken.None);
        await context.EmitAndWaitForReconciliationAsync("grab", CoordinatorTaskState.Running);

        var stopTask = context.Service.StopAsync(CancellationToken.None);
        await Task.WhenAll(
            Task.Run(() => context.Emit("global-leak", CoordinatorTaskState.Running)),
            Task.Run(() => context.Emit("occupy", CoordinatorTaskState.Running)),
            Task.Run(() => context.Emit("tomorrow", CoordinatorTaskState.Running)),
            stopTask);

        Assert.False(context.Inhibitor.IsActive);
        Assert.Equal(1, context.Inhibitor.ActivateCalls);
        Assert.Equal(1, context.Inhibitor.DeactivateCalls);
        Assert.Equal(1, context.Inhibitor.DisposeCalls);
        await context.DisposeAsync();
    }

    private static TestContext CreateContext(
        bool enabled,
        TimeProvider? timeProvider = null,
        RecordingSystemIdleSleepInhibitor? inhibitor = null,
        FakeSettingsService? settingsService = null)
    {
        var settings = AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                PreventSystemSleepWhileTasksActive = enabled
            }
        };
        var grab = new FakeGrabSeatCoordinator();
        var globalLeak = new FakeGlobalLeakCoordinator();
        var occupy = new FakeOccupySeatCoordinator();
        var tomorrow = new FakeTomorrowReservationCoordinator();
        inhibitor ??= new RecordingSystemIdleSleepInhibitor();
        var activityLog = new ActivityLogService();
        var logger = new CapturingLogger<TaskSleepPreventionService>();
        var service = new TaskSleepPreventionService(
            settingsService ?? new FakeSettingsService(settings),
            grab,
            globalLeak,
            occupy,
            tomorrow,
            inhibitor,
            activityLog,
            timeProvider ?? TimeProvider.System,
            logger);
        return new TestContext(service, grab, globalLeak, occupy, tomorrow, inhibitor, activityLog, logger);
    }

    private static CoordinatorStatus CreateStatus(CoordinatorTaskState state, string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new CoordinatorStatus(
            state,
            title,
            state.ToString(),
            state == CoordinatorTaskState.Idle ? null : now,
            now,
            Reason: CoordinatorStatusReason.Running);
    }

    private static SystemSleepInhibitorException CreateNativeFailure(string operation, int errorCode)
        => new("Windows", operation, errorCode, $"{operation} failed: {errorCode}");

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private static async Task<T> WaitForValueAsync<T>(Func<T?> valueFactory) where T : class
    {
        T? value = null;
        await WaitForAsync(() => (value = valueFactory()) is not null);
        return value!;
    }

    private sealed record TestContext(
        TaskSleepPreventionService Service,
        FakeGrabSeatCoordinator Grab,
        FakeGlobalLeakCoordinator GlobalLeak,
        FakeOccupySeatCoordinator Occupy,
        FakeTomorrowReservationCoordinator Tomorrow,
        RecordingSystemIdleSleepInhibitor Inhibitor,
        ActivityLogService ActivityLog,
        CapturingLogger<TaskSleepPreventionService> Logger) : IAsyncDisposable
    {
        public void Emit(string task, CoordinatorTaskState state)
        {
            switch (task)
            {
                case "grab":
                    Grab.EmitStatus(CreateStatus(state, "抢座"));
                    break;
                case "global-leak":
                    GlobalLeak.EmitStatus(CreateStatus(state, "全域捡漏"));
                    break;
                case "occupy":
                    Occupy.EmitStatus(CreateStatus(state, "占座"));
                    break;
                case "tomorrow":
                    Tomorrow.EmitStatus(CreateStatus(state, "明日预约"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(task), task, null);
            }
        }

        public async Task EmitAndWaitForReconciliationAsync(string task, CoordinatorTaskState state)
        {
            Emit(task, state);
            await WaitForRequestedReconciliationAsync();
        }

        public async Task SetEnabledAndWaitForReconciliationAsync(bool enabled)
        {
            Service.SetEnabled(enabled);
            await WaitForRequestedReconciliationAsync();
        }

        public Task WaitForRequestedReconciliationAsync()
        {
            var requestedVersion = Service.RequestedReconciliationVersion;
            return WaitForAsync(() => Service.ProcessedReconciliationVersion >= requestedVersion);
        }

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(CancellationToken.None);
            Service.Dispose();
        }
    }

    private sealed class RecordingSystemIdleSleepInhibitor(bool isSupported = true) : ISystemIdleSleepInhibitor
    {
        public event EventHandler<SystemSleepInhibitorException>? CleanupFailed;

        public int CleanupFailedSubscriberCount => CleanupFailed?.GetInvocationList().Length ?? 0;

        public string PlatformName => isSupported ? "Test" : "Unsupported";

        public bool IsSupported => isSupported;

        public bool IsActive { get; private set; }

        public int ActivateCalls { get; private set; }

        public int DeactivateCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public string? LastReason { get; private set; }

        public Queue<Exception> ActivateExceptions { get; } = [];

        public Queue<Exception> DeactivateExceptions { get; } = [];

        public void Activate(string reason)
        {
            ActivateCalls++;
            LastReason = reason;
            if (ActivateExceptions.TryDequeue(out var exception))
            {
                throw exception;
            }

            IsActive = true;
        }

        public void Deactivate()
        {
            DeactivateCalls++;
            if (DeactivateExceptions.TryDequeue(out var exception))
            {
                throw exception;
            }

            IsActive = false;
        }

        public void Dispose()
        {
            DisposeCalls++;
            IsActive = false;
        }

        public void EmitCleanupFailure(SystemSleepInhibitorException exception)
            => CleanupFailed?.Invoke(this, exception);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<CapturedLogEntry> _entries = [];

        public IReadOnlyList<CapturedLogEntry> Entries => _entries.ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values
                    .Where(item => item.Key != "{OriginalFormat}")
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            _entries.Enqueue(new CapturedLogEntry(
                logLevel,
                formatter(state, exception),
                exception,
                properties));
        }
    }

    private sealed record CapturedLogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);
}

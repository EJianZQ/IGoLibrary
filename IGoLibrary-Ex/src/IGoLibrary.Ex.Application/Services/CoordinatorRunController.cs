using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class CoordinatorRunController(
    string title,
    ICoordinatorRuntime runtime,
    IAppLogWriter? logWriter = null)
{
    private readonly object _gate = new();
    private readonly object _notificationGate = new();
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private Guid? _runId;
    private long _statusVersion;
    private CoordinatorStatus _status = CoordinatorStatus.Idle(title);

    public event EventHandler<CoordinatorStatus>? StatusChanged;

    public CoordinatorStatus GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    public Task StartAsync(
        Func<CoordinatorRunContext, CancellationToken, Task> runAsync,
        CancellationToken cancellationToken = default)
    {
        CoordinatorStatus startingStatus;
        Guid runId;
        long startingStatusVersion;
        var startSignal = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_cts is not null || _runningTask is { IsCompleted: false })
            {
                WriteLog(
                    LogLevel.Warning,
                    $"任务启动请求被拒绝：任务已在运行，运行标识={FormatRunId(_runId)}。",
                    eventId: new EventId(2002, "CoordinatorStartConflict"));
                throw new TaskLaunchConflictException($"{title}任务已在运行");
            }

            runId = Guid.NewGuid();
            _runId = runId;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startingStatus = new CoordinatorStatus(
                CoordinatorTaskState.Starting,
                title,
                $"准备启动{title}任务",
                runtime.Now,
                runtime.Now,
                Reason: CoordinatorStatusReason.Starting);
            SetStatusUnsafe(startingStatus);
            startingStatusVersion = _statusVersion;

            var context = new CoordinatorRunContext(this, runId);
            _runningTask = RunCoreAsync(runAsync, context, startSignal.Task, runId, _cts.Token);
        }

        try
        {
            WriteLog(
                LogLevel.Information,
                $"任务开始启动，运行标识={FormatRunId(runId)}。",
                eventId: new EventId(2001, "CoordinatorStarting"));
            NotifyStatusChanged(startingStatus, runId, startingStatusVersion);
        }
        finally
        {
            startSignal.TrySetResult(null);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? runningTask;
        CancellationTokenSource cts;
        CoordinatorStatus stoppingStatus;
        Guid? stoppingRunId;
        long stoppingStatusVersion;
        lock (_gate)
        {
            if (_cts is null)
            {
                return;
            }

            stoppingStatus = GetStatusUnsafe() with
            {
                State = CoordinatorTaskState.Stopping,
                Message = $"正在停止{title}任务",
                LastUpdatedAt = runtime.Now,
                Reason = CoordinatorStatusReason.Stopping
            };
            SetStatusUnsafe(stoppingStatus);
            stoppingStatusVersion = _statusVersion;
            cts = _cts;
            runningTask = _runningTask;
            stoppingRunId = _runId;
        }

        try
        {
            WriteLog(
                LogLevel.Information,
                $"收到任务停止请求，运行标识={FormatRunId(stoppingRunId)}。",
                eventId: new EventId(2003, "CoordinatorStopping"));
            NotifyStatusChanged(stoppingStatus, stoppingRunId, stoppingStatusVersion);
        }
        finally
        {
            cts.Cancel();
        }

        if (runningTask is not null)
        {
            try
            {
                await runningTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RunCoreAsync(
        Func<CoordinatorRunContext, CancellationToken, Task> runAsync,
        CoordinatorRunContext context,
        Task startSignal,
        Guid runId,
        CancellationToken cancellationToken)
    {
        try
        {
            await startSignal;
            cancellationToken.ThrowIfCancellationRequested();
            await runAsync(context, cancellationToken);
            context.Complete($"{title}任务已停止", CoordinatorStatusReason.Stopped);
        }
        catch (OperationCanceledException)
        {
            context.Complete($"{title}任务已停止", CoordinatorStatusReason.Stopped);
        }
        catch (Exception ex)
        {
            WriteLog(
                LogLevel.Error,
                $"任务执行发生未处理异常，运行标识={FormatRunId(runId)}。",
                ex,
                new EventId(2005, "CoordinatorUnhandledFailure"));
            context.Fail($"{title}任务失败：{ex.Message}", CoordinatorStatusReason.TaskFailed);
        }
    }

    private CoordinatorStatus GetStatusUnsafe() => _status;

    private void SetStatusUnsafe(CoordinatorStatus status)
    {
        _status = status;
        _statusVersion++;
    }

    private void NotifyStatusChanged(
        CoordinatorStatus status,
        Guid? runId,
        long expectedStatusVersion)
    {
        lock (_notificationGate)
        {
            lock (_gate)
            {
                if (_statusVersion != expectedStatusVersion)
                {
                    return;
                }
            }

            var handlers = StatusChanged;
            if (handlers is null)
            {
                return;
            }

            foreach (EventHandler<CoordinatorStatus> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, status);
                }
                catch (Exception ex)
                {
                    WriteLog(
                        LogLevel.Error,
                        $"任务状态订阅者处理失败，运行标识={FormatRunId(runId)}，状态={status.State}。",
                        ex,
                        new EventId(2006, "CoordinatorStatusSubscriberFailed"));
                }
            }
        }
    }

    private void ClearRunUnsafe()
    {
        _cts?.Dispose();
        _cts = null;
        _runningTask = null;
    }

    internal void SetRunning(
        Guid expectedRunId,
        string message,
        CoordinatorStatusReason reason = CoordinatorStatusReason.Running)
    {
        CoordinatorStatus status;
        Guid? runId;
        long statusVersion;
        lock (_gate)
        {
            if (!IsCurrentRunUnsafe(expectedRunId))
            {
                return;
            }

            status = new CoordinatorStatus(
                CoordinatorTaskState.Running,
                title,
                message,
                _status.StartedAt ?? runtime.Now,
                runtime.Now,
                _status.PollCount,
                _status.RequestCount,
                _status.LastRequestAt,
                reason);
            SetStatusUnsafe(status);
            statusVersion = _statusVersion;
            runId = _runId;
        }

        NotifyStatusChanged(status, runId, statusVersion);
    }

    internal void UpdateRunningMetrics(
        Guid expectedRunId,
        string message,
        int pollCount,
        int requestCount,
        DateTimeOffset? lastRequestAt,
        CoordinatorStatusReason reason = CoordinatorStatusReason.Running)
    {
        CoordinatorStatus status;
        Guid? runId;
        long statusVersion;
        lock (_gate)
        {
            if (!IsCurrentRunUnsafe(expectedRunId))
            {
                return;
            }

            status = new CoordinatorStatus(
                CoordinatorTaskState.Running,
                title,
                message,
                _status.StartedAt ?? runtime.Now,
                runtime.Now,
                pollCount,
                requestCount,
                lastRequestAt,
                reason);
            SetStatusUnsafe(status);
            statusVersion = _statusVersion;
            runId = _runId;
        }

        NotifyStatusChanged(status, runId, statusVersion);
    }

    internal void Complete(Guid expectedRunId, string message, CoordinatorStatusReason reason)
    {
        CoordinatorStatus status;
        Guid? completedRunId;
        long statusVersion;
        lock (_gate)
        {
            if (!IsCurrentRunUnsafe(expectedRunId))
            {
                return;
            }

            completedRunId = _runId;
            status = new CoordinatorStatus(
                CoordinatorTaskState.Completed,
                title,
                message,
                _status.StartedAt,
                runtime.Now,
                _status.PollCount,
                _status.RequestCount,
                _status.LastRequestAt,
                reason);
            SetStatusUnsafe(status);
            statusVersion = _statusVersion;
            ClearRunUnsafe();
            _runId = null;
        }

        NotifyStatusChanged(status, completedRunId, statusVersion);
        WriteTerminalLog(LogLevel.Information, status, completedRunId, exception: null);
    }

    internal void Fail(Guid expectedRunId, string message, CoordinatorStatusReason reason)
    {
        CoordinatorStatus status;
        Guid? failedRunId;
        long statusVersion;
        lock (_gate)
        {
            if (!IsCurrentRunUnsafe(expectedRunId))
            {
                return;
            }

            failedRunId = _runId;
            status = new CoordinatorStatus(
                CoordinatorTaskState.Failed,
                title,
                message,
                _status.StartedAt,
                runtime.Now,
                _status.PollCount,
                _status.RequestCount,
                _status.LastRequestAt,
                reason);
            SetStatusUnsafe(status);
            statusVersion = _statusVersion;
            ClearRunUnsafe();
            _runId = null;
        }

        NotifyStatusChanged(status, failedRunId, statusVersion);
        WriteTerminalLog(LogLevel.Error, status, failedRunId, exception: null);
    }

    private bool IsCurrentRunUnsafe(Guid expectedRunId)
    {
        return _runId == expectedRunId;
    }

    private void WriteTerminalLog(
        LogLevel level,
        CoordinatorStatus status,
        Guid? runId,
        Exception? exception)
    {
        var duration = status.StartedAt is { } startedAt &&
                       status.LastUpdatedAt is { } lastUpdatedAt
            ? lastUpdatedAt - startedAt
            : TimeSpan.Zero;
        WriteLog(
            level,
            $"任务进入终态，运行标识={FormatRunId(runId)}，状态={status.State}，原因={status.Reason}，" +
            $"耗时毫秒={Math.Max(0, duration.TotalMilliseconds):0}，轮询次数={status.PollCount}，请求次数={status.RequestCount}。",
            exception,
            new EventId(2004, "CoordinatorTerminal"));
    }

    private void WriteLog(
        LogLevel level,
        string message,
        Exception? exception = null,
        EventId eventId = default)
    {
        try
        {
            logWriter?.Write(level, $"Coordinator.{title}", message, exception, eventId);
        }
        catch
        {
        }
    }

    private static string FormatRunId(Guid? runId)
    {
        return runId?.ToString("N") ?? "无";
    }
}

internal sealed class CoordinatorRunContext(
    CoordinatorRunController controller,
    Guid runId)
{
    public CoordinatorStatus Status => controller.GetStatus();

    public void SetRunning(
        string message,
        CoordinatorStatusReason reason = CoordinatorStatusReason.Running)
    {
        controller.SetRunning(runId, message, reason);
    }

    public void UpdateRunningMetrics(
        string message,
        int pollCount,
        int requestCount,
        DateTimeOffset? lastRequestAt,
        CoordinatorStatusReason reason = CoordinatorStatusReason.Running)
    {
        controller.UpdateRunningMetrics(runId, message, pollCount, requestCount, lastRequestAt, reason);
    }

    public void Complete(string message, CoordinatorStatusReason reason)
    {
        controller.Complete(runId, message, reason);
    }

    public void Fail(string message, CoordinatorStatusReason reason)
    {
        controller.Fail(runId, message, reason);
    }
}

using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class CoordinatorRunController(string title, ICoordinatorRuntime runtime)
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
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
        var startSignal = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_cts is not null || _runningTask is { IsCompleted: false })
            {
                throw new InvalidOperationException($"{title}任务已在运行。");
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startingStatus = new CoordinatorStatus(
                CoordinatorTaskState.Starting,
                title,
                $"准备启动{title}任务。",
                runtime.Now,
                runtime.Now,
                Reason: CoordinatorStatusReason.Starting);
            SetStatusUnsafe(startingStatus);

            var context = new CoordinatorRunContext(this);
            _runningTask = RunCoreAsync(runAsync, context, startSignal.Task, _cts.Token);
        }

        try
        {
            NotifyStatusChanged(startingStatus);
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
        lock (_gate)
        {
            if (_cts is null)
            {
                return;
            }

            stoppingStatus = GetStatusUnsafe() with
            {
                State = CoordinatorTaskState.Stopping,
                Message = $"正在停止{title}任务。",
                LastUpdatedAt = runtime.Now,
                Reason = CoordinatorStatusReason.Stopping
            };
            SetStatusUnsafe(stoppingStatus);
            cts = _cts;
            runningTask = _runningTask;
        }

        try
        {
            NotifyStatusChanged(stoppingStatus);
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
        CancellationToken cancellationToken)
    {
        try
        {
            await startSignal;
            cancellationToken.ThrowIfCancellationRequested();
            await runAsync(context, cancellationToken);
            if (!IsTerminal(GetStatus().State))
            {
                context.Complete($"{title}任务已停止。", CoordinatorStatusReason.Stopped);
            }
        }
        catch (OperationCanceledException)
        {
            context.Complete($"{title}任务已停止。", CoordinatorStatusReason.Stopped);
        }
        catch (Exception ex)
        {
            context.Fail($"{title}任务失败：{ex.Message}", CoordinatorStatusReason.TaskFailed);
        }
    }

    private static bool IsTerminal(CoordinatorTaskState state)
    {
        return state is CoordinatorTaskState.Completed or CoordinatorTaskState.Failed;
    }

    private CoordinatorStatus GetStatusUnsafe() => _status;

    private void SetStatusUnsafe(CoordinatorStatus status)
    {
        _status = status;
    }

    private void NotifyStatusChanged(CoordinatorStatus status)
    {
        StatusChanged?.Invoke(this, status);
    }

    private void ClearRunUnsafe()
    {
        _cts?.Dispose();
        _cts = null;
        _runningTask = null;
    }

    internal void SetRunning(
        string message,
        CoordinatorStatusReason reason = CoordinatorStatusReason.Running)
    {
        UpdateRunningMetrics(message, _status.PollCount, _status.RequestCount, _status.LastRequestAt, reason);
    }

    internal void UpdateRunningMetrics(
        string message,
        int pollCount,
        int requestCount,
        DateTimeOffset? lastRequestAt,
        CoordinatorStatusReason reason = CoordinatorStatusReason.Running)
    {
        CoordinatorStatus status;
        lock (_gate)
        {
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
        }

        NotifyStatusChanged(status);
    }

    internal void Complete(string message, CoordinatorStatusReason reason)
    {
        CoordinatorStatus status;
        lock (_gate)
        {
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
            ClearRunUnsafe();
        }

        NotifyStatusChanged(status);
    }

    internal void Fail(string message, CoordinatorStatusReason reason)
    {
        CoordinatorStatus status;
        lock (_gate)
        {
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
            ClearRunUnsafe();
        }

        NotifyStatusChanged(status);
    }
}

internal sealed class CoordinatorRunContext(CoordinatorRunController controller)
{
    public CoordinatorStatus Status => controller.GetStatus();

    public void SetRunning(
        string message,
        CoordinatorStatusReason reason = CoordinatorStatusReason.Running)
    {
        controller.SetRunning(message, reason);
    }

    public void UpdateRunningMetrics(
        string message,
        int pollCount,
        int requestCount,
        DateTimeOffset? lastRequestAt,
        CoordinatorStatusReason reason = CoordinatorStatusReason.Running)
    {
        controller.UpdateRunningMetrics(message, pollCount, requestCount, lastRequestAt, reason);
    }

    public void Complete(string message, CoordinatorStatusReason reason)
    {
        controller.Complete(message, reason);
    }

    public void Fail(string message, CoordinatorStatusReason reason)
    {
        controller.Fail(message, reason);
    }
}

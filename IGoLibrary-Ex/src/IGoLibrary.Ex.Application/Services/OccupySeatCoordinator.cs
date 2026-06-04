using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class OccupySeatCoordinator(
    ITraceIntApiClient apiClient,
    IOccupyReReservationExecutor reReservationExecutor,
    ICoordinatorEventPublisher coordinatorEventPublisher,
    IActivityLogService activityLogService,
    AppRuntimeState runtimeState) : IOccupySeatCoordinator
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private CoordinatorStatus _status = CoordinatorStatus.Idle("占座");

    public event EventHandler<CoordinatorStatus>? StatusChanged;

    public CoordinatorStatus GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    public Task StartAsync(OccupySeatPlan plan, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_runningTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("占座任务已在运行。");
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _status = new CoordinatorStatus(
                CoordinatorTaskState.Starting,
                "占座",
                "准备启动占座任务。",
                DateTimeOffset.Now,
                DateTimeOffset.Now);
            NotifyStatusChanged();
            _runningTask = RunAsync(plan, _cts.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? runningTask;
        lock (_gate)
        {
            if (_cts is null)
            {
                return;
            }

            _status = GetStatus() with
            {
                State = CoordinatorTaskState.Stopping,
                Message = "正在停止占座任务。",
                LastUpdatedAt = DateTimeOffset.Now
            };
            NotifyStatusChanged();
            _cts.Cancel();
            runningTask = _runningTask;
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

    private async Task RunAsync(OccupySeatPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            SetRunning("占座任务已启动。");
            activityLogService.Write(LogEntryKind.Success, "Occupy", "占座任务已启动。");
            var random = new Random();
            while (!cancellationToken.IsCancellationRequested)
            {
                var cookie = GetCurrentCookieOrThrow();
                var info = await apiClient.GetReservationInfoAsync(cookie, cancellationToken);
                if (info is null)
                {
                    throw new InvalidOperationException("当前没有可续占的预约。");
                }

                runtimeState.CurrentReservation = info;
                if (!ReservationTimeHelper.ShouldReReserve(info.ExpirationTime, DateTimeOffset.Now))
                {
                    var delay = plan.OccupyRefreshMode == OccupyRefreshMode.FixedTenSeconds
                        ? TimeSpan.FromSeconds(10)
                        : TimeSpan.FromSeconds(random.Next(10, 21));
                    activityLogService.Write(LogEntryKind.Info, "Occupy", $"距离过期还有 {(info.ExpirationTime - DateTimeOffset.Now).TotalSeconds:0} 秒，{delay.TotalSeconds:0} 秒后继续检测。");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                activityLogService.Write(LogEntryKind.Warning, "Occupy", "预约即将过期，开始取消并重新预约。");
                var reservationResult = await reReservationExecutor.ExecuteAsync(cookie, info, plan, cancellationToken);
                if (!reservationResult.Succeeded)
                {
                    throw new InvalidOperationException("重新预约失败，已达到重试上限。");
                }

                activityLogService.Write(LogEntryKind.Success, "Occupy", $"{info.SeatName} 已重新预约成功。");
                await PublishCoordinatorEventSafelyAsync(
                    new OccupyReReserveSucceededCoordinatorEvent(info.SeatName),
                    "发送占座成功提醒失败");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Complete("占座任务已停止。");
        }
        catch (Exception ex)
        {
            Fail($"占座任务失败：{ex.Message}");
            activityLogService.Write(LogEntryKind.Error, "Occupy", ex.Message);
            if (SessionAuthFailureDetector.IsSessionInvalidException(ex, runtimeState.Session?.Cookie))
            {
                await PublishCoordinatorEventSafelyAsync(
                    new SessionInvalidCoordinatorEvent("占座轮询", ex.Message),
                    "发送会话失效提醒失败");
                return;
            }

            await PublishCoordinatorEventSafelyAsync(
                new TaskFailedCoordinatorEvent("占座", ex.Message),
                "发送任务失败提醒失败");
        }
    }

    private void SetRunning(string message)
    {
        lock (_gate)
        {
            _status = new CoordinatorStatus(
                CoordinatorTaskState.Running,
                "占座",
                message,
                _status.StartedAt ?? DateTimeOffset.Now,
                DateTimeOffset.Now);
        }

        NotifyStatusChanged();
    }

    private void Complete(string message)
    {
        lock (_gate)
        {
            _cts = null;
            _runningTask = null;
            _status = new CoordinatorStatus(
                CoordinatorTaskState.Completed,
                "占座",
                message,
                _status.StartedAt,
                DateTimeOffset.Now);
        }

        NotifyStatusChanged();
    }

    private void Fail(string message)
    {
        lock (_gate)
        {
            _cts = null;
            _runningTask = null;
            _status = new CoordinatorStatus(
                CoordinatorTaskState.Failed,
                "占座",
                message,
                _status.StartedAt,
                DateTimeOffset.Now);
        }

        NotifyStatusChanged();
    }

    private void NotifyStatusChanged()
    {
        StatusChanged?.Invoke(this, GetStatus());
    }

    private async Task PublishCoordinatorEventSafelyAsync(CoordinatorEvent @event, string failureMessage)
    {
        try
        {
            await coordinatorEventPublisher.PublishAsync(@event, CancellationToken.None);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"{failureMessage}：{ex.Message}");
        }
    }

    private string GetCurrentCookieOrThrow()
    {
        var cookie = runtimeState.Session?.Cookie ?? throw new InvalidOperationException("当前未登录。");
        if (SessionAuthFailureDetector.TryGetCookieExpirationTime(cookie, out var expirationTime) &&
            expirationTime <= DateTimeOffset.Now)
        {
            throw new InvalidOperationException(SessionAuthFailureDetector.BuildCookieExpiredMessage(expirationTime));
        }

        return cookie;
    }
}

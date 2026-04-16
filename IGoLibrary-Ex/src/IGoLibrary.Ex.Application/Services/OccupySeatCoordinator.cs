using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class OccupySeatCoordinator(
    ITraceIntApiClient apiClient,
    ISettingsService settingsService,
    INotificationService notificationService,
    ITaskAlertService taskAlertService,
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
                var cookie = runtimeState.Session?.Cookie ?? throw new InvalidOperationException("当前未登录。");
                var info = await apiClient.GetReservationInfoAsync(cookie, cancellationToken);
                if (info is null)
                {
                    throw new InvalidOperationException("当前没有可续占的预约。");
                }

                runtimeState.CurrentReservation = info;
                if (!ReservationTimeHelper.ShouldReReserve(info.ExpirationTime, DateTimeOffset.Now))
                {
                    var delay = plan.RefreshMode == RefreshMode.FixedTenSeconds
                        ? TimeSpan.FromSeconds(10)
                        : TimeSpan.FromSeconds(random.Next(10, 21));
                    activityLogService.Write(LogEntryKind.Info, "Occupy", $"距离过期还有 {(info.ExpirationTime - DateTimeOffset.Now).TotalSeconds:0} 秒，{delay.TotalSeconds:0} 秒后继续检测。");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                activityLogService.Write(LogEntryKind.Warning, "Occupy", "预约即将过期，开始取消并重新预约。");
                var cancelled = await apiClient.CancelReservationAsync(cookie, info.ReservationToken, cancellationToken);
                if (!cancelled)
                {
                    throw new InvalidOperationException("取消预约失败。");
                }

                await Task.Delay(plan.ReReserveDelay, cancellationToken);
                var reserved = await TryReserveAgainAsync(cookie, info, cancellationToken);
                if (!reserved)
                {
                    throw new InvalidOperationException("重新预约失败，已达到重试上限。");
                }

                activityLogService.Write(LogEntryKind.Success, "Occupy", $"{info.SeatName} 已重新预约成功。");
                await notificationService.ShowSuccessAsync("占座成功", $"{info.SeatName} 已重新预约。", cancellationToken);
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
            if (CookieExpiryDetector.IsExpired(ex))
            {
                await taskAlertService.NotifyCookieExpiredAsync("占座轮询", ex.Message, CancellationToken.None);
                return;
            }

            await taskAlertService.NotifyTaskFailedAsync("占座", ex.Message, CancellationToken.None);
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

    private async Task<bool> TryReserveAgainAsync(string cookie, ReservationInfo info, CancellationToken cancellationToken)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        var maxAttempts = Math.Max(1, settings.RetryCount + 1);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var reserved = await apiClient.ReserveSeatAsync(cookie, info.LibraryId, info.SeatKey, cancellationToken);
            if (reserved)
            {
                if (attempt > 1)
                {
                    activityLogService.Write(LogEntryKind.Success, "Occupy", $"第 {attempt} 次重新预约尝试成功。");
                }

                return true;
            }

            if (attempt >= maxAttempts)
            {
                break;
            }

            activityLogService.Write(LogEntryKind.Warning, "Occupy", $"第 {attempt} 次重新预约失败，1 秒后继续重试。");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return false;
    }
}

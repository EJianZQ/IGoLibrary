using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class GrabSeatCoordinator(
    ITraceIntApiClient apiClient,
    INotificationService notificationService,
    IActivityLogService activityLogService,
    AppRuntimeState runtimeState) : IGrabSeatCoordinator
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private CoordinatorStatus _status = CoordinatorStatus.Idle("抢座");

    public event EventHandler<CoordinatorStatus>? StatusChanged;

    public CoordinatorStatus GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    public Task StartAsync(GrabSeatPlan plan, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_runningTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("抢座任务已在运行。");
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _status = new CoordinatorStatus(
                CoordinatorTaskState.Starting,
                "抢座",
                "准备启动抢座任务。",
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
                Message = "正在停止抢座任务。",
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

    private async Task RunAsync(GrabSeatPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            if (plan.ScheduledStart is not null)
            {
                await WaitUntilScheduledStartAsync(plan.ScheduledStart.Value, cancellationToken);
            }

            SetRunning("抢座任务已启动。");
            activityLogService.Write(LogEntryKind.Info, "Grab", $"开始监控 {plan.Seats.Count} 个目标座位。");

            var cycle = 0;
            var random = new Random();
            while (!cancellationToken.IsCancellationRequested)
            {
                cycle++;
                var cookie = runtimeState.Session?.Cookie ?? throw new InvalidOperationException("当前未登录。");
                var layout = await apiClient.GetLibraryLayoutAsync(cookie, plan.LibraryId, cancellationToken);
                runtimeState.CurrentLayout = layout;

                var availableSeat = layout.Seats
                    .Where(seat => plan.Seats.Any(target => target.SeatKey == seat.SeatKey))
                    .FirstOrDefault(seat => seat.IsAvailable);

                if (availableSeat is not null)
                {
                    activityLogService.Write(LogEntryKind.Success, "Grab", $"{availableSeat.SeatName} 空闲，正在尝试预约。");
                    var reserved = await apiClient.ReserveSeatAsync(cookie, plan.LibraryId, availableSeat.SeatKey, cancellationToken);
                    if (reserved)
                    {
                        activityLogService.Write(LogEntryKind.Success, "Grab", $"{availableSeat.SeatName} 预约成功。");
                        await notificationService.ShowSuccessAsync("抢座成功", $"{availableSeat.SeatName} 预约成功。", cancellationToken);
                        Complete("已成功预约到目标座位。");
                        return;
                    }
                }
                else
                {
                    activityLogService.Write(LogEntryKind.Info, "Grab", $"第 {cycle} 次轮询：目标座位仍不可用。");
                }

                if (plan.PollingStrategy.CooldownEveryCycles > 0 && cycle % plan.PollingStrategy.CooldownEveryCycles == 0)
                {
                    var cooldown = RandomBetween(plan.PollingStrategy.CooldownMinimum, plan.PollingStrategy.CooldownMaximum, random);
                    activityLogService.Write(LogEntryKind.Info, "Grab", $"达到冷却节点，暂停 {cooldown.TotalSeconds:0} 秒。");
                    await Task.Delay(cooldown, cancellationToken);
                }
                else
                {
                    var delay = RandomBetween(plan.PollingStrategy.MinimumDelay, plan.PollingStrategy.MaximumDelay, random);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Complete("抢座任务已停止。");
        }
        catch (Exception ex)
        {
            Fail($"抢座任务失败：{ex.Message}");
            activityLogService.Write(LogEntryKind.Error, "Grab", ex.Message);
            await notificationService.ShowWarningAsync("抢座失败", ex.Message, CancellationToken.None);
        }
    }

    private async Task WaitUntilScheduledStartAsync(TimeOnly scheduledStart, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = TimeOnly.FromDateTime(DateTime.Now);
            if (now >= scheduledStart)
            {
                return;
            }

            var remaining = scheduledStart.ToTimeSpan() - now.ToTimeSpan();
            activityLogService.Write(LogEntryKind.Info, "Grab", $"定时抢座等待中，还剩 {remaining:hh\\:mm\\:ss}。");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static TimeSpan RandomBetween(TimeSpan minimum, TimeSpan maximum, Random random)
    {
        if (maximum <= minimum)
        {
            return minimum;
        }

        var delta = maximum - minimum;
        var offset = random.NextDouble() * delta.TotalMilliseconds;
        return minimum + TimeSpan.FromMilliseconds(offset);
    }

    private void SetRunning(string message)
    {
        lock (_gate)
        {
            _status = new CoordinatorStatus(
                CoordinatorTaskState.Running,
                "抢座",
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
                "抢座",
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
                "抢座",
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
}

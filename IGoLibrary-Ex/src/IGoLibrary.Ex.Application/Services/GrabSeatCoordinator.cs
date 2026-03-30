using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class GrabSeatCoordinator(
    ITraceIntApiClient apiClient,
    ISettingsService settingsService,
    INotificationService notificationService,
    IActivityLogService activityLogService,
    AppRuntimeState runtimeState) : IGrabSeatCoordinator
{
    private static readonly TimeSpan DirectReserveAttemptInterval = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan DirectReserveRateLimitCycleDelay = TimeSpan.FromSeconds(3);
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
            var settings = await settingsService.LoadAsync(cancellationToken);
            var reservationStrategy = settings.GrabReservationStrategy;
            var directReservationStartIndex = 0;
            activityLogService.Write(LogEntryKind.Info, "Grab", $"当前执行策略：{GetReservationStrategyText(reservationStrategy)}。");

            while (!cancellationToken.IsCancellationRequested)
            {
                cycle++;
                var cookie = runtimeState.Session?.Cookie ?? throw new InvalidOperationException("当前未登录。");
                var reservationResult = reservationStrategy == GrabReservationStrategy.ReserveDirectly
                    ? await TryReserveDirectlyAsync(cookie, plan, directReservationStartIndex, cancellationToken)
                    : await TryReserveAfterAvailabilityCheckAsync(cookie, plan, cancellationToken);
                directReservationStartIndex = reservationResult.NextSeatStartIndex;

                if (reservationResult.ReservedSeat is not null)
                {
                    activityLogService.Write(LogEntryKind.Success, "Grab", $"{reservationResult.ReservedSeat.SeatName} 预约成功。");
                    await notificationService.ShowSuccessAsync("抢座成功", $"{reservationResult.ReservedSeat.SeatName} 预约成功。", cancellationToken);
                    Complete("已成功预约到目标座位。");
                    return;
                }

                if (reservationResult.RateLimitTriggered)
                {
                    activityLogService.Write(LogEntryKind.Warning, "Grab", "直接预约触发速率限制，本轮提前结束，等待下一轮。");
                    var rateLimitDelay = GetDelayAfterRateLimit(plan.PollingStrategy);
                    await Task.Delay(rateLimitDelay, cancellationToken);
                    continue;
                }

                if (!reservationResult.HadReservationAttempt)
                {
                    var missMessage = reservationStrategy == GrabReservationStrategy.ReserveDirectly
                        ? $"第 {cycle} 次轮询：直接预约未命中目标座位。"
                        : $"第 {cycle} 次轮询：目标座位仍不可用。";
                    activityLogService.Write(LogEntryKind.Info, "Grab", missMessage);
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

    private async Task<GrabReservationAttemptResult> TryReserveAfterAvailabilityCheckAsync(
        string cookie,
        GrabSeatPlan plan,
        CancellationToken cancellationToken)
    {
        var layout = await apiClient.GetLibraryLayoutAsync(cookie, plan.LibraryId, cancellationToken);
        runtimeState.CurrentLayout = layout;

        var availableSeat = layout.Seats
            .Where(seat => plan.Seats.Any(target => target.SeatKey == seat.SeatKey))
            .FirstOrDefault(seat => seat.IsAvailable);

        if (availableSeat is null)
        {
            return new GrabReservationAttemptResult(null, false, false, 0);
        }

        activityLogService.Write(LogEntryKind.Success, "Grab", $"{availableSeat.SeatName} 空闲，正在尝试预约。");
        var reserved = await apiClient.ReserveSeatAsync(cookie, plan.LibraryId, availableSeat.SeatKey, cancellationToken);
        return reserved
            ? new GrabReservationAttemptResult(new TrackedSeat(availableSeat.SeatKey, availableSeat.SeatName), true, false, 0)
            : new GrabReservationAttemptResult(null, true, false, 0);
    }

    private async Task<GrabReservationAttemptResult> TryReserveDirectlyAsync(
        string cookie,
        GrabSeatPlan plan,
        int startIndex,
        CancellationToken cancellationToken)
    {
        if (plan.Seats.Count == 0)
        {
            return new GrabReservationAttemptResult(null, false, false, 0);
        }

        for (var offset = 0; offset < plan.Seats.Count; offset++)
        {
            var index = (startIndex + offset) % plan.Seats.Count;
            var seat = plan.Seats[index];
            bool reserved;
            try
            {
                reserved = await apiClient.ReserveSeatAsync(cookie, plan.LibraryId, seat.SeatKey, cancellationToken);
            }
            catch (Exception ex) when (TryGetExpectedDirectReservationMiss(ex, out var missKind))
            {
                activityLogService.Write(LogEntryKind.Info, "Grab", GetDirectReservationMissMessage(missKind, seat));
                if (missKind == DirectReservationMissKind.RetryRequested)
                {
                    return new GrabReservationAttemptResult(null, true, true, (index + 1) % plan.Seats.Count);
                }

                await DelayBeforeNextDirectReserveAttemptAsync(offset, plan.Seats.Count, DirectReserveAttemptInterval, cancellationToken);
                continue;
            }

            if (reserved)
            {
                return new GrabReservationAttemptResult(seat, true, false, (index + 1) % plan.Seats.Count);
            }

            await DelayBeforeNextDirectReserveAttemptAsync(offset, plan.Seats.Count, DirectReserveAttemptInterval, cancellationToken);
        }

        return new GrabReservationAttemptResult(null, false, false, startIndex);
    }

    private static string GetReservationStrategyText(GrabReservationStrategy strategy)
    {
        return strategy switch
        {
            GrabReservationStrategy.ReserveDirectly => "直接预约看返回值",
            _ => "先查列表再预约"
        };
    }

    private static bool TryGetExpectedDirectReservationMiss(
        Exception exception,
        out DirectReservationMissKind missKind)
    {
        missKind = DirectReservationMissKind.None;
        if (exception is not InvalidOperationException)
        {
            return false;
        }

        var message = exception.Message;
        if (!message.Contains("GraphQL 错误(code=1)", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (message.Contains("请重新尝试", StringComparison.OrdinalIgnoreCase))
        {
            missKind = DirectReservationMissKind.RetryRequested;
            return true;
        }

        if (message.Contains("座位", StringComparison.OrdinalIgnoreCase) &&
            (message.Contains("预定", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("预约", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("不可预约", StringComparison.OrdinalIgnoreCase)))
        {
            missKind = DirectReservationMissKind.Occupied;
            return true;
        }

        return false;
    }

    private static string GetDirectReservationMissMessage(DirectReservationMissKind missKind, TrackedSeat seat)
    {
        return missKind switch
        {
            DirectReservationMissKind.RetryRequested =>
                $"{seat.SeatName} 返回“请重新尝试”，触发短暂退避后继续。",
            DirectReservationMissKind.Occupied =>
                $"{seat.SeatName} 已被占用，继续尝试下一个目标座位。",
            _ => $"{seat.SeatName} 预约未命中。"
        };
    }

    private static async Task DelayBeforeNextDirectReserveAttemptAsync(
        int currentOffset,
        int seatCount,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (currentOffset >= seatCount - 1 || delay <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(delay, cancellationToken);
    }

    private static TimeSpan GetDelayAfterRateLimit(GrabSeatPollingStrategy pollingStrategy)
    {
        if (pollingStrategy.MaximumDelay <= pollingStrategy.MinimumDelay)
        {
            return pollingStrategy.MinimumDelay > DirectReserveRateLimitCycleDelay
                ? pollingStrategy.MinimumDelay
                : DirectReserveRateLimitCycleDelay;
        }

        return pollingStrategy.MaximumDelay > DirectReserveRateLimitCycleDelay
            ? pollingStrategy.MaximumDelay
            : DirectReserveRateLimitCycleDelay;
    }

    private sealed record GrabReservationAttemptResult(
        TrackedSeat? ReservedSeat,
        bool HadReservationAttempt,
        bool RateLimitTriggered,
        int NextSeatStartIndex);

    private enum DirectReservationMissKind
    {
        None = 0,
        Occupied = 1,
        RetryRequested = 2
    }
}

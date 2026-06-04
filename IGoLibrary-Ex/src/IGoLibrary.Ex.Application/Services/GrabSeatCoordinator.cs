using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class GrabSeatCoordinator(
    ISettingsService settingsService,
    GrabReservationStrategySelector strategySelector,
    ICoordinatorEventPublisher coordinatorEventPublisher,
    IActivityLogService activityLogService,
    AppRuntimeState runtimeState) : IGrabSeatCoordinator
{
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
            var requestCount = 0;
            DateTimeOffset? lastRequestAt = null;
            var random = new Random();
            var settings = await settingsService.LoadAsync(cancellationToken);
            var reservationStrategy = settings.Tasks.GrabReservationStrategy;
            var reservationAttemptStrategy = strategySelector.Select(reservationStrategy);
            var directReservationStartIndex = 0;
            activityLogService.Write(LogEntryKind.Info, "Grab", $"当前执行策略：{GetReservationStrategyText(reservationStrategy)}。");

            while (!cancellationToken.IsCancellationRequested)
            {
                cycle++;
                UpdateRunningMetrics("抢座任务运行中。", cycle, requestCount, lastRequestAt);
                var cookie = GetCurrentCookieOrThrow();
                void MarkRequestSent()
                {
                    requestCount++;
                    lastRequestAt = DateTimeOffset.Now;
                    UpdateRunningMetrics("抢座任务运行中。", cycle, requestCount, lastRequestAt);
                }

                var reservationResult = await reservationAttemptStrategy.TryReserveAsync(
                    new GrabReservationAttemptContext(cookie, plan, directReservationStartIndex, MarkRequestSent),
                    cancellationToken);
                directReservationStartIndex = reservationResult.NextSeatStartIndex;

                if (reservationResult.ReservedSeat is not null)
                {
                    var reservedSeatName = reservationResult.ReservedSeat.SeatName;
                    activityLogService.Write(LogEntryKind.Success, "Grab", $"{reservedSeatName} 预约成功。");
                    Complete("已成功预约到目标座位。");
                    _ = PublishCoordinatorEventSafelyAsync(
                        new GrabSucceededCoordinatorEvent(plan.LibraryName, reservedSeatName),
                        "发送抢座成功提醒失败");
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
            if (SessionAuthFailureDetector.IsSessionInvalidException(ex, runtimeState.Session?.Cookie))
            {
                await PublishCoordinatorEventSafelyAsync(
                    new SessionInvalidCoordinatorEvent("抢座轮询", ex.Message),
                    "发送会话失效提醒失败");
                return;
            }

            await PublishCoordinatorEventSafelyAsync(
                new TaskFailedCoordinatorEvent("抢座", ex.Message),
                "发送任务失败提醒失败");
        }
    }

    private async Task WaitUntilScheduledStartAsync(TimeOnly scheduledStart, CancellationToken cancellationToken)
    {
        var targetStart = ResolveNextScheduledStart(scheduledStart, DateTimeOffset.Now);
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.Now;
            var remaining = targetStart - now;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            activityLogService.Write(
                LogEntryKind.Info,
                "Grab",
                $"定时抢座等待中，目标启动时间 {targetStart:yyyy-MM-dd HH:mm:ss}，还剩 {remaining:hh\\:mm\\:ss}。");
            await Task.Delay(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    internal static DateTimeOffset ResolveNextScheduledStart(TimeOnly scheduledStart, DateTimeOffset now)
    {
        var todayScheduledStart = new DateTimeOffset(
            now.Date.Add(scheduledStart.ToTimeSpan()),
            now.Offset);

        return todayScheduledStart < now
            ? todayScheduledStart.AddDays(1)
            : todayScheduledStart;
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
        UpdateRunningMetrics(message, _status.PollCount, _status.RequestCount, _status.LastRequestAt);
    }

    private void UpdateRunningMetrics(
        string message,
        int pollCount,
        int requestCount,
        DateTimeOffset? lastRequestAt)
    {
        lock (_gate)
        {
            _status = new CoordinatorStatus(
                CoordinatorTaskState.Running,
                "抢座",
                message,
                _status.StartedAt ?? DateTimeOffset.Now,
                DateTimeOffset.Now,
                pollCount,
                requestCount,
                lastRequestAt);
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
                DateTimeOffset.Now,
                _status.PollCount,
                _status.RequestCount,
                _status.LastRequestAt);
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
                DateTimeOffset.Now,
                _status.PollCount,
                _status.RequestCount,
                _status.LastRequestAt);
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

    private static string GetReservationStrategyText(GrabReservationStrategy strategy)
    {
        return strategy switch
        {
            GrabReservationStrategy.ReserveDirectly => "直接预约看返回值",
            _ => "先查列表再预约"
        };
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
}

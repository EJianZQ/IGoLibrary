using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class GrabSeatWorkflowRunner(
    ISettingsService settingsService,
    GrabReservationStrategySelector strategySelector,
    ICoordinatorEventPublisher coordinatorEventPublisher,
    IActivityLogService activityLogService,
    ISessionState sessionState,
    IVenueState venueState,
    ICoordinatorRuntime runtime)
{
    public async Task RunAsync(
        GrabSeatPlan plan,
        CoordinatorRunContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            if (plan.ScheduledStart is not null)
            {
                await WaitUntilScheduledStartAsync(plan.ScheduledStart.Value, cancellationToken);
            }

            context.SetRunning("抢座任务已启动。");
            activityLogService.Write(LogEntryKind.Info, "Grab", $"开始监控 {plan.Seats.Count} 个目标座位。");

            var cycle = 0;
            var requestCount = 0;
            DateTimeOffset? lastRequestAt = null;
            var settings = await settingsService.LoadAsync(cancellationToken);
            var reservationStrategy = settings.Tasks.Grab.ReservationStrategy;
            var reservationAttemptStrategy = strategySelector.Select(reservationStrategy);
            var directReservationStartIndex = 0;
            activityLogService.Write(LogEntryKind.Info, "Grab", $"当前执行策略：{GetReservationStrategyText(reservationStrategy)}。");

            while (!cancellationToken.IsCancellationRequested)
            {
                cycle++;
                context.UpdateRunningMetrics("抢座任务运行中。", cycle, requestCount, lastRequestAt);
                var cookie = GetCurrentCookieOrThrow();
                void MarkRequestSent()
                {
                    requestCount++;
                    lastRequestAt = runtime.Now;
                    context.UpdateRunningMetrics("抢座任务运行中。", cycle, requestCount, lastRequestAt);
                }

                var reservationResult = await reservationAttemptStrategy.TryReserveAsync(
                    new GrabReservationAttemptContext(cookie, plan, directReservationStartIndex, MarkRequestSent),
                    cancellationToken);
                directReservationStartIndex = reservationResult.NextSeatStartIndex;
                if (reservationResult.LatestLayout is not null)
                {
                    venueState.CurrentLayout = reservationResult.LatestLayout;
                }

                if (reservationResult.ReservedSeat is not null)
                {
                    var reservedSeatName = reservationResult.ReservedSeat.SeatName;
                    activityLogService.Write(LogEntryKind.Success, "Grab", $"{reservedSeatName} 预约成功。");
                    context.Complete("已成功预约到目标座位。", CoordinatorStatusReason.GrabSucceeded);
                    _ = PublishCoordinatorEventSafelyAsync(
                        new GrabSucceededCoordinatorEvent(plan.LibraryName, reservedSeatName),
                        "发送抢座成功提醒失败");
                    return;
                }

                if (reservationResult.RateLimitTriggered)
                {
                    activityLogService.Write(LogEntryKind.Warning, "Grab", "直接预约触发速率限制，本轮提前结束，等待下一轮。");
                    var rateLimitDelay = GrabSeatStateMachine.GetDelayAfterRateLimit(plan.PollingStrategy);
                    await runtime.DelayAsync(rateLimitDelay, cancellationToken);
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
                    var cooldown = runtime.RandomBetween(
                        plan.PollingStrategy.CooldownMinimum,
                        plan.PollingStrategy.CooldownMaximum);
                    activityLogService.Write(LogEntryKind.Info, "Grab", $"达到冷却节点，暂停 {cooldown.TotalSeconds:0} 秒。");
                    await runtime.DelayAsync(cooldown, cancellationToken);
                }
                else
                {
                    var delay = runtime.RandomBetween(
                        plan.PollingStrategy.MinimumDelay,
                        plan.PollingStrategy.MaximumDelay);
                    await runtime.DelayAsync(delay, cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var isSessionInvalid = IsSessionInvalidException(ex);
            context.Fail(
                $"抢座任务失败：{ex.Message}",
                isSessionInvalid ? CoordinatorStatusReason.SessionInvalid : CoordinatorStatusReason.TaskFailed);
            activityLogService.Write(LogEntryKind.Error, "Grab", ex.Message);
            if (isSessionInvalid)
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
        var targetStart = GrabSeatStateMachine.ResolveNextScheduledStart(scheduledStart, runtime.Now);
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = runtime.Now;
            var remaining = targetStart - now;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            activityLogService.Write(
                LogEntryKind.Info,
                "Grab",
                $"定时抢座等待中，目标启动时间 {targetStart:yyyy-MM-dd HH:mm:ss}，还剩 {remaining:hh\\:mm\\:ss}。");
            await runtime.DelayAsync(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private bool IsSessionInvalidException(Exception ex)
    {
        return SessionAuthFailureDetector.IsSessionInvalidException(ex, sessionState.Session?.Cookie, runtime.Now);
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
        var cookie = sessionState.Session?.Cookie ?? throw new InvalidOperationException("当前未登录。");
        if (SessionAuthFailureDetector.TryGetCookieExpirationTime(cookie, out var expirationTime) &&
            expirationTime <= runtime.Now)
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
}

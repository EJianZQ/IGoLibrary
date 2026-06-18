using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class CheckInGuardWorkflowRunner(
    ITraceIntApiClient apiClient,
    ICoordinatorEventPublisher coordinatorEventPublisher,
    IActivityLogService activityLogService,
    ISessionState sessionState,
    IReservationState reservationState,
    ICoordinatorRuntime runtime)
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinimumPollInterval = TimeSpan.FromSeconds(1);

    public async Task RunAsync(
        CheckInGuardPlan plan,
        CoordinatorRunContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentDeadline = plan.Deadline;
            var reminderAt = currentDeadline - plan.ReminderLeadTime;
            var actionAt = ResolveActionDeadline(currentDeadline, plan);
            var reminderSent = false;

            context.SetRunning(BuildRunningMessage(currentDeadline));
            activityLogService.Write(LogEntryKind.Success, "CheckIn", $"签到守护已启动，签到截止 {currentDeadline:HH:mm:ss}，自动处理 {actionAt:HH:mm:ss}。");

            while (!cancellationToken.IsCancellationRequested)
            {
                var cookie = GetCurrentCookieOrThrow();
                var reservation = await apiClient.GetReservationInfoAsync(cookie, cancellationToken);
                if (reservation is null)
                {
                    reservationState.CurrentReservation = null;
                    context.Complete("当前预约已不存在，签到守护已结束。", CoordinatorStatusReason.CheckInGuardCompleted);
                    activityLogService.Write(LogEntryKind.Info, "CheckIn", "当前预约已不存在，签到守护已结束。");
                    return;
                }

                reservationState.CurrentReservation = reservation;
                if (reservation.IsCheckedIn)
                {
                    context.Complete("已检测到签到，签到守护已结束。", CoordinatorStatusReason.CheckInGuardCompleted);
                    activityLogService.Write(LogEntryKind.Success, "CheckIn", $"{reservation.SeatName} 已检测到签到。");
                    return;
                }

                var now = runtime.Now;
                if (!reminderSent && now >= reminderAt)
                {
                    reminderSent = true;
                    context.SetRunning("签到提醒已发送，继续等待截止时间。", CoordinatorStatusReason.CheckInReminderSent);
                    activityLogService.Write(LogEntryKind.Warning, "CheckIn", $"{reservation.SeatName} 即将到达签到截止时间。");
                    await PublishCoordinatorEventSafelyAsync(
                        new CheckInReminderCoordinatorEvent(reservation.LibraryName, reservation.SeatName, currentDeadline),
                        "发送签到提醒失败");
                }

                if (now >= actionAt)
                {
                    var result = await HandleMissedCheckInAsync(
                        cookie,
                        reservation,
                        plan,
                        currentDeadline,
                        context,
                        cancellationToken);
                    if (!result.ShouldContinue)
                    {
                        return;
                    }

                    currentDeadline = result.NextDeadline!.Value;
                    reminderAt = currentDeadline - plan.ReminderLeadTime;
                    actionAt = ResolveActionDeadline(currentDeadline, plan);
                    reminderSent = false;
                    context.SetRunning($"自动补约已完成，继续守护下次签到，截止 {currentDeadline:HH:mm:ss}。");
                    activityLogService.Write(LogEntryKind.Info, "CheckIn", $"已进入下一轮签到守护，签到截止 {currentDeadline:HH:mm:ss}，自动处理 {actionAt:HH:mm:ss}。");
                    continue;
                }

                var delay = ResolveNextDelay(now, ResolveNextMilestone(now, reminderAt, actionAt, reminderSent));
                context.SetRunning(BuildRunningMessage(currentDeadline));
                await runtime.DelayAsync(delay, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var isSessionInvalid = SessionAuthFailureDetector.IsSessionInvalidException(ex, sessionState.Session?.Cookie, runtime.Now);
            context.Fail(
                $"签到守护失败：{ex.Message}",
                isSessionInvalid ? CoordinatorStatusReason.SessionInvalid : CoordinatorStatusReason.TaskFailed);
            activityLogService.Write(LogEntryKind.Error, "CheckIn", ex.Message);

            if (isSessionInvalid)
            {
                await PublishCoordinatorEventSafelyAsync(
                    new SessionInvalidCoordinatorEvent("签到守护", ex.Message),
                    "发送会话失效提醒失败");
                return;
            }

            await PublishCoordinatorEventSafelyAsync(
                new TaskFailedCoordinatorEvent("签到守护", ex.Message),
                "发送任务失败提醒失败");
        }
    }

    private async Task<CheckInMissedResult> HandleMissedCheckInAsync(
        string cookie,
        ReservationInfo reservation,
        CheckInGuardPlan plan,
        DateTimeOffset deadline,
        CoordinatorRunContext context,
        CancellationToken cancellationToken)
    {
        var latestReservation = await apiClient.GetReservationInfoAsync(cookie, cancellationToken);
        if (latestReservation is null)
        {
            reservationState.CurrentReservation = null;
            context.Complete("当前预约已不存在，签到守护已结束。", CoordinatorStatusReason.CheckInGuardCompleted);
            return CheckInMissedResult.Completed;
        }

        reservationState.CurrentReservation = latestReservation;
        if (latestReservation.IsCheckedIn)
        {
            context.Complete("已检测到签到，签到守护已结束。", CoordinatorStatusReason.CheckInGuardCompleted);
            activityLogService.Write(LogEntryKind.Success, "CheckIn", $"{latestReservation.SeatName} 已检测到签到。");
            return CheckInMissedResult.Completed;
        }

        var actionText = BuildMissedActionText(plan.MissedAction);
        await PublishCoordinatorEventSafelyAsync(
            new CheckInMissedCoordinatorEvent(
                latestReservation.LibraryName,
                latestReservation.SeatName,
                deadline,
                actionText),
            "发送错过签到提醒失败");

        if (plan.MissedAction == CheckInGuardMissedAction.NotifyOnly)
        {
            context.Complete("已超过签到时间，仅发送提醒。", CoordinatorStatusReason.CheckInGuardCompleted);
            activityLogService.Write(LogEntryKind.Warning, "CheckIn", $"{latestReservation.SeatName} 已超过签到时间，仅发送提醒。");
            return CheckInMissedResult.Completed;
        }

        var cancelled = await apiClient.CancelReservationAsync(cookie, latestReservation.ReservationToken, cancellationToken);
        if (!cancelled)
        {
            throw new InvalidOperationException("自动退座失败，接口未返回成功结果。");
        }

        reservationState.CurrentReservation = null;
        activityLogService.Write(LogEntryKind.Warning, "CheckIn", $"{latestReservation.SeatName} 已超过签到时间，已自动退座。");

        if (plan.MissedAction == CheckInGuardMissedAction.CancelReservation)
        {
            context.Complete("已超过签到时间，已自动退座。", CoordinatorStatusReason.CheckInGuardCompleted);
            return CheckInMissedResult.Completed;
        }

        await runtime.DelayAsync(plan.ReReserveDelay, cancellationToken);
        var rescued = await TryReserveSameSeatAsync(cookie, latestReservation, cancellationToken);
        if (!rescued.Succeeded && plan.MissedAction == CheckInGuardMissedAction.CancelAndReserveSameSeatOrRandomInLibrary)
        {
            rescued = await TryReserveRandomSeatAsync(cookie, latestReservation, plan, cancellationToken);
        }

        if (!rescued.Succeeded)
        {
            throw new InvalidOperationException("自动退座后重新预约失败。");
        }

        var refreshed = await apiClient.GetReservationInfoAsync(cookie, cancellationToken);
        reservationState.CurrentReservation = refreshed;
        var rescuedLibraryName = refreshed?.LibraryName ?? rescued.LibraryName ?? latestReservation.LibraryName;
        var rescuedSeatName = refreshed?.SeatName ?? rescued.SeatName ?? latestReservation.SeatName;
        await PublishCoordinatorEventSafelyAsync(
            new CheckInAutoRescueSucceededCoordinatorEvent(rescuedLibraryName, rescuedSeatName),
            "发送签到补约成功提醒失败");

        activityLogService.Write(LogEntryKind.Success, "CheckIn", $"{rescuedLibraryName} · {rescuedSeatName} 已完成签到防漏补约。");
        return CheckInMissedResult.Continue(ResolveNextDeadlineAfterRescue(plan, refreshed, runtime.Now));
    }

    private async Task<CheckInRescueResult> TryReserveSameSeatAsync(
        string cookie,
        ReservationInfo reservation,
        CancellationToken cancellationToken)
    {
        var reserved = await apiClient.ReserveSeatAsync(
            cookie,
            reservation.LibraryId,
            reservation.SeatKey,
            cancellationToken);

        return reserved
            ? new CheckInRescueResult(true, reservation.LibraryName, reservation.SeatName)
            : CheckInRescueResult.Failed;
    }

    private async Task<CheckInRescueResult> TryReserveRandomSeatAsync(
        string cookie,
        ReservationInfo reservation,
        CheckInGuardPlan plan,
        CancellationToken cancellationToken)
    {
        var libraryId = plan.FallbackLibraryId ?? reservation.LibraryId;
        var layout = await apiClient.GetLibraryLayoutAsync(cookie, libraryId, cancellationToken);
        var availableSeats = layout.Seats.Where(static seat => seat.IsAvailable).ToList();
        if (availableSeats.Count == 0)
        {
            return CheckInRescueResult.Failed;
        }

        while (availableSeats.Count > 0)
        {
            var index = runtime.NextInt(0, availableSeats.Count);
            var seat = availableSeats[index];
            availableSeats.RemoveAt(index);
            var reserved = await apiClient.ReserveSeatAsync(cookie, layout.LibraryId, seat.SeatKey, cancellationToken);
            if (reserved)
            {
                return new CheckInRescueResult(true, layout.Name, seat.SeatName);
            }
        }

        return CheckInRescueResult.Failed;
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

    private TimeSpan ResolveNextDelay(DateTimeOffset now, DateTimeOffset nextMilestone)
    {
        var untilMilestone = nextMilestone - now;
        if (untilMilestone <= MinimumPollInterval)
        {
            return MinimumPollInterval;
        }

        return untilMilestone < DefaultPollInterval ? untilMilestone : DefaultPollInterval;
    }

    private static DateTimeOffset ResolveNextDeadlineAfterRescue(
        CheckInGuardPlan plan,
        ReservationInfo? refreshed,
        DateTimeOffset now)
    {
        if (refreshed?.ValidateTime is { } validateTime && validateTime > now)
        {
            return validateTime;
        }

        var requiredWithin = plan.CheckInRequiredWithin > TimeSpan.Zero
            ? plan.CheckInRequiredWithin
            : TimeSpan.FromMinutes(20);
        return now + requiredWithin;
    }

    private static DateTimeOffset ResolveActionDeadline(DateTimeOffset checkInDeadline, CheckInGuardPlan plan)
    {
        if (plan.MissedAction == CheckInGuardMissedAction.NotifyOnly || plan.MissedActionLeadTime <= TimeSpan.Zero)
        {
            return checkInDeadline;
        }

        return checkInDeadline - plan.MissedActionLeadTime;
    }

    private static DateTimeOffset ResolveNextMilestone(
        DateTimeOffset now,
        DateTimeOffset reminderAt,
        DateTimeOffset actionAt,
        bool reminderSent)
    {
        if (!reminderSent && reminderAt > now && reminderAt < actionAt)
        {
            return reminderAt;
        }

        return actionAt;
    }

    private static string BuildRunningMessage(DateTimeOffset deadline)
    {
        return $"签到守护运行中，截止 {deadline:HH:mm:ss}。";
    }

    private static string BuildMissedActionText(CheckInGuardMissedAction action)
    {
        return action switch
        {
            CheckInGuardMissedAction.NotifyOnly => "仅发送提醒",
            CheckInGuardMissedAction.CancelReservation => "自动退座",
            CheckInGuardMissedAction.CancelAndReserveSameSeat => "自动退座后重约原座",
            CheckInGuardMissedAction.CancelAndReserveSameSeatOrRandomInLibrary => "自动退座后重约原座，失败则随机补约锁定场馆",
            _ => "仅发送提醒"
        };
    }

    private string GetCurrentCookieOrThrow()
    {
        return sessionState.Session?.Cookie ?? throw new InvalidOperationException("当前未登录。");
    }

    private sealed record CheckInRescueResult(bool Succeeded, string? LibraryName, string? SeatName)
    {
        public static CheckInRescueResult Failed { get; } = new(false, null, null);
    }

    private sealed record CheckInMissedResult(bool ShouldContinue, DateTimeOffset? NextDeadline)
    {
        public static CheckInMissedResult Completed { get; } = new(false, null);

        public static CheckInMissedResult Continue(DateTimeOffset nextDeadline) => new(true, nextDeadline);
    }
}

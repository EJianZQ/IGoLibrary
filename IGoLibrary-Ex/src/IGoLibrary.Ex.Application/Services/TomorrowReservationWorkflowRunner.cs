using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class TomorrowReservationWorkflowRunner(
    ITraceIntApiClient apiClient,
    ICoordinatorEventPublisher coordinatorEventPublisher,
    IActivityLogService activityLogService,
    ISessionState sessionState,
    ICoordinatorRuntime runtime)
{
    public async Task RunAsync(
        TomorrowReservationPlan plan,
        CoordinatorRunContext context,
        CancellationToken cancellationToken)
    {
        var requestCount = 0;
        DateTimeOffset? lastRequestAt = null;

        void MarkRequestSent(string message)
        {
            requestCount++;
            lastRequestAt = runtime.Now;
            context.UpdateRunningMetrics(message, context.Status.PollCount, requestCount, lastRequestAt);
        }

        try
        {
            if (!plan.ExecuteImmediately)
            {
                await WaitUntilScheduledStartAsync(plan.ScheduledStart, context, cancellationToken);
            }

            context.UpdateRunningMetrics("明日预约任务已启动", 1, requestCount, lastRequestAt);
            activityLogService.Write(LogEntryKind.Info, "Tomorrow", $"{plan.LibraryName} · {plan.Seat.SeatName} 开始执行明日预约");

            var cookie = GetCurrentCookieOrThrow();

            MarkRequestSent("正在进入明日预约排队通道");
            var queueResult = await apiClient.EnterTomorrowReservationQueueAsync(cookie, cancellationToken);
            if (queueResult.ShouldStop)
            {
                throw new InvalidOperationException($"排队被拦截: {queueResult.Message}");
            }

            activityLogService.Write(LogEntryKind.Info, "Tomorrow", queueResult.Message);

            try
            {
                MarkRequestSent("正在预热明日预约场馆");
                await apiClient.WarmUpTomorrowReservationAsync(cookie, plan.LibraryId, cancellationToken);
                activityLogService.Write(LogEntryKind.Info, "Tomorrow", "明日预约预热请求已完成");
                await runtime.DelayAsync(TimeSpan.FromMilliseconds(200), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                activityLogService.Write(LogEntryKind.Warning, "Tomorrow", $"明日预约预热失败，继续提交预约：{ex.Message}");
            }

            MarkRequestSent("正在提交明日预约");
            var saved = await apiClient.SaveTomorrowReservationAsync(
                cookie,
                plan.LibraryId,
                plan.Seat.SeatKey,
                cancellationToken);
            if (!saved)
            {
                throw new InvalidOperationException("明日预约保存接口未返回成功结果");
            }

            TomorrowReservationInfo? verification = null;
            try
            {
                MarkRequestSent("正在验证明日预约结果");
                verification = await apiClient.GetTomorrowReservationInfoAsync(cookie, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                activityLogService.Write(LogEntryKind.Warning, "Tomorrow", $"明日预约结果验证失败：{ex.Message}");
            }

            var matchedVerification = verification is not null &&
                                      TomorrowReservationStateMachine.IsVerificationRecordForPlan(plan, verification)
                ? verification
                : null;
            if (verification is not null && matchedVerification is null)
            {
                activityLogService.Write(
                    LogEntryKind.Warning,
                    "Tomorrow",
                    $"明日预约验证记录与本次目标不一致：期望 {plan.LibraryId}/{plan.Seat.SeatKey}，实际 {verification.LibraryId}/{verification.SeatKey}");
            }

            var successMessage = TomorrowReservationStateMachine.BuildVerificationText(plan, verification);
            activityLogService.Write(LogEntryKind.Success, "Tomorrow", successMessage);
            context.UpdateRunningMetrics(successMessage, context.Status.PollCount, requestCount, lastRequestAt);
            context.Complete(successMessage, CoordinatorStatusReason.TomorrowReservationSucceeded);
            _ = PublishCoordinatorEventSafelyAsync(
                new TomorrowReservationSucceededCoordinatorEvent(
                    plan.LibraryName,
                    matchedVerification?.SeatName ?? plan.Seat.SeatName,
                    matchedVerification?.Day),
                "发送明日预约成功提醒失败");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var isSessionInvalid = IsSessionInvalidException(ex);
            context.Fail(
                $"明日预约任务失败：{ex.Message}",
                isSessionInvalid ? CoordinatorStatusReason.SessionInvalid : CoordinatorStatusReason.TaskFailed);
            activityLogService.Write(LogEntryKind.Error, "Tomorrow", ex.Message);
            if (isSessionInvalid)
            {
                await PublishCoordinatorEventSafelyAsync(
                    new SessionInvalidCoordinatorEvent("明日预约", ex.Message),
                    "发送会话失效提醒失败");
                return;
            }

            await PublishCoordinatorEventSafelyAsync(
                new TaskFailedCoordinatorEvent("明日预约", ex.Message),
                "发送任务失败提醒失败");
        }
    }

    private async Task WaitUntilScheduledStartAsync(
        TimeOnly scheduledStart,
        CoordinatorRunContext context,
        CancellationToken cancellationToken)
    {
        var targetStart = TomorrowReservationStateMachine.ResolveNextScheduledStart(scheduledStart, runtime.Now);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = runtime.Now;
            var remaining = targetStart - now;
            var delay = TomorrowReservationStateMachine.ResolveWaitDelay(remaining);
            if (delay <= TimeSpan.Zero)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            context.SetRunning($"明日预约等待中，目标触发时间 {targetStart:yyyy-MM-dd HH:mm:ss}");
            activityLogService.Write(
                LogEntryKind.Info,
                "Tomorrow",
                $"明日预约等待中，目标触发时间 {targetStart:yyyy-MM-dd HH:mm:ss}，还剩 {remaining:hh\\:mm\\:ss}");
            await runtime.DelayAsync(delay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
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
        var cookie = sessionState.Session?.Cookie ?? throw new InvalidOperationException("当前未登录");
        if (SessionAuthFailureDetector.TryGetCookieExpirationTime(cookie, out var expirationTime) &&
            expirationTime <= runtime.Now)
        {
            throw new InvalidOperationException(SessionAuthFailureDetector.BuildCookieExpiredMessage(expirationTime));
        }

        return cookie;
    }
}

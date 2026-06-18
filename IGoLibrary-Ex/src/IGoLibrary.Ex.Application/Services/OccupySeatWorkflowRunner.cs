using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class OccupySeatWorkflowRunner(
    ISettingsService settingsService,
    ITraceIntApiClient apiClient,
    IOccupyReReservationExecutor reReservationExecutor,
    ICoordinatorEventPublisher coordinatorEventPublisher,
    IActivityLogService activityLogService,
    ISessionState sessionState,
    IReservationState reservationState,
    ICoordinatorRuntime runtime)
{
    public async Task RunAsync(
        OccupySeatPlan plan,
        CoordinatorRunContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            context.SetRunning("占座任务已启动。");
            activityLogService.Write(LogEntryKind.Success, "Occupy", "占座任务已启动。");

            while (!cancellationToken.IsCancellationRequested)
            {
                var cookie = GetCurrentCookieOrThrow();
                var info = await apiClient.GetReservationInfoAsync(cookie, cancellationToken);
                if (info is null)
                {
                    throw new InvalidOperationException("当前没有可续占的预约。");
                }

                reservationState.CurrentReservation = info;
                if (!OccupySeatStateMachine.ShouldReReserve(info, runtime.Now))
                {
                    var delay = OccupySeatStateMachine.ResolveCheckDelay(
                        plan.OccupyCheckIntervalMode,
                        plan.OccupyCheckIntervalMode == OccupyCheckIntervalMode.FixedTenSeconds ? 10 : runtime.NextInt(10, 21));
                    activityLogService.Write(LogEntryKind.Info, "Occupy", $"距离过期还有 {(info.ExpirationTime - runtime.Now).TotalSeconds:0} 秒，{delay.TotalSeconds:0} 秒后继续检测。");
                    await runtime.DelayAsync(delay, cancellationToken);
                    continue;
                }

                activityLogService.Write(LogEntryKind.Warning, "Occupy", "预约即将过期，开始取消并重新预约。");
                var settings = await settingsService.LoadAsync(cancellationToken);
                var maxAttempts = Math.Max(1, settings.Tasks.Occupy.ReReservationMaxAttempts);
                var reservationResult = await reReservationExecutor.ExecuteAsync(
                    cookie,
                    info,
                    plan,
                    maxAttempts,
                    cancellationToken);
                if (!reservationResult.Succeeded)
                {
                    throw new InvalidOperationException("重新预约失败，已达到重试上限。");
                }

                activityLogService.Write(LogEntryKind.Success, "Occupy", $"{info.SeatName} 已重新预约成功。");
                context.SetRunning("占座任务已启动。", CoordinatorStatusReason.OccupyReReserveSucceeded);
                await PublishCoordinatorEventSafelyAsync(
                    new OccupyReReserveSucceededCoordinatorEvent(info.SeatName),
                    "发送占座成功提醒失败");
                await runtime.DelayAsync(TimeSpan.FromSeconds(5), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                context.SetRunning("占座任务已启动。");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var isSessionInvalid = IsSessionInvalidException(ex);
            context.Fail(
                $"占座任务失败：{ex.Message}",
                isSessionInvalid ? CoordinatorStatusReason.SessionInvalid : CoordinatorStatusReason.TaskFailed);
            activityLogService.Write(LogEntryKind.Error, "Occupy", ex.Message);
            if (isSessionInvalid)
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
        return cookie;
    }
}

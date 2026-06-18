using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class VenueAvailabilityWorkflowRunner(
    ITraceIntApiClient apiClient,
    ICoordinatorEventPublisher coordinatorEventPublisher,
    IActivityLogService activityLogService,
    ISessionState sessionState,
    IVenueState venueState,
    ICoordinatorRuntime runtime)
{
    public async Task RunAsync(
        VenueAvailabilityWatchPlan plan,
        CoordinatorRunContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            context.SetRunning("空座追踪任务已启动。");
            activityLogService.Write(LogEntryKind.Info, "Vacancy", $"开始追踪场馆：{plan.LibraryName}。");

            var cycle = 0;
            var requestCount = 0;
            DateTimeOffset? lastRequestAt = null;
            bool? wasFull = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                cycle++;
                context.UpdateRunningMetrics("空座追踪运行中。", cycle, requestCount, lastRequestAt);
                var cookie = GetCurrentCookieOrThrow();
                var layout = await apiClient.GetLibraryLayoutAsync(cookie, plan.LibraryId, cancellationToken);
                requestCount++;
                lastRequestAt = runtime.Now;
                venueState.CurrentLayout = layout;

                var availableSeats = layout.AvailableSeats;
                if (wasFull == true && availableSeats > 0)
                {
                    var message = $"{plan.LibraryName} 当前有 {availableSeats} 个空座。";
                    activityLogService.Write(LogEntryKind.Success, "Vacancy", message);
                    context.UpdateRunningMetrics(message, cycle, requestCount, lastRequestAt, CoordinatorStatusReason.VenueAvailable);
                    await PublishCoordinatorEventSafelyAsync(
                        new VenueAvailableCoordinatorEvent(plan.LibraryName, availableSeats),
                        "发送场馆空座提醒失败");
                }
                else
                {
                    var stateText = availableSeats == 0
                        ? $"{plan.LibraryName} 当前满座，继续追踪。"
                        : $"{plan.LibraryName} 当前有 {availableSeats} 个空座，等待下一次满座后再提醒。";
                    activityLogService.Write(LogEntryKind.Info, "Vacancy", $"第 {cycle} 次轮询：{stateText}");
                    context.UpdateRunningMetrics(stateText, cycle, requestCount, lastRequestAt);
                }

                wasFull = availableSeats == 0;
                await runtime.DelayAsync(NormalizePollingInterval(plan.PollingInterval), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var isSessionInvalid = SessionAuthFailureDetector.IsSessionInvalidException(ex, sessionState.Session?.Cookie, runtime.Now);
            context.Fail(
                $"空座追踪任务失败：{ex.Message}",
                isSessionInvalid ? CoordinatorStatusReason.SessionInvalid : CoordinatorStatusReason.TaskFailed);
            activityLogService.Write(LogEntryKind.Error, "Vacancy", ex.Message);

            if (isSessionInvalid)
            {
                await PublishCoordinatorEventSafelyAsync(
                    new SessionInvalidCoordinatorEvent("空座追踪", ex.Message),
                    "发送会话失效提醒失败");
                return;
            }

            await PublishCoordinatorEventSafelyAsync(
                new TaskFailedCoordinatorEvent("空座追踪", ex.Message),
                "发送任务失败提醒失败");
        }
    }

    private string GetCurrentCookieOrThrow()
    {
        var cookie = sessionState.Session?.Cookie ?? throw new InvalidOperationException("当前未登录。");
        return cookie;
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

    private static TimeSpan NormalizePollingInterval(TimeSpan interval)
    {
        return interval < TimeSpan.FromSeconds(5)
            ? TimeSpan.FromSeconds(5)
            : interval;
    }
}

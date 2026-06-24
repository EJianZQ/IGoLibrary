using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class GlobalLeakWorkflowRunner(
    ITraceIntApiClient apiClient,
    ICoordinatorEventPublisher coordinatorEventPublisher,
    IActivityLogService activityLogService,
    ISessionState sessionState,
    ICoordinatorRuntime runtime)
{
    public async Task RunAsync(
        GlobalLeakPlan plan,
        CoordinatorRunContext context,
        CancellationToken cancellationToken)
    {
        var cycle = 0;
        var requestCount = 0;
        DateTimeOffset? lastRequestAt = null;

        void MarkRequestSent(string message)
        {
            requestCount++;
            lastRequestAt = runtime.Now;
            context.UpdateRunningMetrics(message, cycle, requestCount, lastRequestAt);
        }

        try
        {
            if (plan.Libraries.Count == 0)
            {
                throw new InvalidOperationException("请至少选择一个扫描场馆。");
            }

            var scanInterval = GlobalLeakStateMachine.NormalizeScanInterval(plan.ScanInterval);
            context.SetRunning("全域捡漏任务已启动。");
            activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"开始扫描 {plan.Libraries.Count} 个场馆，扫描间隔 {scanInterval.TotalSeconds:0} 秒。");

            while (!cancellationToken.IsCancellationRequested)
            {
                cycle++;
                context.UpdateRunningMetrics($"全域捡漏第 {cycle} 轮扫描中。", cycle, requestCount, lastRequestAt);
                var cookie = GetCurrentCookieOrThrow();

                foreach (var target in plan.Libraries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MarkRequestSent($"正在扫描 {target.LibraryName}");
                    var layout = await apiClient.GetLibraryLayoutAsync(
                        cookie,
                        target.LibraryId,
                        cancellationToken);
                    var availableSeats = GlobalLeakStateMachine.GetAvailableSeats(layout);
                    if (availableSeats.Count == 0)
                    {
                        activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"{target.LibraryName} 暂无空座。");
                        continue;
                    }

                    activityLogService.Write(LogEntryKind.Success, "GlobalLeak", $"{target.LibraryName} 发现 {availableSeats.Count} 个空座，开始尝试预约。");
                    foreach (var seat in availableSeats)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var reserved = await TryReserveSeatAsync(
                            cookie,
                            target,
                            seat,
                            MarkRequestSent,
                            cancellationToken);
                        if (!reserved)
                        {
                            continue;
                        }

                        activityLogService.Write(LogEntryKind.Success, "GlobalLeak", $"{target.LibraryName} · {seat.SeatName} 捡漏成功。");
                        context.Complete("已成功捡漏预约到空座。", CoordinatorStatusReason.GlobalLeakSucceeded);
                        _ = PublishCoordinatorEventSafelyAsync(
                            new GlobalLeakSucceededCoordinatorEvent(target.LibraryName, seat.SeatName),
                            "发送全域捡漏成功提醒失败");
                        return;
                    }
                }

                activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"第 {cycle} 轮扫描结束，未发现可成功预约空座。");
                await runtime.DelayAsync(scanInterval, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var isSessionInvalid = IsSessionInvalidException(ex);
            context.Fail(
                $"全域捡漏任务失败：{ex.Message}",
                isSessionInvalid ? CoordinatorStatusReason.SessionInvalid : CoordinatorStatusReason.TaskFailed);
            activityLogService.Write(LogEntryKind.Error, "GlobalLeak", ex.Message);
            if (isSessionInvalid)
            {
                await PublishCoordinatorEventSafelyAsync(
                    new SessionInvalidCoordinatorEvent("全域捡漏扫描", ex.Message),
                    "发送会话失效提醒失败");
                return;
            }

            await PublishCoordinatorEventSafelyAsync(
                new TaskFailedCoordinatorEvent("全域捡漏", ex.Message),
                "发送任务失败提醒失败");
        }
    }

    private async Task<bool> TryReserveSeatAsync(
        string cookie,
        GlobalLeakLibraryTarget target,
        SeatSnapshot seat,
        Action<string> markRequestSent,
        CancellationToken cancellationToken)
    {
        try
        {
            markRequestSent($"正在预约 {target.LibraryName} · {seat.SeatName}");
            var reserved = await apiClient.ReserveSeatAsync(
                cookie,
                target.LibraryId,
                seat.SeatKey,
                cancellationToken);
            if (!reserved)
            {
                activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"{target.LibraryName} · {seat.SeatName} 预约未命中，继续尝试。");
            }

            return reserved;
        }
        catch (Exception ex) when (DirectReservationMissClassifier.TryClassify(ex, out var missKind))
        {
            activityLogService.Write(
                LogEntryKind.Info,
                "GlobalLeak",
                $"{target.LibraryName} · {DirectReservationMissClassifier.GetMessage(missKind, new SeatReference(seat.SeatKey, seat.SeatName))}");
            if (missKind == DirectReservationMissKind.RetryRequested)
            {
                await runtime.DelayAsync(GlobalLeakStateMachine.GetRetryRequestedBackoff(), cancellationToken);
            }

            return false;
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
}

using System.Collections.Frozen;
using IGoLibrary.Ex.Application.Configuration;
using Microsoft.Extensions.Logging;
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
    ICoordinatorRuntime runtime,
    ISettingsService settingsService,
    ILogger<GlobalLeakWorkflowRunner> logger)
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
                throw new InvalidOperationException("请至少选择一个扫描场馆");
            }

            var settings = await settingsService.LoadAsync(cancellationToken);
            var libraryIds = plan.Libraries.Select(static library => library.LibraryId).ToHashSet();
            var blacklist = GlobalLeakSeatBlacklistSettings.Normalize(settings.Tasks.GlobalLeak.BlacklistedSeats)
                .Where(seat => libraryIds.Contains(seat.LibraryId))
                .GroupBy(static seat => seat.LibraryId)
                .ToFrozenDictionary(static group => group.Key,
                    static group => group.Select(static seat => seat.SeatKey).ToFrozenSet(StringComparer.Ordinal));
            activityLogService.Write(LogEntryKind.Info, "GlobalLeak",
                $"本次任务已加载 {blacklist.Values.Sum(static seats => seats.Count)} 个黑名单座位。");
            var scanInterval = GlobalLeakStateMachine.NormalizeScanInterval(plan.ScanInterval);
            context.SetRunning("全域捡漏任务已启动");
            activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"开始扫描 {plan.Libraries.Count} 个场馆，扫描间隔 {scanInterval.TotalSeconds:0} 秒。");

            while (!cancellationToken.IsCancellationRequested)
            {
                cycle++;
                context.UpdateRunningMetrics($"全域捡漏第 {cycle} 轮扫描中", cycle, requestCount, lastRequestAt);
                var cookie = GetCurrentCookieOrThrow();

                foreach (var target in plan.Libraries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MarkRequestSent($"正在扫描 {target.LibraryName}");
                    var layout = await apiClient.GetLibraryLayoutAsync(
                        cookie,
                        target.LibraryId,
                        cancellationToken);
                    blacklist.TryGetValue(target.LibraryId, out var excludedKeys);
                    var availableSeats = GlobalLeakStateMachine.GetAvailableSeats(layout, excludedKeys);
                    var originalAvailableCount = layout.Seats.Count(static seat => seat.IsAvailable);
                    var excludedCount = originalAvailableCount - availableSeats.Count;
                    logger.LogInformation(new EventId(3103, "GlobalLeakBlacklistFiltered"),
                        "全域捡漏座位过滤完成。轮次={Round}，场馆标识={LibraryId}，空座数量={AvailableCount}，排除数量={ExcludedCount}，候选数量={CandidateCount}。",
                        cycle, target.LibraryId, originalAvailableCount, excludedCount, availableSeats.Count);
                    if (excludedCount > 0)
                        activityLogService.Write(LogEntryKind.Info, "GlobalLeak",
                            $"{target.LibraryName} 发现 {originalAvailableCount} 个空座，黑名单排除 {excludedCount} 个，剩余 {availableSeats.Count} 个可尝试座位。");
                    if (availableSeats.Count == 0)
                    {
                        activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"{target.LibraryName} {(excludedCount > 0 ? "空座均已列入黑名单，继续扫描" : "暂无空座")}。");
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
                        context.Complete("已成功捡漏预约到空座", CoordinatorStatusReason.GlobalLeakSucceeded);
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
            activityLogService.Write(LogEntryKind.Error, "GlobalLeak", ex.Message, ex);
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
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"{failureMessage}：{ex.Message}", ex);
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

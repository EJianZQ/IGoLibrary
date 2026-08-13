using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class OccupyReReservationExecutor(
    ITraceIntApiClient apiClient,
    IActivityLogService activityLogService,
    ICoordinatorRuntime runtime) : IOccupyReReservationExecutor
{
    public async Task<OccupyReReservationResult> ExecuteAsync(
        string cookie,
        ReservationInfo reservation,
        OccupySeatPlan plan,
        int maxAttempts,
        Action<string> markRequestSent,
        CancellationToken cancellationToken)
    {
        markRequestSent("正在取消即将到期的预约");
        var cancelled = await apiClient.CancelReservationAsync(cookie, reservation.ReservationToken, cancellationToken);
        if (!cancelled)
        {
            throw new InvalidOperationException("取消预约失败");
        }

        await runtime.DelayAsync(plan.ReReserveDelay, cancellationToken);

        maxAttempts = Math.Max(1, maxAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            bool reserved;
            try
            {
                markRequestSent($"正在进行第 {attempt} 次重新预约");
                reserved = await apiClient.ReserveSeatAsync(
                    cookie,
                    reservation.LibraryId,
                    reservation.SeatKey,
                    cancellationToken);
            }
            catch (Exception ex) when (DirectReservationMissClassifier.TryClassify(ex, out _))
            {
                reserved = false;
                activityLogService.Write(
                    LogEntryKind.Warning,
                    "Occupy",
                    $"第 {attempt} 次重新预约收到可重试响应：{ex.Message}");
            }

            if (reserved)
            {
                if (attempt > 1)
                {
                    activityLogService.Write(LogEntryKind.Success, "Occupy", $"第 {attempt} 次重新预约尝试成功。");
                }

                return new OccupyReReservationResult(true);
            }

            if (attempt >= maxAttempts)
            {
                break;
            }

            activityLogService.Write(LogEntryKind.Warning, "Occupy", $"第 {attempt} 次重新预约失败，1 秒后继续重试。");
            await runtime.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return new OccupyReReservationResult(false);
    }
}

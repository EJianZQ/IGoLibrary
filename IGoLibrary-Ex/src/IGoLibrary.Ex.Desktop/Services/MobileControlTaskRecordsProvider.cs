using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class MobileControlTaskRecordsProvider(
    ITaskLaunchHistoryService historyService,
    TimeProvider timeProvider,
    ILogger<MobileControlTaskRecordsProvider> logger) : IMobileControlTaskRecordsProvider
{
    public async Task<MobileControlTaskRecordsSnapshot> CreateSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var grab = await historyService.GetRecentGrabAsync(cancellationToken);
        var globalLeak = await historyService.GetRecentGlobalLeakAsync(cancellationToken);
        logger.LogDebug(
            "已创建手机端任务记录快照。抢座数量={GrabCount}，全馆捡漏数量={GlobalLeakCount}。",
            grab.Count,
            globalLeak.Count);

        return new MobileControlTaskRecordsSnapshot(
            timeProvider.GetUtcNow(),
            grab.Select(static record => new MobileControlGrabTaskRecordSnapshot(
                record.RecordId,
                FormatRecordedAt(record.RecordedAtUtc),
                record.LibraryName,
                record.Seats.Select(static seat => seat.SeatName).ToArray(),
                GetPollingModeText(record.PollingMode),
                GetReservationStrategyText(record.ReservationStrategy))).ToArray(),
            globalLeak.Select(static record => new MobileControlGlobalLeakTaskRecordSnapshot(
                record.RecordId,
                FormatRecordedAt(record.RecordedAtUtc),
                record.Libraries.Select(static library => new MobileControlGlobalLeakLibrarySnapshot(
                    library.LibraryName,
                    library.Floor)).ToArray(),
                record.ScanInterval.TotalSeconds)).ToArray());
    }

    private static string FormatRecordedAt(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string GetPollingModeText(GrabPollingMode mode)
    {
        return mode switch
        {
            GrabPollingMode.Aggressive => "极限速度",
            GrabPollingMode.Randomized => "随机延迟",
            _ => "延迟 5 秒"
        };
    }

    private static string GetReservationStrategyText(GrabReservationStrategy strategy)
    {
        return strategy switch
        {
            GrabReservationStrategy.ReserveDirectly => "直接发送预约请求",
            _ => "先获取列表判断状态"
        };
    }
}

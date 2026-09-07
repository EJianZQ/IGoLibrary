using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Application.Services;

public sealed class GlobalLeakSeatBlacklistService(
    ISettingsService settingsService,
    IGlobalLeakCoordinator coordinator,
    GlobalLeakConfigurationGate gate,
    IActivityLogService activityLogService,
    ILogger<GlobalLeakSeatBlacklistService> logger) : IGlobalLeakSeatBlacklistService
{
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<SeatReference>>> LoadAsync(
        IReadOnlyList<int> libraryIds, CancellationToken cancellationToken = default)
    {
        var ids = libraryIds.ToHashSet();
        var settings = await settingsService.LoadAsync(cancellationToken);
        var seats = GlobalLeakSeatBlacklistSettings.Normalize(settings.Tasks.GlobalLeak.BlacklistedSeats);
        var result = ids.ToDictionary(id => id, id => (IReadOnlyList<SeatReference>)seats
            .Where(seat => seat.LibraryId == id)
            .Select(static seat => new SeatReference(seat.SeatKey, seat.SeatName)).ToArray());
        logger.LogDebug("已读取全域捡漏黑名单。场馆数量={LibraryCount}，座位数量={SeatCount}。",
            ids.Count, result.Values.Sum(static seats => seats.Count));
        return result;
    }

    public async Task SaveAsync(IReadOnlyDictionary<int, IReadOnlyList<SeatReference>> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Any(static entry => entry.Key <= 0 || entry.Value is null ||
            entry.Value.Any(static seat => seat is null || string.IsNullOrWhiteSpace(seat.SeatKey))))
        {
            throw new ArgumentException("黑名单包含无效的场馆或座位标识", nameof(changes));
        }

        var ids = changes.Keys.ToHashSet();
        var replacement = GlobalLeakSeatBlacklistSettings.Normalize(changes.SelectMany(entry => entry.Value
            .Select(seat => new GlobalLeakBlacklistedSeatSettings(entry.Key, seat.SeatKey, seat.SeatName))).ToArray());
        await gate.EnterAsync(cancellationToken);
        try
        {
            var status = coordinator.GetStatus();
            if (status.IsActive)
            {
                logger.LogInformation(new EventId(3101, "GlobalLeakBlacklistSaveRejected"),
                    "任务处于活动状态，拒绝修改全域捡漏黑名单。任务状态={TaskState}。", status.State);
                throw new InvalidOperationException("请先停止全域捡漏任务再修改黑名单");
            }

            var changed = false;
            var added = 0;
            var removed = 0;
            await settingsService.UpdateAsync(current =>
            {
                var previous = GlobalLeakSeatBlacklistSettings.Normalize(current.Tasks.GlobalLeak.BlacklistedSeats);
                var next = GlobalLeakSeatBlacklistSettings.Normalize(previous
                    .Where(seat => !ids.Contains(seat.LibraryId)).Concat(replacement).ToArray());
                if (previous.SequenceEqual(next)) return current;

                changed = true;
                var beforeKeys = previous.Select(static seat => (seat.LibraryId, seat.SeatKey)).ToHashSet();
                var afterKeys = next.Select(static seat => (seat.LibraryId, seat.SeatKey)).ToHashSet();
                added = afterKeys.Except(beforeKeys).Count();
                removed = beforeKeys.Except(afterKeys).Count();
                return current with { Tasks = current.Tasks with
                {
                    GlobalLeak = current.Tasks.GlobalLeak with { BlacklistedSeats = next }
                }};
            }, cancellationToken);

            if (changed)
            {
                logger.LogInformation(new EventId(3102, "GlobalLeakBlacklistSaved"),
                    "全域捡漏黑名单已保存。场馆数量={LibraryCount}，新增数量={AddedCount}，移除数量={RemovedCount}。",
                    ids.Count, added, removed);
                activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"黑名单已保存，新增 {added} 个座位，移除 {removed} 个座位。");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("保存全域捡漏黑名单已取消。");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "保存全域捡漏黑名单失败。场馆数量={LibraryCount}。", ids.Count);
            throw;
        }
        finally { gate.Exit(); }
    }
}

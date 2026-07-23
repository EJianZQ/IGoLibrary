using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Application.Services;

public sealed class TaskLaunchService(
    IGrabSeatCoordinator grabSeatCoordinator,
    IGlobalLeakCoordinator globalLeakCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    ITaskLaunchHistoryService historyService,
    IActivityLogService activityLogService,
    ILogger<TaskLaunchService> logger) : ITaskLaunchService
{
    public async Task StartGrabAsync(
        GrabSeatPlan plan,
        TaskLaunchSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await StartAsync(
            "抢座",
            source,
            token => grabSeatCoordinator.StartAsync(plan, token),
            source == TaskLaunchSource.Desktop
                ? token => historyService.RecordGrabAsync(plan, token)
                : null,
            cancellationToken);
    }

    public async Task StartGlobalLeakAsync(
        GlobalLeakPlan plan,
        TaskLaunchSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await StartAsync(
            "全域捡漏",
            source,
            token => globalLeakCoordinator.StartAsync(plan, token),
            source == TaskLaunchSource.Desktop
                ? token => historyService.RecordGlobalLeakAsync(plan, token)
                : null,
            cancellationToken);
    }

    public Task StartOccupyAsync(
        OccupySeatPlan plan,
        TaskLaunchSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return StartAsync(
            "占座",
            source,
            token => occupySeatCoordinator.StartAsync(plan, token),
            recordHistoryAsync: null,
            cancellationToken);
    }

    private async Task StartAsync(
        string displayName,
        TaskLaunchSource source,
        Func<CancellationToken, Task> startAsync,
        Func<CancellationToken, Task<TaskLaunchHistorySaveResult>>? recordHistoryAsync,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "任务启动来源无效");
        }

        var sourceText = source == TaskLaunchSource.Desktop ? "电脑端" : "手机端";
        activityLogService.Write(LogEntryKind.Info, "TaskLaunch", $"收到{sourceText}{displayName}启动请求。");

        try
        {
            await startAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "TaskLaunch", $"{sourceText}{displayName}启动请求被拒绝：{ex.Message}");
            throw;
        }

        activityLogService.Write(LogEntryKind.Info, "TaskLaunch", $"{sourceText}{displayName}启动请求已被接受。");
        if (recordHistoryAsync is null)
        {
            return;
        }

        try
        {
            var result = await recordHistoryAsync(cancellationToken);
            var operation = result.RefreshedExisting ? "已更新并提升" : "已新增";
            var pruneText = result.PrunedCount > 0 ? $"，已清理 {result.PrunedCount} 条旧记录" : string.Empty;
            activityLogService.Write(
                LogEntryKind.Info,
                "TaskLaunchHistory",
                $"{displayName}手机控制记录{operation}{pruneText}。");
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "已接受 {TaskKind} 请求（来源：{LaunchSource}），但任务启动历史记录持久化失败。",
                displayName,
                source);
            activityLogService.Write(
                LogEntryKind.Warning,
                "TaskLaunchHistory",
                $"保存{displayName}手机控制记录失败，任务不受影响：{ex.Message}");
        }
    }
}

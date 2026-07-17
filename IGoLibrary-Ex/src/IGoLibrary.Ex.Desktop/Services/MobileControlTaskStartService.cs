using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.AspNetCore.Http;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class MobileControlTaskStartService(
    ITaskLaunchHistoryService historyService,
    ITaskLaunchService taskLaunchService,
    IGrabSeatCoordinator grabSeatCoordinator,
    IGlobalLeakCoordinator globalLeakCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    ISessionState sessionState,
    IReservationState reservationState,
    ShellWorkflowState workflowState,
    IMobileControlOccupyPlanProvider occupyPlanProvider,
    IActivityLogService activityLogService,
    TimeProvider timeProvider) : IMobileControlTaskStartService
{
    public async Task<MobileControlActionResult> StartTaskAsync(
        string taskKind,
        string? recordId,
        CancellationToken cancellationToken = default)
    {
        var normalizedKind = taskKind?.Trim();
        if (normalizedKind is not ("grab" or "globalLeak" or "occupy"))
        {
            return Failure("未知任务类型", StatusCodes.Status400BadRequest);
        }

        var session = sessionState.Session;
        if (session is null)
        {
            return Reject(normalizedKind, "当前未登录，请先在电脑端登录", StatusCodes.Status409Conflict);
        }

        if (SessionAuthFailureDetector.IsCookieExpired(session.Cookie, timeProvider.GetUtcNow()))
        {
            return Reject(normalizedKind, "Cookie 已过期，请先刷新 Cookie", StatusCodes.Status409Conflict);
        }

        return normalizedKind switch
        {
            "grab" => await StartGrabAsync(recordId, cancellationToken),
            "globalLeak" => await StartGlobalLeakAsync(recordId, cancellationToken),
            _ => await StartOccupyAsync(cancellationToken)
        };
    }

    private async Task<MobileControlActionResult> StartGrabAsync(
        string? recordId,
        CancellationToken cancellationToken)
    {
        if (IsActive(grabSeatCoordinator.GetStatus()))
        {
            return Reject("grab", "抢座任务已在运行", StatusCodes.Status409Conflict);
        }

        if (!IsValidRecordId(recordId))
        {
            return Failure("抢座记录 ID 无效", StatusCodes.Status400BadRequest);
        }

        var record = await historyService.GetGrabAsync(recordId!, cancellationToken);
        if (record is null)
        {
            return Reject("grab", "抢座记录不存在或已被清理", StatusCodes.Status404NotFound);
        }

        var plan = new GrabSeatPlan(
            record.LibraryId,
            record.LibraryName,
            record.Seats.ToArray(),
            record.PollingMode,
            GrabPollingStrategyFactory.FromMode(record.PollingMode),
            ScheduledStart: null,
            ReservationStrategy: record.ReservationStrategy);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await taskLaunchService.StartGrabAsync(plan, TaskLaunchSource.MobileControl, CancellationToken.None);
        }
        catch (TaskLaunchConflictException ex)
        {
            return Reject("grab", ex.Message, StatusCodes.Status409Conflict);
        }

        return Accepted("grab", "抢座任务启动请求已被电脑端接受");
    }

    private async Task<MobileControlActionResult> StartGlobalLeakAsync(
        string? recordId,
        CancellationToken cancellationToken)
    {
        if (IsActive(globalLeakCoordinator.GetStatus()))
        {
            return Reject("globalLeak", "全域捡漏任务已在运行", StatusCodes.Status409Conflict);
        }

        if (!IsValidRecordId(recordId))
        {
            return Failure("全域捡漏记录 ID 无效", StatusCodes.Status400BadRequest);
        }

        var record = await historyService.GetGlobalLeakAsync(recordId!, cancellationToken);
        if (record is null)
        {
            return Reject("globalLeak", "全域捡漏记录不存在或已被清理", StatusCodes.Status404NotFound);
        }

        var plan = new GlobalLeakPlan(record.Libraries.ToArray(), record.ScanInterval);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await taskLaunchService.StartGlobalLeakAsync(plan, TaskLaunchSource.MobileControl, CancellationToken.None);
        }
        catch (TaskLaunchConflictException ex)
        {
            return Reject("globalLeak", ex.Message, StatusCodes.Status409Conflict);
        }

        return Accepted("globalLeak", "全域捡漏任务启动请求已被电脑端接受");
    }

    private async Task<MobileControlActionResult> StartOccupyAsync(CancellationToken cancellationToken)
    {
        if (IsActive(occupySeatCoordinator.GetStatus()))
        {
            return Reject("occupy", "占座任务已在运行", StatusCodes.Status409Conflict);
        }

        if (workflowState.CurrentReservation is null && reservationState.CurrentReservation is null)
        {
            return Reject("occupy", "当前没有可续占的预约", StatusCodes.Status409Conflict);
        }

        var plan = await occupyPlanProvider.CreatePlanAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await taskLaunchService.StartOccupyAsync(plan, TaskLaunchSource.MobileControl, CancellationToken.None);
        }
        catch (TaskLaunchConflictException ex)
        {
            return Reject("occupy", ex.Message, StatusCodes.Status409Conflict);
        }

        return Accepted("occupy", "占座任务启动请求已被电脑端接受");
    }

    private MobileControlActionResult Accepted(string taskKind, string message)
    {
        activityLogService.Write(LogEntryKind.Info, "MobileControl", $"手机端{GetTaskName(taskKind)}启动请求已被接受。");
        return new MobileControlActionResult(true, message, StatusCodes.Status200OK);
    }

    private MobileControlActionResult Reject(string taskKind, string message, int statusCode)
    {
        activityLogService.Write(LogEntryKind.Warning, "MobileControl", $"手机端{GetTaskName(taskKind)}启动请求被拒绝：{message}。");
        return Failure(message, statusCode);
    }

    private static MobileControlActionResult Failure(string message, int statusCode)
    {
        return new MobileControlActionResult(false, message, statusCode);
    }

    private static bool IsValidRecordId(string? recordId)
    {
        return recordId is { Length: 32 } && Guid.TryParseExact(recordId, "N", out _);
    }

    private static bool IsActive(CoordinatorStatus status)
    {
        return status.State is CoordinatorTaskState.Starting
            or CoordinatorTaskState.Running
            or CoordinatorTaskState.Stopping;
    }

    private static string GetTaskName(string taskKind)
    {
        return taskKind switch
        {
            "grab" => "抢座",
            "globalLeak" => "全域捡漏",
            _ => "占座"
        };
    }
}

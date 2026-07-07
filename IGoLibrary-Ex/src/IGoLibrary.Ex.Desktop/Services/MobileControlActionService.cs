using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.AspNetCore.Http;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IMobileControlActionService
{
    Task<MobileControlActionResult> CancelTaskAsync(
        string taskKind,
        CancellationToken cancellationToken = default);

    Task<MobileControlActionResult> CancelCurrentReservationAsync(
        CancellationToken cancellationToken = default);

    Task<MobileControlActionResult> RefreshCookieFromLinkAsync(
        string linkText,
        CancellationToken cancellationToken = default);
}

public sealed record MobileControlActionResult(
    bool Success,
    string Message,
    int StatusCode);

public sealed record MobileControlActionResponse(
    bool Success,
    string Message);

public sealed class MobileControlActionService(
    IGrabSeatCoordinator grabSeatCoordinator,
    IGlobalLeakCoordinator globalLeakCoordinator,
    ITomorrowReservationCoordinator tomorrowReservationCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    IReservationWorkflowService reservationWorkflowService,
    IReservationState reservationState,
    ShellWorkflowState workflowState,
    IMobileControlCookieRefreshHandler cookieRefreshHandler,
    IActivityLogService activityLogService) : IMobileControlActionService
{
    private readonly SemaphoreSlim _reservationCancelGate = new(1, 1);
    private readonly SemaphoreSlim _cookieRefreshGate = new(1, 1);

    public async Task<MobileControlActionResult> CancelTaskAsync(
        string taskKind,
        CancellationToken cancellationToken = default)
    {
        var descriptor = ResolveTaskDescriptor(taskKind);
        if (descriptor is null)
        {
            return ConflictOrFailure(
                false,
                "未知任务类型",
                StatusCodes.Status400BadRequest);
        }

        var status = descriptor.GetStatus();
        if (!IsActive(status))
        {
            return ConflictOrFailure(
                false,
                $"{descriptor.DisplayName}任务当前未运行",
                StatusCodes.Status409Conflict);
        }

        await descriptor.StopAsync(cancellationToken);
        activityLogService.Write(LogEntryKind.Info, "MobileControl", $"手机端已请求取消{descriptor.DisplayName}任务。");
        return new MobileControlActionResult(
            true,
            $"已取消{descriptor.DisplayName}任务",
            StatusCodes.Status200OK);
    }

    public async Task<MobileControlActionResult> CancelCurrentReservationAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _reservationCancelGate.WaitAsync(0, cancellationToken))
        {
            return ConflictOrFailure(
                false,
                "当前已有取消预约操作正在执行",
                StatusCodes.Status409Conflict);
        }

        try
        {
            var reservation = workflowState.CurrentReservation ?? reservationState.CurrentReservation;
            if (reservation is null)
            {
                return ConflictOrFailure(
                    false,
                    "当前没有可取消的预约",
                    StatusCodes.Status409Conflict);
            }

            var result = await reservationWorkflowService.CancelCurrentReservationAsync(
                reservation,
                stopOccupyFirst: IsActive(occupySeatCoordinator.GetStatus()),
                cancellationToken);

            if (!result.Succeeded)
            {
                return ConflictOrFailure(
                    false,
                    ResolveReservationFailureMessage(result),
                    ResolveReservationFailureStatusCode(result));
            }

            workflowState.CurrentReservation = result.Reservation;
            reservationState.CurrentReservation = result.Reservation;
            activityLogService.Write(LogEntryKind.Info, "MobileControl", "手机端已取消当前预约。");
            return new MobileControlActionResult(
                true,
                "已取消当前预约",
                StatusCodes.Status200OK);
        }
        finally
        {
            _reservationCancelGate.Release();
        }
    }

    public async Task<MobileControlActionResult> RefreshCookieFromLinkAsync(
        string linkText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(linkText))
        {
            return ConflictOrFailure(
                false,
                "没有收到授权链接，请先粘贴链接",
                StatusCodes.Status400BadRequest);
        }

        if (!await _cookieRefreshGate.WaitAsync(0, cancellationToken))
        {
            return ConflictOrFailure(
                false,
                "当前已有 Cookie 刷新操作正在执行",
                StatusCodes.Status409Conflict);
        }

        try
        {
            var result = await cookieRefreshHandler.RefreshCookieFromLinkAsync(
                linkText.Trim(),
                cancellationToken);
            if (result.Authenticated)
            {
                activityLogService.Write(LogEntryKind.Info, "MobileControl", "手机端已刷新 Cookie。");
                return new MobileControlActionResult(
                    true,
                    result.Message,
                    StatusCodes.Status200OK);
            }

            return ConflictOrFailure(false, result.Message, ResolveCookieRefreshFailureStatusCode(result.Status));
        }
        finally
        {
            _cookieRefreshGate.Release();
        }
    }

    private MobileControlTaskDescriptor? ResolveTaskDescriptor(string taskKind)
    {
        return taskKind.Trim() switch
        {
            "grab" => new MobileControlTaskDescriptor(
                "抢座",
                grabSeatCoordinator.GetStatus,
                grabSeatCoordinator.StopAsync),
            "globalLeak" => new MobileControlTaskDescriptor(
                "全域捡漏",
                globalLeakCoordinator.GetStatus,
                globalLeakCoordinator.StopAsync),
            "tomorrow" => new MobileControlTaskDescriptor(
                "明日预约",
                tomorrowReservationCoordinator.GetStatus,
                tomorrowReservationCoordinator.StopAsync),
            "occupy" => new MobileControlTaskDescriptor(
                "占座",
                occupySeatCoordinator.GetStatus,
                occupySeatCoordinator.StopAsync),
            _ => null
        };
    }

    private static MobileControlActionResult ConflictOrFailure(
        bool success,
        string message,
        int statusCode)
    {
        return new MobileControlActionResult(success, message, statusCode);
    }

    private static string ResolveReservationFailureMessage(ReservationOperationResult result)
    {
        return string.IsNullOrWhiteSpace(result.FailureMessage)
            ? "取消当前预约失败，请稍后重试"
            : result.FailureMessage;
    }

    private static int ResolveReservationFailureStatusCode(ReservationOperationResult result)
    {
        if (!result.HasSession)
        {
            return StatusCodes.Status409Conflict;
        }

        return result.RemoteSucceeded == false
            ? StatusCodes.Status502BadGateway
            : StatusCodes.Status500InternalServerError;
    }

    private static int ResolveCookieRefreshFailureStatusCode(SessionCookieLinkParseStatus status)
    {
        return status switch
        {
            SessionCookieLinkParseStatus.InvalidLink => StatusCodes.Status400BadRequest,
            SessionCookieLinkParseStatus.DuplicateCode => StatusCodes.Status409Conflict,
            SessionCookieLinkParseStatus.AuthenticationFailed => StatusCodes.Status422UnprocessableEntity,
            SessionCookieLinkParseStatus.CookieFetched => StatusCodes.Status422UnprocessableEntity,
            SessionCookieLinkParseStatus.FetchFailed => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    private static bool IsActive(CoordinatorStatus status)
    {
        return status.State is CoordinatorTaskState.Starting
            or CoordinatorTaskState.Running
            or CoordinatorTaskState.Stopping;
    }

    private sealed record MobileControlTaskDescriptor(
        string DisplayName,
        Func<CoordinatorStatus> GetStatus,
        Func<CancellationToken, Task> StopAsync);
}

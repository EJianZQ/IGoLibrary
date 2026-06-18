using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class ReservationWorkflowService(
    ISessionService sessionService,
    ITraceIntApiClient apiClient,
    IOccupySeatCoordinator occupySeatCoordinator,
    IActivityLogService activityLogService,
    IReservationState reservationState) : IReservationWorkflowService
{
    public async Task<ReservationOperationResult> RefreshReservationAsync(CancellationToken cancellationToken = default)
    {
        var session = sessionService.CurrentSession;
        if (session is null)
        {
            return new ReservationOperationResult(
                Succeeded: true,
                Reservation: null,
                HasSession: false);
        }

        var reservation = await apiClient.GetReservationInfoAsync(session.Cookie, cancellationToken);
        reservationState.CurrentReservation = reservation;
        return new ReservationOperationResult(
            Succeeded: true,
            Reservation: reservation);
    }

    public async Task<ReservationOperationResult> CancelCurrentReservationAsync(
        ReservationInfo reservation,
        bool stopOccupyFirst,
        CancellationToken cancellationToken = default)
    {
        var session = sessionService.CurrentSession;
        if (session is null)
        {
            return new ReservationOperationResult(
                Succeeded: false,
                Reservation: reservation,
                HasSession: false,
                FailureMessage: "当前会话已失效，请重新授权后再操作。");
        }

        if (stopOccupyFirst)
        {
            try
            {
                await occupySeatCoordinator.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                activityLogService.Write(LogEntryKind.Warning, "Occupy", $"取消预约前停止占座失败：{ex.Message}");
            }
        }

        var cancelled = await apiClient.CancelReservationAsync(session.Cookie, reservation.ReservationToken, cancellationToken);
        if (cancelled)
        {
            reservationState.CurrentReservation = null;
            return new ReservationOperationResult(
                Succeeded: true,
                Reservation: null);
        }

        reservationState.CurrentReservation = reservation;
        return new ReservationOperationResult(
            Succeeded: false,
            Reservation: reservation,
            RemoteSucceeded: false,
            FailureMessage: "接口未返回成功结果，请稍后重试。");
    }
}

using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class RemoteCheckInPageViewModel
{
    [RelayCommand]
    private async Task SignAsync()
    {
        if (!await TryEnterOperationAsync())
        {
            return;
        }

        try
        {
            var library = _workflowState.LockedLibrary
                ?? throw new InvalidOperationException("请先锁定场馆");
            if (!IsProfileSaved || _savedProfile is null || _savedProfile.LibraryId != library.LibraryId)
            {
                throw new InvalidOperationException("请先保存当前场馆的 Beacon 与 GCJ-02 坐标配置");
            }

            var reservationResult = await _reservationWorkflowService.RefreshReservationAsync();
            if (!reservationResult.HasSession)
            {
                throw new InvalidOperationException("普通登录会话已失效，请重新登录");
            }

            _workflowState.CurrentReservation = reservationResult.Reservation;
            if (reservationResult.Reservation is null)
            {
                throw new InvalidOperationException("当前没有可签到的预约");
            }

            if (reservationResult.Reservation.LibraryId != library.LibraryId)
            {
                throw new InvalidOperationException("当前预约与锁定场馆不一致，请切换到预约所在场馆后再签到");
            }

            var result = await _workflowService.SignAsync(new RemoteCheckInSignPlan(
                library.LibraryId,
                library.Name,
                _savedProfile.BeaconUuid,
                _savedProfile.Major!.Value,
                _savedProfile.Minor!.Value,
                _savedProfile.Latitude!.Value,
                _savedProfile.Longitude!.Value));

            var actualLibrary = string.IsNullOrWhiteSpace(result.LibraryName) ? library.Name : result.LibraryName;
            var actualSeat = string.IsNullOrWhiteSpace(result.SeatName)
                ? reservationResult.Reservation.SeatName
                : result.SeatName;
            LastResultText = BuildResultText(result, actualLibrary, actualSeat);
            _activityLogService.Write(
                LogEntryKind.Success,
                "RemoteCheckIn",
                $"远程签到成功：{actualLibrary} / {actualSeat}。");

            if (result.LibraryId is int actualLibraryId && actualLibraryId != library.LibraryId)
            {
                await _notificationService.ShowWarningAsync(
                    "签到已成功，但场馆不一致",
                    $"服务端返回的实际场馆为 {actualLibrary}，请立即核对预约状态");
            }
            else
            {
                await _notificationService.ShowSuccessAsync("远程签到成功", $"{actualLibrary} · {actualSeat}");
            }

            await RefreshReservationAfterSuccessAsync();
        }
        catch (RemoteCheckInOutcomeUnknownException ex)
        {
            LastResultText = ex.Message;
            _activityLogService.Write(
                LogEntryKind.Warning,
                "RemoteCheckIn",
                "远程签到结果未知，已阻止自动重试。",
                ex);
            await _notificationService.ShowWarningAsync("签到结果未知", ex.Message);
        }
        catch (Exception ex)
        {
            HandleSessionFailure(ex);
            LastResultText = $"签到失败：{ex.Message}";
            _activityLogService.Write(
                LogEntryKind.Error,
                "RemoteCheckIn",
                $"远程签到失败：{ex.Message}",
                ex);
            await _notificationService.ShowWarningAsync("远程签到失败", ex.Message);
        }
        finally
        {
            ExitOperation();
        }
    }

    private async Task RefreshReservationAfterSuccessAsync()
    {
        try
        {
            var result = await _reservationWorkflowService.RefreshReservationAsync();
            if (result.HasSession && result.Succeeded)
            {
                _workflowState.CurrentReservation = result.Reservation;
            }
        }
        catch (Exception ex)
        {
            _activityLogService.Write(
                LogEntryKind.Warning,
                "RemoteCheckIn",
                $"签到成功后刷新预约状态失败：{ex.Message}",
                ex);
        }
    }

    private static string BuildResultText(RemoteCheckInResult result, string library, string seat)
    {
        var signedAt = result.SignedAt is { } signed ? signed.ToString("yyyy-MM-dd HH:mm:ss") : "--";
        var expiration = result.ExpirationTime is { } expires ? expires.ToString("HH:mm:ss") : "--";
        return $"{result.Message} · {library} · {seat} · 签到 {signedAt} · 有效至 {expiration}";
    }
}

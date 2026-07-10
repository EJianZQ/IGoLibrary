using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class RemoteCheckInPageViewModel
{
    private int _expirationCheckInProgress;

    public async Task<bool> TryAutoParseClipboardLinkAsync(string clipboardText)
    {
        AuthorizationLinkText = clipboardText.Trim();
        var result = await AuthorizeFromLinkAsync(AuthorizationLinkText, showNotifications: true);
        return result.Authenticated;
    }

    [RelayCommand]
    private async Task AuthorizeFromLinkAsync()
    {
        await AuthorizeFromLinkAsync(AuthorizationLinkText, showNotifications: true);
    }

    [RelayCommand]
    private async Task StartRemoteCheckInLanRelayAsync()
    {
        await _lanCookieRelay.StartSessionAsync(
            LanAuthLinkRelayPurpose.RemoteCheckIn,
            link => AuthorizeFromLinkAsync(link, showNotifications: false));
    }

    [RelayCommand]
    private async Task ClearAuthorizationAsync()
    {
        if (!await TryEnterOperationAsync())
        {
            return;
        }

        try
        {
            await _workflowService.ClearSessionAsync();
            HasRemoteCheckInSession = false;
            UpdateAuthorizationExpirationPresentation(null);
            AllowedBeaconUuids.Clear();
            AccountSummaryText = "等待获取签到账号信息";
            AuthorizationStatusText = "签到授权已清除";
            DeviceStatusText = "请重新扫码获取签到授权";
        }
        catch (Exception ex)
        {
            HasRemoteCheckInSession = _workflowService.CurrentSession is not null;
            if (!HasRemoteCheckInSession)
            {
                UpdateAuthorizationExpirationPresentation(null);
            }

            AuthorizationStatusText = HasRemoteCheckInSession
                ? $"清除签到授权失败，原授权仍保留：{ex.Message}"
                : $"清除本地签到授权时发生错误：{ex.Message}";
            await _notificationService.ShowWarningAsync("清除签到授权失败", ex.Message);
        }
        finally
        {
            ExitOperation();
        }
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        if (!await TryEnterOperationAsync())
        {
            return;
        }

        try
        {
            var info = await _workflowService.GetDeviceInfoAsync();
            ApplyDeviceInfo(info);
            AuthorizationStatusText = "签到授权已通过服务端验证";
        }
        catch (Exception ex)
        {
            HandleSessionFailure(ex);
            await _notificationService.ShowWarningAsync("刷新签到信标失败", ex.Message);
        }
        finally
        {
            ExitOperation();
        }
    }

    private async Task<LanCookieRelayLinkSubmitResult> AuthorizeFromLinkAsync(
        string? linkText,
        bool showNotifications)
    {
        if (!CodeLinkParser.TryExtractCode(linkText, out var code))
        {
            const string message = "未能从链接中提取 32 位 code";
            if (showNotifications)
            {
                await _notificationService.ShowWarningAsync("链接无效", message);
            }

            return new LanCookieRelayLinkSubmitResult(false, message);
        }

        if (!_oauthCodeConsumptionRegistry.TryReserve(code))
        {
            const string message = "该授权链接已被普通登录或远程签到处理，请重新扫码获取新链接";
            if (showNotifications)
            {
                await _notificationService.ShowWarningAsync("链接已使用", message);
            }

            return new LanCookieRelayLinkSubmitResult(false, message);
        }

        var consumed = false;
        var hadPreviousSession = _workflowService.CurrentSession is not null;
        if (!await TryEnterOperationAsync())
        {
            _oauthCodeConsumptionRegistry.Complete(code, false);
            return new LanCookieRelayLinkSubmitResult(false, "已有签到操作正在执行，请稍后重试");
        }

        try
        {
            var authorization = await _workflowService.AuthorizeFromCodeAsync(
                code,
                RememberRemoteCheckInSession);
            consumed = true;
            HasRemoteCheckInSession = true;
            UpdateAuthorizationExpirationPresentation(authorization.Session);
            if (authorization.DeviceInfo is not null)
            {
                ApplyDeviceInfo(authorization.DeviceInfo);
                AuthorizationStatusText = "签到授权已通过服务端验证";
            }
            else
            {
                AllowedBeaconUuids.Clear();
                AccountSummaryText = "签到授权已更新，请刷新信标确认账号";
                AuthorizationStatusText = "已获取签到授权，等待刷新信标验证";
                DeviceStatusText = $"签到授权已获取，但刷新信标失败：{authorization.DeviceRefreshWarning}";
            }

            AuthorizationLinkText = string.Empty;
            if (showNotifications)
            {
                var storageMessage = RememberRemoteCheckInSession
                    ? "签到授权已写入系统安全凭据存储，未写入日志或设置数据库"
                    : "签到授权仅保存在本次运行内存中，旧的持久化签到授权已清理";
                await _notificationService.ShowSuccessAsync("签到授权已获取", storageMessage);
            }

            return new LanCookieRelayLinkSubmitResult(true, "签到授权已获取，请返回电脑确认信标配置");
        }
        catch (RemoteCheckInAuthorizationException ex)
        {
            consumed = ex.OAuthCodeConsumed;
            HasRemoteCheckInSession = _workflowService.CurrentSession is not null;
            AuthorizationStatusText = hadPreviousSession && HasRemoteCheckInSession
                ? $"重新授权失败，已保留原签到授权：{ex.Message}"
                : $"获取签到授权失败：{ex.Message}";
            if (!HasRemoteCheckInSession)
            {
                UpdateAuthorizationExpirationPresentation(null);
                AccountSummaryText = "等待重新获取签到账号信息";
                AllowedBeaconUuids.Clear();
            }

            if (showNotifications)
            {
                await _notificationService.ShowWarningAsync("获取签到授权失败", ex.Message);
            }

            return new LanCookieRelayLinkSubmitResult(false, ex.Message);
        }
        catch (Exception ex)
        {
            HasRemoteCheckInSession = _workflowService.CurrentSession is not null;
            if (!HasRemoteCheckInSession)
            {
                UpdateAuthorizationExpirationPresentation(null);
            }

            AuthorizationStatusText = hadPreviousSession && HasRemoteCheckInSession
                ? $"重新授权失败，已保留原签到授权：{ex.Message}"
                : $"获取签到授权失败：{ex.Message}";

            if (showNotifications)
            {
                await _notificationService.ShowWarningAsync("获取签到授权失败", ex.Message);
            }

            return new LanCookieRelayLinkSubmitResult(false, ex.Message);
        }
        finally
        {
            _oauthCodeConsumptionRegistry.Complete(code, consumed);
            ExitOperation();
        }
    }

    public void QueueAuthorizationExpirationCheck(DateTimeOffset currentTime)
    {
        if (!HasRemoteCheckInSession ||
            _workflowService.CurrentSession?.ExpiresAt is not { } expiresAt ||
            expiresAt > currentTime ||
            Interlocked.CompareExchange(ref _expirationCheckInProgress, 1, 0) != 0)
        {
            return;
        }

        _ = ClearExpiredAuthorizationIfNeededAsync();
    }

    private async Task ClearExpiredAuthorizationIfNeededAsync()
    {
        var enteredOperation = false;
        try
        {
            enteredOperation = await TryEnterOperationAsync();
            if (!enteredOperation)
            {
                return;
            }

            if (!await _workflowService.ClearExpiredSessionAsync())
            {
                return;
            }

            HasRemoteCheckInSession = false;
            AuthorizationStatusText = "签到授权已到期，请重新扫码授权";
            AuthorizationExpirationText = "签到授权到期时间：已到期";
            AccountSummaryText = "等待重新获取签到账号信息";
            DeviceStatusText = "请重新扫码获取签到授权";
            AllowedBeaconUuids.Clear();
            await _notificationService.ShowWarningAsync("签到授权已到期", "远程签到授权已自动取消，请重新扫码授权");
        }
        catch (Exception ex)
        {
            AuthorizationStatusText = $"签到授权到期清理失败，将自动重试：{ex.Message}";
            _activityLogService.Write(
                LogEntryKind.Warning,
                "RemoteCheckIn",
                $"自动清理到期签到授权失败：{ex.Message}");
        }
        finally
        {
            if (enteredOperation)
            {
                ExitOperation();
            }

            Volatile.Write(ref _expirationCheckInProgress, 0);
        }
    }

    private void HandleSessionFailure(Exception exception)
    {
        if (exception is RemoteCheckInApiException { IsSessionInvalid: true } ||
            _workflowService.CurrentSession is null)
        {
            var expired = exception is RemoteCheckInSessionExpiredException;
            HasRemoteCheckInSession = false;
            if (expired)
            {
                AuthorizationExpirationText = "签到授权到期时间：已到期";
            }
            else
            {
                UpdateAuthorizationExpirationPresentation(null);
            }

            AuthorizationStatusText = expired
                ? "签到授权已到期，请重新扫码授权"
                : "签到授权已失效，请重新扫码";
            AccountSummaryText = "等待重新获取签到账号信息";
            AllowedBeaconUuids.Clear();
        }
    }

    private static string BuildAccountSummary(RemoteCheckInUserSummary user)
    {
        var displayName = !string.IsNullOrWhiteSpace(user.StudentName)
            ? user.StudentName
            : user.Nickname;
        var studentNumber = MaskStudentNumber(user.StudentNumber);
        var parts = new[] { displayName, user.School, studentNumber }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(" · ", parts);
    }

    private static string MaskStudentNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 4
            ? new string('*', value.Length)
            : $"{value[..2]}****{value[^2..]}";
    }
}

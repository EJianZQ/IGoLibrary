using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class RemoteCheckInWorkflowService(
    IRemoteCheckInApiClient apiClient,
    ICredentialStore credentialStore,
    IRemoteCheckInSessionState sessionState,
    IActivityLogService activityLogService,
    TimeProvider timeProvider) : IRemoteCheckInWorkflowService
{
    public RemoteCheckInSessionCredentials? CurrentSession => sessionState.RemoteCheckInSession;

    public async Task<RemoteCheckInSessionCredentials?> RestoreAsync(CancellationToken cancellationToken = default)
    {
        RemoteCheckInSessionCredentials? stored;
        try
        {
            stored = await credentialStore.LoadRemoteCheckInSessionAsync(cancellationToken);
        }
        catch (JsonException)
        {
            await ClearSessionAsync(cancellationToken);
            activityLogService.Write(LogEntryKind.Warning, "RemoteCheckIn", "本地签到授权已损坏，已自动清理");
            return null;
        }

        if (stored is null)
        {
            return null;
        }

        if (!RemoteCheckInSessionTokenValidator.TryNormalize(stored.Token, out var storedToken))
        {
            await ClearSessionAsync(cancellationToken);
            activityLogService.Write(LogEntryKind.Warning, "RemoteCheckIn", "本地签到授权格式无效，已自动清理");
            return null;
        }

        if (IsExpired(stored))
        {
            await ClearSessionAsync(cancellationToken);
            activityLogService.Write(LogEntryKind.Warning, "RemoteCheckIn", "本地签到授权已到期，已自动清理");
            return null;
        }

        var restored = stored with
        {
            Token = storedToken,
            CanAutoRestore = true
        };
        sessionState.RemoteCheckInSession = restored;
        activityLogService.Write(LogEntryKind.Info, "RemoteCheckIn", "已加载本地保存的签到授权，等待服务端验证");
        return restored;
    }

    public async Task<RemoteCheckInAuthorizationResult> AuthorizeFromCodeAsync(
        string code,
        bool remember,
        CancellationToken cancellationToken = default)
    {
        var exchange = await apiClient.ExchangeOAuthCodeAsync(code, cancellationToken);
        if (!RemoteCheckInSessionTokenValidator.TryNormalize(exchange.Token, out var token))
        {
            throw new RemoteCheckInAuthorizationException(
                "签到接口返回了无效的 wechatSESS_ID",
                isSessionInvalid: true);
        }

        var now = timeProvider.GetUtcNow();
        if (exchange.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            throw new RemoteCheckInAuthorizationException(
                "签到接口返回的授权已到期，请重新扫码获取新的授权链接",
                isSessionInvalid: true);
        }

        var session = new RemoteCheckInSessionCredentials(
            token,
            now,
            remember,
            exchange.ExpiresAt?.ToUniversalTime());

        RemoteCheckInDeviceInfo? deviceInfo = null;
        string? deviceRefreshWarning = null;
        try
        {
            deviceInfo = await apiClient.GetDeviceInfoAsync(token, cancellationToken);
        }
        catch (RemoteCheckInApiException ex) when (ex.IsSessionInvalid)
        {
            throw new RemoteCheckInAuthorizationException(
                ex.Message,
                isSessionInvalid: true,
                ex);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new RemoteCheckInAuthorizationException(
                "签到授权验证已取消，原签到授权保持不变",
                isSessionInvalid: false,
                ex);
        }
        catch (Exception ex)
        {
            deviceRefreshWarning = ex.Message;
        }

        try
        {
            if (remember)
            {
                await credentialStore.SaveRemoteCheckInSessionAsync(session, cancellationToken);
            }
            else
            {
                await credentialStore.ClearRemoteCheckInSessionAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            throw new RemoteCheckInAuthorizationException(
                $"签到授权已获取，但系统安全凭据操作失败：{ex.Message}",
                isSessionInvalid: false,
                ex);
        }

        sessionState.RemoteCheckInSession = session;
        activityLogService.Write(LogEntryKind.Success, "RemoteCheckIn", "已获取独立签到授权");
        return new RemoteCheckInAuthorizationResult(session, deviceInfo, deviceRefreshWarning);
    }

    public async Task<RemoteCheckInDeviceInfo> GetDeviceInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var session = await RequireActiveSessionAsync(cancellationToken);
        try
        {
            return await apiClient.GetDeviceInfoAsync(session.Token, cancellationToken);
        }
        catch (RemoteCheckInApiException ex) when (ex.IsSessionInvalid)
        {
            await ClearSessionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<RemoteCheckInResult> SignAsync(
        RemoteCheckInSignPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.ExpectedLibraryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plan), "锁定场馆无效");
        }

        var profile = RemoteCheckInProfileValidator.NormalizeAndValidate(new RemoteCheckInVenueProfileSettings
        {
            LibraryId = plan.ExpectedLibraryId,
            LibraryName = plan.ExpectedLibraryName,
            BeaconUuid = plan.BeaconUuid,
            Major = plan.Major,
            Minor = plan.Minor,
            Latitude = plan.Latitude,
            Longitude = plan.Longitude
        });

        var session = await RequireActiveSessionAsync(cancellationToken);
        try
        {
            var deviceInfo = await apiClient.GetDeviceInfoAsync(session.Token, cancellationToken);
            if (!deviceInfo.BeaconUuids.Contains(profile.BeaconUuid, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("服务端允许的 Beacon UUID 已变化，请刷新并重新保存场馆配置后再签到");
            }

            var serverTime = await apiClient.GetServerTimeAsync(cancellationToken);
            var request = new RemoteCheckInSignRequest(
                profile.BeaconUuid,
                checked((ushort)profile.Major!.Value),
                checked((ushort)profile.Minor!.Value),
                profile.Latitude!.Value,
                profile.Longitude!.Value,
                serverTime.Value);
            return await apiClient.SignAsync(session.Token, request, cancellationToken);
        }
        catch (RemoteCheckInApiException ex) when (ex.IsSessionInvalid)
        {
            await ClearSessionAsync(cancellationToken);
            throw;
        }
    }

    public async Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        await credentialStore.ClearRemoteCheckInSessionAsync(cancellationToken);
        sessionState.RemoteCheckInSession = null;
    }

    public async Task<bool> ClearExpiredSessionAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentSession is not { } session || !IsExpired(session))
        {
            return false;
        }

        await ClearSessionAsync(cancellationToken);
        activityLogService.Write(LogEntryKind.Warning, "RemoteCheckIn", "签到授权已到期，已自动取消授权");
        return true;
    }

    private async Task<RemoteCheckInSessionCredentials> RequireActiveSessionAsync(
        CancellationToken cancellationToken)
    {
        var session = CurrentSession
            ?? throw new InvalidOperationException("请先使用新的微信授权链接获取签到授权");
        if (!IsExpired(session))
        {
            return session;
        }

        await ClearExpiredSessionAsync(cancellationToken);
        throw new RemoteCheckInSessionExpiredException();
    }

    private bool IsExpired(RemoteCheckInSessionCredentials session)
        => session.ExpiresAt is { } expiresAt && expiresAt <= timeProvider.GetUtcNow();
}

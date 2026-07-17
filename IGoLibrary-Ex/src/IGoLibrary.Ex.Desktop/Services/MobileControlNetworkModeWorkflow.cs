using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IMobileControlNetworkModeWorkflow
{
    Task<MobileControlNetworkMode> ReconcilePersistedModeAsync(
        MobileControlNetworkMode persistedMode,
        CancellationToken cancellationToken = default);

    Task<MobileControlNetworkMode> ApplyAsync(
        MobileControlNetworkMode requestedMode,
        CancellationToken cancellationToken = default);
}

internal sealed class MobileControlNetworkModeWorkflow(
    INetworkExposureManager networkExposureManager,
    ICloudflaredToolLocator cloudflaredLocator,
    ICloudflaredDownloadDialogService dialogService,
    ISettingsWorkflowService settingsWorkflowService,
    IActivityLogService activityLogService,
    ILogger<MobileControlNetworkModeWorkflow> logger) : IMobileControlNetworkModeWorkflow
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<MobileControlNetworkMode> ReconcilePersistedModeAsync(
        MobileControlNetworkMode persistedMode,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var normalized = MobileControlSettings.NormalizeNetworkMode(persistedMode);
            if (normalized != MobileControlNetworkMode.CloudflareTunnel)
            {
                return MobileControlNetworkMode.LocalNetwork;
            }

            var availability = await cloudflaredLocator.FindAsync(cancellationToken);
            if (availability.IsAvailable)
            {
                return MobileControlNetworkMode.CloudflareTunnel;
            }

            var persistenceFailed = false;
            try
            {
                await settingsWorkflowService.SaveMobileControlNetworkModeAsync(
                    MobileControlNetworkMode.LocalNetwork,
                    cancellationToken);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                persistenceFailed = true;
                logger.LogWarning(
                    exception,
                    "启动时发现 cloudflared 不可用，但持久化本机局域网模式失败。");
            }

            var message = persistenceFailed
                ? "启动时发现 cloudflared 已被删除或损坏，已在本次运行回退到本机局域网，但设置保存失败"
                : "启动时发现 cloudflared 已被删除或损坏，手机控制网络方式已回退到本机局域网";
            logger.LogWarning(
                "{Message}。版本={Version}，运行时={RuntimeIdentifier}。",
                message,
                availability.Asset.Version,
                availability.Asset.RuntimeIdentifier);
            activityLogService.Write(LogEntryKind.Warning, "Cloudflared", message);
            return MobileControlNetworkMode.LocalNetwork;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MobileControlNetworkMode> ApplyAsync(
        MobileControlNetworkMode requestedMode,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var normalized = MobileControlSettings.NormalizeNetworkMode(requestedMode);
            if (normalized == MobileControlNetworkMode.LocalNetwork)
            {
                return await networkExposureManager.SetModeAsync(normalized, cancellationToken);
            }

            var availability = await cloudflaredLocator.FindAsync(cancellationToken);
            if (!availability.IsAvailable)
            {
                var confirmed = await dialogService.ConfirmDownloadAsync(
                    availability.Asset,
                    cancellationToken);
                if (!confirmed)
                {
                    logger.LogInformation(
                        "用户暂不下载 cloudflared，保持当前网络方式。当前方式={NetworkMode}。",
                        networkExposureManager.CurrentMode);
                    activityLogService.Write(
                        LogEntryKind.Info,
                        "Cloudflared",
                        "已暂不下载 cloudflared，继续使用当前网络方式");
                    return networkExposureManager.CurrentMode;
                }

                logger.LogInformation(
                    "用户确认下载 cloudflared。版本={Version}，运行时={RuntimeIdentifier}。",
                    availability.Asset.Version,
                    availability.Asset.RuntimeIdentifier);
                var installResult = await dialogService.ShowInstallAsync(cancellationToken);
                if (installResult.Outcome != CloudflaredInstallDialogOutcome.Installed)
                {
                    logger.LogInformation(
                        "cloudflared 安装流程未完成，保持当前网络方式。结果={Outcome}。",
                        installResult.Outcome);
                    return networkExposureManager.CurrentMode;
                }

                availability = await cloudflaredLocator.FindAsync(cancellationToken);
                if (!availability.IsAvailable)
                {
                    throw new InvalidDataException("cloudflared 安装完成后仍不可用，请查看日志");
                }
            }

            return await networkExposureManager.SetModeAsync(
                MobileControlNetworkMode.CloudflareTunnel,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}

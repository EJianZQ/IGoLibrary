using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflareTunnelRuntimeNotificationCoordinator
{
    Task NotifyMobileControlInterruptedAsync(
        CloudflareTunnelInterruptionOutcome outcome,
        CancellationToken cancellationToken = default);

    Task NotifyAuthorizationRelayInterruptedAsync(
        Guid leaseId,
        bool mobileControlTunnelActive,
        CancellationToken cancellationToken = default);

    void CancelAuthorizationRelayNotification(Guid leaseId);

    void CancelPendingNotifications();
}

internal sealed class CloudflareTunnelRuntimeNotificationCoordinator(
    ICloudflareTunnelRuntimeAlertHandler alertHandler,
    INotificationService notificationService,
    ILogger<CloudflareTunnelRuntimeNotificationCoordinator> logger,
    TimeProvider? timeProvider = null) : ICloudflareTunnelRuntimeNotificationCoordinator
{
    internal static TimeSpan CoalescingWindow { get; } = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private PendingAuthorizationRelayNotification? _pendingAuthorizationRelayNotification;
    private DateTimeOffset _lastMobileControlInterruptionAt = DateTimeOffset.MinValue;

    public async Task NotifyMobileControlInterruptedAsync(
        CloudflareTunnelInterruptionOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? pendingCancellation;
        lock (_gate)
        {
            _lastMobileControlInterruptionAt = _timeProvider.GetUtcNow();
            pendingCancellation = DetachPendingNotificationUnderGate();
        }

        pendingCancellation?.Cancel();
        await alertHandler.HandleAsync(outcome, cancellationToken);
    }

    public async Task NotifyAuthorizationRelayInterruptedAsync(
        Guid leaseId,
        bool mobileControlTunnelActive,
        CancellationToken cancellationToken = default)
    {
        PendingAuthorizationRelayNotification? pendingToStart = null;
        CancellationTokenSource? pendingToCancel = null;
        var showImmediately = false;
        var suppressed = false;

        lock (_gate)
        {
            if (HasRecentMobileControlInterruptionUnderGate())
            {
                suppressed = true;
            }
            else if (!mobileControlTunnelActive)
            {
                showImmediately = true;
            }
            else
            {
                pendingToCancel = DetachPendingNotificationUnderGate();
                pendingToStart = new PendingAuthorizationRelayNotification(
                    leaseId,
                    new CancellationTokenSource());
                _pendingAuthorizationRelayNotification = pendingToStart;
            }
        }

        pendingToCancel?.Cancel();
        if (suppressed)
        {
            logger.LogInformation(
                "因同一运行时故障已影响手机控制，已抑制旧版授权链接中继 Tunnel 警告。" +
                "租约 ID={LeaseId}。",
                leaseId);
            return;
        }

        if (showImmediately)
        {
            await ShowLegacyWarningSafelyAsync(cancellationToken);
            return;
        }

        if (pendingToStart is not null)
        {
            _ = ShowDelayedLegacyWarningSafelyAsync(pendingToStart);
            logger.LogInformation(
                "为合并运行时故障，已延迟授权链接中继 Tunnel 警告。" +
                "租约 ID={LeaseId}，延迟秒数={DelaySeconds}。",
                leaseId,
                CoalescingWindow.TotalSeconds);
        }
    }

    public void CancelAuthorizationRelayNotification(Guid leaseId)
    {
        CancellationTokenSource? pendingCancellation = null;
        lock (_gate)
        {
            if (_pendingAuthorizationRelayNotification?.LeaseId == leaseId)
            {
                pendingCancellation = DetachPendingNotificationUnderGate();
            }
        }

        pendingCancellation?.Cancel();
    }

    public void CancelPendingNotifications()
    {
        CancellationTokenSource? pendingCancellation;
        lock (_gate)
        {
            pendingCancellation = DetachPendingNotificationUnderGate();
        }

        pendingCancellation?.Cancel();
    }

    private async Task ShowDelayedLegacyWarningSafelyAsync(
        PendingAuthorizationRelayNotification pending)
    {
        try
        {
            await Task.Delay(CoalescingWindow, _timeProvider, pending.Cancellation.Token);

            var shouldShow = false;
            lock (_gate)
            {
                if (ReferenceEquals(_pendingAuthorizationRelayNotification, pending) &&
                    !HasRecentMobileControlInterruptionUnderGate())
                {
                    _pendingAuthorizationRelayNotification = null;
                    shouldShow = true;
                }
            }

            if (shouldShow)
            {
                await ShowLegacyWarningSafelyAsync(pending.Cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (pending.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "合并授权链接中继的 Cloudflare Tunnel 运行时警告时失败。" +
                "租约 ID={LeaseId}。",
                pending.LeaseId);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pendingAuthorizationRelayNotification, pending))
                {
                    _pendingAuthorizationRelayNotification = null;
                }
            }

            pending.Cancellation.Dispose();
        }
    }

    private async Task ShowLegacyWarningSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await notificationService.ShowWarningAsync(
                "Cloudflare Tunnel 不可用",
                NetworkExposureManager.TunnelUnavailableWithoutFallbackUserMessage,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "显示旧版 Cloudflare Tunnel 运行时警告失败。");
        }
    }

    private bool HasRecentMobileControlInterruptionUnderGate()
    {
        var elapsed = _timeProvider.GetUtcNow() - _lastMobileControlInterruptionAt;
        return elapsed >= TimeSpan.Zero && elapsed < CoalescingWindow;
    }

    private CancellationTokenSource? DetachPendingNotificationUnderGate()
    {
        var pending = _pendingAuthorizationRelayNotification;
        _pendingAuthorizationRelayNotification = null;
        return pending?.Cancellation;
    }

    private sealed record PendingAuthorizationRelayNotification(
        Guid LeaseId,
        CancellationTokenSource Cancellation);
}

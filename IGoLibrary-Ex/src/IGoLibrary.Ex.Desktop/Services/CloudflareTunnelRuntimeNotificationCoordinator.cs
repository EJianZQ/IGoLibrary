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
                "Suppressed the legacy authorization-relay Tunnel warning because mobile control " +
                "was affected in the same runtime incident. LeaseId={LeaseId}.",
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
                "Delayed the authorization-relay Tunnel warning for runtime incident coalescing. " +
                "LeaseId={LeaseId}, DelaySeconds={DelaySeconds}.",
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
                "Failed while coalescing the authorization-relay Cloudflare Tunnel runtime warning. " +
                "LeaseId={LeaseId}.",
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
            logger.LogWarning(ex, "Failed to show the legacy Cloudflare Tunnel runtime warning.");
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

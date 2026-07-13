using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ISettingsWorkflowService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task<AppSettings> SaveSystemSettingsAsync(
        SystemSettingsSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task SaveNotificationSettingsAsync(
        TaskEventAlertSettings alerts,
        CancellationToken cancellationToken = default);

    Task SaveGrabStartPreferencesAsync(
        GrabReservationStrategy strategy,
        bool disableOptimalStrategyReminder,
        CancellationToken cancellationToken = default);

    Task SaveGrabScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default);

    Task SaveTomorrowScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves selected venues in persisted high-to-low scan-priority order.
    /// </summary>
    Task SaveGlobalLeakSelectedLibrariesAsync(
        IReadOnlyList<GlobalLeakLibraryTarget> libraries,
        CancellationToken cancellationToken = default);

    Task ClearStoredLibrarySelectionAsync(CancellationToken cancellationToken = default);

    Task SaveDashboardMetricsAsync(
        DashboardMetrics metrics,
        CancellationToken cancellationToken = default);

    Task<MobileControlSettings> EnsureMobileControlSettingsAsync(CancellationToken cancellationToken = default);

    Task<MobileControlSettings> SaveMobileControlPortAsync(
        int port,
        CancellationToken cancellationToken = default);

    Task<MobileControlSettings> SaveMobileControlAccessTokenAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<MobileControlSettings> SaveMobileControlAutoStartAsync(
        bool autoStart,
        CancellationToken cancellationToken = default);

    Task<MobileControlSettings> SaveMobileControlNetworkModeAsync(
        MobileControlNetworkMode networkMode,
        CancellationToken cancellationToken = default);

    Task<MobileControlSettings> SaveCloudflareTunnelProxyAsync(
        CloudflareTunnelProxyMode proxyMode,
        string manualProxyUrl,
        CancellationToken cancellationToken = default);

    Task<MobileControlSettings> SaveCloudflareTunnelFallbackAsync(
        bool fallbackToLocalNetworkOnTunnelFailure,
        CancellationToken cancellationToken = default);

    Task<MobileControlSettings> SaveClashMihomoCompatibilityAsync(
        bool enabled,
        string configPath,
        string routePolicy,
        CancellationToken cancellationToken = default);
}

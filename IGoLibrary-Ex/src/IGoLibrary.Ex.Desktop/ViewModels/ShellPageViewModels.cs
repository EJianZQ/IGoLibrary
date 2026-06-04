using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class HomeDashboardViewModel : ViewModelBase;

public sealed partial class AccountVenueViewModel(
    ISessionWorkflowService sessionWorkflowService,
    IVenueWorkflowService venueWorkflowService) : ViewModelBase
{
    public Task<SessionWorkflowResult> AuthenticateFromCodeAsync(
        string code,
        bool remember,
        CancellationToken cancellationToken = default)
    {
        return sessionWorkflowService.AuthenticateFromCodeAsync(code, remember, cancellationToken);
    }

    public Task<SessionWorkflowResult> AuthenticateFromCookieAsync(
        string cookie,
        bool remember,
        CancellationToken cancellationToken = default)
    {
        return sessionWorkflowService.AuthenticateFromCookieAsync(cookie, remember, cancellationToken);
    }

    public Task<SessionWorkflowResult> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        return sessionWorkflowService.RestoreAsync(cancellationToken);
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        return sessionWorkflowService.SignOutAsync(cancellationToken);
    }

    public Task<VenueLibraryLoadResult> LoadLibrariesAsync(
        bool restorePreferredSelection,
        int? preferredLibraryId = null,
        CancellationToken cancellationToken = default)
    {
        return venueWorkflowService.LoadLibrariesAsync(
            restorePreferredSelection,
            preferredLibraryId,
            cancellationToken);
    }

    public Task<VenueBindingResult> BindLibraryAsync(
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        return venueWorkflowService.BindLibraryAsync(libraryId, cancellationToken);
    }

    public Task<VenueBindingResult> RefreshBoundLibraryAsync(CancellationToken cancellationToken = default)
    {
        return venueWorkflowService.RefreshBoundLibraryAsync(cancellationToken);
    }

    public Task<VenuePreviewResult> PreviewLibraryAsync(
        LibrarySummary library,
        CancellationToken cancellationToken = default)
    {
        return venueWorkflowService.PreviewLibraryAsync(library, cancellationToken);
    }

    public Task<LibraryRule?> LoadLibraryRuleAsync(
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        return venueWorkflowService.LoadLibraryRuleAsync(libraryId, cancellationToken);
    }

    public Task<IReadOnlyList<TrackedSeat>> GetFavoritesAsync(
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        return venueWorkflowService.GetFavoritesAsync(libraryId, cancellationToken);
    }

    public Task SaveFavoritesAsync(
        int libraryId,
        IReadOnlyList<TrackedSeat> seats,
        CancellationToken cancellationToken = default)
    {
        return venueWorkflowService.SaveFavoritesAsync(libraryId, seats, cancellationToken);
    }
}

public sealed partial class GrabPageViewModel(
    IGrabSeatCoordinator grabSeatCoordinator,
    ISettingsWorkflowService settingsWorkflowService) : ViewModelBase
{
    public Task StartAsync(GrabSeatPlan plan, CancellationToken cancellationToken = default)
    {
        return grabSeatCoordinator.StartAsync(plan, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return grabSeatCoordinator.StopAsync(cancellationToken);
    }

    public Task SaveReservationStrategyAsync(
        GrabReservationStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        return settingsWorkflowService.SaveGrabReservationStrategyAsync(strategy, cancellationToken);
    }
}

public sealed partial class OccupyPageViewModel(
    IOccupySeatCoordinator occupySeatCoordinator,
    IReservationWorkflowService reservationWorkflowService) : ViewModelBase
{
    public Task StartAsync(OccupySeatPlan plan, CancellationToken cancellationToken = default)
    {
        return occupySeatCoordinator.StartAsync(plan, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return occupySeatCoordinator.StopAsync(cancellationToken);
    }

    public Task<ReservationOperationResult> RefreshReservationAsync(CancellationToken cancellationToken = default)
    {
        return reservationWorkflowService.RefreshReservationAsync(cancellationToken);
    }

    public Task<ReservationOperationResult> CancelCurrentReservationAsync(
        ReservationInfo reservation,
        bool stopOccupyFirst,
        CancellationToken cancellationToken = default)
    {
        return reservationWorkflowService.CancelCurrentReservationAsync(
            reservation,
            stopOccupyFirst,
            cancellationToken);
    }
}

public sealed partial class NotificationSettingsViewModel(
    ISettingsWorkflowService settingsWorkflowService,
    INotificationTestService notificationTestService) : ViewModelBase
{
    public Task SaveNotificationSettingsAsync(
        TaskEventAlertSettings alerts,
        CancellationToken cancellationToken = default)
    {
        return settingsWorkflowService.SaveNotificationSettingsAsync(alerts, cancellationToken);
    }

    public Task SendTestEmailAsync(
        EmailAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return notificationTestService.SendTestEmailAsync(settings, cancellationToken);
    }

    public Task SendTestTelegramAsync(
        TelegramAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return notificationTestService.SendTestTelegramAsync(settings, cancellationToken);
    }

    public Task SendTestLocalAlertAsync(
        LocalAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return notificationTestService.SendTestLocalAlertAsync(settings, cancellationToken);
    }
}

public sealed partial class SystemSettingsViewModel(
    ISettingsWorkflowService settingsWorkflowService,
    IProtocolTemplateEditorService protocolTemplateEditorService) : ViewModelBase
{
    public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        return settingsWorkflowService.LoadAsync(cancellationToken);
    }

    public Task<AppSettings> SaveSystemSettingsAsync(
        SystemSettingsSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        return settingsWorkflowService.SaveSystemSettingsAsync(snapshot, cancellationToken);
    }

    public Task ClearStoredLibrarySelectionAsync(CancellationToken cancellationToken = default)
    {
        return settingsWorkflowService.ClearStoredLibrarySelectionAsync(cancellationToken);
    }

    public Task SaveDashboardMetricsAsync(
        DashboardMetrics metrics,
        CancellationToken cancellationToken = default)
    {
        return settingsWorkflowService.SaveDashboardMetricsAsync(metrics, cancellationToken);
    }

    public Task<TraceIntGraphQlTemplateSet> LoadProtocolTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return protocolTemplateEditorService.LoadTemplatesAsync(cancellationToken);
    }

    public Task SaveProtocolOverridesAsync(
        TraceIntGraphQlTemplateOverrides overrides,
        CancellationToken cancellationToken = default)
    {
        return protocolTemplateEditorService.SaveOverridesAsync(overrides, cancellationToken);
    }

    public Task ResetProtocolOverridesAsync(CancellationToken cancellationToken = default)
    {
        return protocolTemplateEditorService.ResetOverridesAsync(cancellationToken);
    }
}

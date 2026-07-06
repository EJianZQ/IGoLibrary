using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Tests;

internal static class MainWindowViewModelTestHarness
{
    public static MainWindowViewModel Create(
        ISessionWorkflowService sessionWorkflowService,
        IVenueWorkflowService venueWorkflowService,
        IReservationWorkflowService reservationWorkflowService,
        ISettingsWorkflowService settingsWorkflowService,
        IProtocolTemplateEditorService protocolTemplateEditorService,
        INotificationTestService notificationTestService,
        IGrabSeatCoordinator grabSeatCoordinator,
        IGlobalLeakCoordinator globalLeakCoordinator,
        IOccupySeatCoordinator occupySeatCoordinator,
        ITomorrowReservationCoordinator tomorrowReservationCoordinator,
        IActivityLogService activityLogService,
        INotificationService notificationService,
        IErrorDialogService errorDialogService,
        IUpdateCheckService updateCheckService,
        IUpdateDialogService updateDialogService,
        IExternalLinkService externalLinkService,
        IAppVersionProvider appVersionProvider,
        IAppThemeService appThemeService,
        TimeProvider timeProvider,
        AppWindowService appWindowService,
        IStartupEntryService startupEntryService,
        ILanCookieRelayService lanCookieRelayService,
        IQrCodeImageFactory qrCodeImageFactory)
    {
        var pages = new MainWindowWorkflowPages(
            new HomeDashboardViewModel(
                activityLogService,
                appThemeService,
                timeProvider),
            new SessionViewModel(
                activityLogService,
                notificationService,
                appThemeService,
                timeProvider),
            new AccountVenueViewModel(
                sessionWorkflowService,
                venueWorkflowService,
                settingsWorkflowService,
                activityLogService,
                notificationService,
                appThemeService),
            new MultiSeatSelectionViewModel(
                venueWorkflowService,
                activityLogService,
                notificationService),
            new GrabPageViewModel(
                grabSeatCoordinator,
                settingsWorkflowService,
                activityLogService,
                notificationService,
                appThemeService,
                timeProvider),
            new GlobalLeakPageViewModel(
                globalLeakCoordinator,
                settingsWorkflowService,
                activityLogService,
                notificationService,
                appThemeService,
                timeProvider),
            new OccupyPageViewModel(
                occupySeatCoordinator,
                reservationWorkflowService,
                activityLogService,
                notificationService,
                timeProvider),
            new TomorrowReservationPageViewModel(
                tomorrowReservationCoordinator,
                settingsWorkflowService,
                activityLogService,
                notificationService,
                appThemeService,
                timeProvider),
            new LanCookieRelayViewModel(
                lanCookieRelayService,
                qrCodeImageFactory,
                activityLogService,
                notificationService),
            new NotificationSettingsViewModel(
                settingsWorkflowService,
                notificationTestService,
                activityLogService,
                notificationService,
                errorDialogService),
            new SystemSettingsViewModel(
                settingsWorkflowService,
                protocolTemplateEditorService,
                appThemeService,
                activityLogService,
                notificationService,
                startupEntryService),
            new ProtocolTemplatesViewModel(
                protocolTemplateEditorService,
                activityLogService,
                notificationService),
            new ShellNavigationViewModel(
                appWindowService,
                grabSeatCoordinator,
                globalLeakCoordinator,
                occupySeatCoordinator,
                tomorrowReservationCoordinator),
            new ActivityLogPanelViewModel(
                activityLogService,
                appThemeService),
            new UpdateLinksViewModel(
                activityLogService,
                notificationService,
                updateCheckService,
                updateDialogService,
                externalLinkService,
                appVersionProvider));

        return new MainWindowViewModel(
            pages,
            new ShellWorkflowState(),
            activityLogService,
            notificationService,
            appThemeService,
            timeProvider,
            lanCookieRelayService);
    }
}

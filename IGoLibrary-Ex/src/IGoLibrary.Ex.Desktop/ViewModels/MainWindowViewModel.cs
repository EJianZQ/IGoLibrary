using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed class MainWindowViewModel(
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
    IStartupEntryService startupEntryService)
    : MainWindowWorkflowViewModel(
        sessionWorkflowService,
        venueWorkflowService,
        reservationWorkflowService,
        settingsWorkflowService,
        protocolTemplateEditorService,
        notificationTestService,
        grabSeatCoordinator,
        globalLeakCoordinator,
        occupySeatCoordinator,
        tomorrowReservationCoordinator,
        activityLogService,
        notificationService,
        errorDialogService,
        updateCheckService,
        updateDialogService,
        externalLinkService,
        appVersionProvider,
        appThemeService,
        timeProvider,
        appWindowService,
        startupEntryService);

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
    IOccupySeatCoordinator occupySeatCoordinator,
    IActivityLogService activityLogService,
    INotificationService notificationService,
    IErrorDialogService errorDialogService,
    IAppThemeService appThemeService,
    AppWindowService appWindowService)
    : MainWindowWorkflowViewModel(
        sessionWorkflowService,
        venueWorkflowService,
        reservationWorkflowService,
        settingsWorkflowService,
        protocolTemplateEditorService,
        notificationTestService,
        grabSeatCoordinator,
        occupySeatCoordinator,
        activityLogService,
        notificationService,
        errorDialogService,
        appThemeService,
        appWindowService);

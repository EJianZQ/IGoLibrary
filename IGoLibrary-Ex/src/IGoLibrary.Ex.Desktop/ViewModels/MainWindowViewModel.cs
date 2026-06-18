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
    ITaskEventAlertDispatcher taskEventAlertDispatcher,
    IGrabSeatCoordinator grabSeatCoordinator,
    IVenueAvailabilityCoordinator venueAvailabilityCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    ITomorrowReservationCoordinator tomorrowReservationCoordinator,
    ICheckInGuardCoordinator checkInGuardCoordinator,
    IActivityLogService activityLogService,
    INotificationService notificationService,
    IErrorDialogService errorDialogService,
    IAppThemeService appThemeService,
    AppWindowService appWindowService,
    IHealthCheckService? healthCheckService = null,
    IDiagnosticExportService? diagnosticExportService = null)
    : MainWindowWorkflowViewModel(
        sessionWorkflowService,
        venueWorkflowService,
        reservationWorkflowService,
        settingsWorkflowService,
        protocolTemplateEditorService,
        notificationTestService,
        taskEventAlertDispatcher,
        grabSeatCoordinator,
        venueAvailabilityCoordinator,
        occupySeatCoordinator,
        tomorrowReservationCoordinator,
        checkInGuardCoordinator,
        activityLogService,
        notificationService,
        errorDialogService,
        appThemeService,
        appWindowService,
        healthCheckService,
        diagnosticExportService);

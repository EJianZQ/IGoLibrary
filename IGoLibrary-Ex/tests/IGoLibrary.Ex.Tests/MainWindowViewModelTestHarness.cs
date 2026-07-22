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
        IQrCodeImageFactory qrCodeImageFactory,
        IMobileControlService? mobileControlService = null,
        IRemoteCheckInWorkflowService? remoteCheckInWorkflowService = null,
        IRemoteCheckInProfileService? remoteCheckInProfileService = null,
        INetworkExposureManager? networkExposureManager = null,
        ISeatLabelDialogService? seatLabelDialogService = null,
        IGrabStrategyReminderDialogService? grabStrategyReminderDialogService = null,
        IMainWindowSizePersistenceService? windowSizePersistenceService = null,
        IWindowsUpdateProgressDialogService? windowsUpdateProgressDialogService = null,
        IMobileControlNetworkModeWorkflow? mobileControlNetworkModeWorkflow = null,
        ITaskSleepPreventionService? taskSleepPreventionService = null)
    {
        mobileControlService ??= new FakeMobileControlService();
        remoteCheckInWorkflowService ??= new FakeRemoteCheckInWorkflowService();
        remoteCheckInProfileService ??= new FakeRemoteCheckInProfileService();
        networkExposureManager ??= new FakeNetworkExposureManager();
        mobileControlNetworkModeWorkflow ??=
            new FakeMobileControlNetworkModeWorkflow(networkExposureManager);
        seatLabelDialogService ??= new FakeSeatLabelDialogService();
        grabStrategyReminderDialogService ??= new FakeGrabStrategyReminderDialogService();
        windowSizePersistenceService ??= NoOpMainWindowSizePersistenceService.Instance;
        windowsUpdateProgressDialogService ??= new FakeWindowsUpdateProgressDialogService();
        taskSleepPreventionService ??= new FakeTaskSleepPreventionService();
        var workflowState = new ShellWorkflowState();
        var taskLaunchService = new FakeTaskLaunchService(
            grabSeatCoordinator,
            globalLeakCoordinator,
            occupySeatCoordinator);
        var oauthCodeRegistry = new OAuthCodeConsumptionRegistry();
        var lanCookieRelayViewModel = new LanCookieRelayViewModel(
            lanCookieRelayService,
            qrCodeImageFactory,
            activityLogService,
            notificationService);
        var pages = new MainWindowWorkflowPages(
            new HomeDashboardViewModel(
                activityLogService,
                appThemeService,
                timeProvider),
            new SessionViewModel(
                activityLogService,
                notificationService,
                appThemeService,
                timeProvider,
                oauthCodeRegistry),
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
                notificationService,
                seatLabelDialogService),
            new GrabPageViewModel(
                grabSeatCoordinator,
                taskLaunchService,
                settingsWorkflowService,
                activityLogService,
                notificationService,
                appThemeService,
                grabStrategyReminderDialogService,
                timeProvider),
            new GlobalLeakPageViewModel(
                globalLeakCoordinator,
                taskLaunchService,
                venueWorkflowService,
                settingsWorkflowService,
                activityLogService,
                notificationService,
                appThemeService,
                timeProvider,
                new GlobalLeakLibrarySelectionViewModel()),
            new OccupyPageViewModel(
                occupySeatCoordinator,
                taskLaunchService,
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
            lanCookieRelayViewModel,
            new RemoteCheckInPageViewModel(
                remoteCheckInWorkflowService,
                remoteCheckInProfileService,
                reservationWorkflowService,
                workflowState,
                oauthCodeRegistry,
                lanCookieRelayViewModel,
                activityLogService,
                notificationService),
            new MobileControlPageViewModel(
                mobileControlService,
                settingsWorkflowService,
                qrCodeImageFactory,
                activityLogService,
                notificationService),
            new NotificationSettingsViewModel(
                settingsWorkflowService,
                notificationTestService,
                activityLogService,
                notificationService,
                errorDialogService,
                timeProvider),
            new SystemSettingsViewModel(
                settingsWorkflowService,
                protocolTemplateEditorService,
                appThemeService,
                activityLogService,
                notificationService,
                startupEntryService,
                new StorageSettingsViewModel(
                    new FakeStorageLocationService(),
                    new FakeFolderPickerService(),
                    new FakeStorageChangeWorkflowService(),
                    new FakeLoggingSettingsWorkflowService(),
                    activityLogService,
                    notificationService),
                networkExposureManager,
                mobileControlNetworkModeWorkflow,
                windowSizePersistenceService,
                taskSleepPreventionService,
                timeProvider),
            new ProtocolTemplatesViewModel(
                protocolTemplateEditorService,
                activityLogService,
                notificationService,
                timeProvider),
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
                windowsUpdateProgressDialogService,
                externalLinkService,
                appVersionProvider));

        return new MainWindowViewModel(
            pages,
            workflowState,
            activityLogService,
            notificationService,
            appThemeService,
            timeProvider,
            lanCookieRelayService,
            mobileControlService);
    }
}

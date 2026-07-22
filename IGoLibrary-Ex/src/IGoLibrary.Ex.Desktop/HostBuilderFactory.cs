using IGoLibrary.Ex.Application;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Infrastructure;
using IGoLibrary.Ex.Infrastructure.Logging;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop;

internal static class HostBuilderFactory
{
    public static IHostBuilder Create(
        string[] args,
        IAppLogWriter? sharedLogWriter = null,
        StorageLocationManager? storageLocationManager = null,
        StorageLocations? storageLocations = null)
    {
        return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(config =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddDebug();
                logging.Services.AddSingleton<ILoggerProvider, AppFileLoggerProvider>();
            })
            .ConfigureServices(services =>
            {
                if (sharedLogWriter is not null)
                {
                    services.AddSingleton(sharedLogWriter);
                    if (sharedLogWriter is IAppLogRuntimeController runtimeController)
                    {
                        services.AddSingleton(runtimeController);
                    }
                }

                if (storageLocationManager is not null && storageLocations is not null)
                {
                    services.AddSingleton(storageLocationManager);
                    services.AddSingleton<IStorageLocationService>(storageLocationManager);
                    services.AddSingleton(storageLocations);
                }

                services.AddApplication();
                services.AddSingleton<IAppSettingsDefaults, DesktopAppSettingsDefaults>();
                services.AddInfrastructure();
                services.AddSingleton<IAppThemeService, AppThemeService>();
                services.AddSingleton<AppWindowService>();
                services.AddSingleton<IMainWindowSizePersistenceService, MainWindowSizePersistenceService>();
                services.AddSingleton<IErrorDialogService, ErrorDialogService>();
                services.AddSingleton<IUpdateDialogService, UpdateDialogService>();
                services.AddSingleton<WindowsUpdateWorkspaceManager>();
                services.AddSingleton<
                    IWindowsUpdatePackagePreparationService,
                    WindowsUpdatePackagePreparationService>();
                services.AddSingleton<
                    IWindowsUpdateHandoffService,
                    WindowsUpdateHandoffService>();
                services.AddSingleton<IWindowsPortableUpdateService, WindowsPortableUpdateService>();
                services.AddSingleton<IWindowsUpdateProgressDialogService, WindowsUpdateProgressDialogService>();
                services.AddSingleton<IExternalLinkService, ExternalLinkService>();
                services.AddSingleton<IStartupEntryService, StartupEntryService>();
                services.AddSingleton<IApplicationRestartService, ApplicationRestartService>();
                services.AddSingleton<IDataRestoreRestartService, DataRestoreRestartService>();
                services.AddSingleton<IFolderPickerService, FolderPickerService>();
                services.AddSingleton<IBackupFilePickerService, BackupFilePickerService>();
                services.AddSingleton<IBackupDialogService, BackupDialogService>();
                services.AddSingleton<IBackupDataFlushService, BackupDataFlushService>();
                services.AddSingleton<IActiveBackupTaskService, ActiveBackupTaskService>();
                services.AddSingleton<IBackupWorkflowService, BackupWorkflowService>();
                services.AddSingleton<IStorageChangeDialogService, StorageChangeDialogService>();
                services.AddSingleton<ISeatLabelDialogService, SeatLabelDialogService>();
                services.AddSingleton<IGrabStrategyReminderDialogService, GrabStrategyReminderDialogService>();
                services.AddSingleton<IStorageChangeWorkflowService, StorageChangeWorkflowService>();
                services.AddSingleton<ILanAddressProvider, LanAddressProvider>();
                services.AddSingleton<IQrCodeImageFactory, QrCodeImageFactory>();
                services.AddSingleton<ICloudflareSystemProxyProvider, CloudflareSystemProxyProvider>();
                services.AddSingleton<ICloudflareTunnelProxyResolver, CloudflareTunnelProxyResolver>();
                services.AddSingleton<ICloudflareTunnelHealthProbeFactory, CloudflareTunnelHealthProbeFactory>();
                services.AddSingleton<IClashMihomoConfigurationLocator, ClashMihomoConfigurationLocator>();
                services.AddSingleton<IMihomoControllerClient, MihomoControllerClient>();
                services.AddSingleton<IClashMihomoCompatibilityService, ClashMihomoCompatibilityService>();
                services.AddSingleton<CloudflaredAssetCatalog>();
                services.AddSingleton<ICloudflaredPathProvider, CloudflaredPathProvider>();
                services.AddSingleton<ICloudflaredToolLocator, CloudflaredToolLocator>();
                services.AddSingleton<ICloudflaredExtractor, CloudflaredExtractor>();
                services.AddSingleton<ICloudflaredDownloadWorkspace, CloudflaredDownloadWorkspace>();
                services.AddSingleton<ICloudflaredManagedInstaller, CloudflaredManagedInstaller>();
                services.AddSingleton<CloudflaredInstallService>();
                services.AddSingleton<ICloudflaredInstallService>(serviceProvider =>
                    serviceProvider.GetRequiredService<CloudflaredInstallService>());
                services.AddHostedService(serviceProvider =>
                    serviceProvider.GetRequiredService<CloudflaredInstallService>());
                services.AddSingleton<ICloudflaredDownloadDialogService, CloudflaredDownloadDialogService>();
                services.AddSingleton<ICloudflareQuickTunnelRunner, CloudflareQuickTunnelRunner>();
                services.AddSingleton<ICloudflareTunnelRuntimeAlertHandler, CloudflareTunnelRuntimeAlertHandler>();
                services.AddSingleton<
                    ICloudflareTunnelRuntimeNotificationCoordinator,
                    CloudflareTunnelRuntimeNotificationCoordinator>();
                services.AddSingleton<INetworkExposureManager, NetworkExposureManager>();
                services.AddSingleton<IMobileControlNetworkModeWorkflow, MobileControlNetworkModeWorkflow>();
                services.AddSingleton<ILanCookieRelayService, LanCookieRelayService>();
                services.AddSingleton<IMobileControlTaskUiStateAccessor, MobileControlTaskUiStateAccessor>();
                services.AddSingleton<IMobileControlStatusSnapshotProvider, MobileControlStatusSnapshotProvider>();
                services.AddSingleton<IMobileControlTaskRecordsProvider, MobileControlTaskRecordsProvider>();
                services.AddSingleton<IMobileControlOccupyPlanProvider, MobileControlOccupyPlanProvider>();
                services.AddSingleton<IMobileControlTaskStartService, MobileControlTaskStartService>();
                services.AddSingleton<IMobileControlCookieRefreshHandler, MobileControlCookieRefreshHandler>();
                services.AddSingleton<IMobileControlActionService, MobileControlActionService>();
                services.AddSingleton<IMobileControlService, MobileControlService>();
                services.AddSingleton<ToastNotificationService>();
                services.AddSingleton<IToastNotificationService>(
                    serviceProvider => serviceProvider.GetRequiredService<ToastNotificationService>());
                services.AddSingleton<INotificationService>(serviceProvider => serviceProvider.GetRequiredService<ToastNotificationService>());
                services.AddSingleton<AlertSoundService>();
                services.AddSingleton<IAlertSoundService>(serviceProvider => serviceProvider.GetRequiredService<AlertSoundService>());
                services.AddSingleton<ITaskEventAlertDispatcher, TaskEventAlertService>();
                services.AddSingleton<CookieExpirationAlertMonitor>();
                services.AddHostedService<CookieExpirationAlertHostedService>();
                services.AddSingleton<INotificationTestService, DesktopNotificationTestService>();
                services.AddSingleton<ICoordinatorEventPublisher, DesktopCoordinatorEventPublisher>();
                services.AddSingleton<OAuthCodeConsumptionRegistry>();
                services.AddSingleton<ShellWorkflowState>();
                services.AddSingleton<HomeDashboardViewModel>();
                services.AddSingleton<SessionViewModel>();
                services.AddSingleton<AccountVenueViewModel>();
                services.AddSingleton<MultiSeatSelectionViewModel>();
                services.AddSingleton<GrabPageViewModel>();
                services.AddSingleton<GlobalLeakLibrarySelectionViewModel>();
                services.AddSingleton<GlobalLeakPageViewModel>();
                services.AddSingleton<OccupyPageViewModel>();
                services.AddSingleton<TomorrowReservationPageViewModel>();
                services.AddSingleton<LanCookieRelayViewModel>();
                services.AddSingleton<RemoteCheckInPageViewModel>();
                services.AddSingleton<MobileControlPageViewModel>();
                services.AddSingleton<NotificationSettingsViewModel>();
                services.AddSingleton<SystemSettingsViewModel>();
                services.AddSingleton<LocalBackupViewModel>();
                services.AddSingleton<WebDavSyncViewModel>();
                services.AddSingleton<StorageSettingsViewModel>();
                services.AddHostedService<WebDavAutoUploadHostedService>();
                services.AddSingleton<ProtocolTemplatesViewModel>();
                services.AddSingleton<ShellNavigationViewModel>();
                services.AddSingleton<ActivityLogPanelViewModel>();
                services.AddSingleton<UpdateLinksViewModel>();
                services.AddSingleton<MainWindowWorkflowPages>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<MainWindowViewModel>();
            });
    }
}

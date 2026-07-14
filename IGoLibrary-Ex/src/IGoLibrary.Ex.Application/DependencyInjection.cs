using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IGoLibrary.Ex.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<AppRuntimeState>();
        services.AddSingleton<ISessionState>(serviceProvider => serviceProvider.GetRequiredService<AppRuntimeState>());
        services.AddSingleton<IVenueState>(serviceProvider => serviceProvider.GetRequiredService<AppRuntimeState>());
        services.AddSingleton<IReservationState>(serviceProvider => serviceProvider.GetRequiredService<AppRuntimeState>());
        services.AddSingleton<IRemoteCheckInSessionState>(serviceProvider => serviceProvider.GetRequiredService<AppRuntimeState>());
        services.AddSingleton<IActivityLogService, ActivityLogService>();
        services.TryAddSingleton<IAppSettingsDefaults, DefaultAppSettingsDefaults>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IAppVersionProvider, AssemblyAppVersionProvider>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<ILibraryService, LibraryService>();
        services.AddSingleton<ISeatLabelService, SeatLabelService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISessionWorkflowService, SessionWorkflowService>();
        services.AddSingleton<IVenueWorkflowService, VenueWorkflowService>();
        services.AddSingleton<IReservationWorkflowService, ReservationWorkflowService>();
        services.AddSingleton<ISettingsWorkflowService, SettingsWorkflowService>();
        services.AddSingleton<ILoggingSettingsWorkflowService, LoggingSettingsWorkflowService>();
        services.AddSingleton<IUpdateCheckService, UpdateCheckService>();
        services.AddSingleton<IUpdateInstallGuard, UpdateInstallGuard>();
        services.AddSingleton<IProtocolTemplateEditorService, ProtocolTemplateEditorService>();
        services.AddSingleton<IRemoteCheckInProfileService, RemoteCheckInProfileService>();
        services.AddSingleton<IRemoteCheckInWorkflowService, RemoteCheckInWorkflowService>();
        services.AddSingleton<ICoordinatorRuntime, SystemCoordinatorRuntime>();
        services.AddSingleton<IGrabReservationAttemptStrategy, QueryThenReserveGrabReservationStrategy>();
        services.AddSingleton<IGrabReservationAttemptStrategy, DirectReserveGrabReservationStrategy>();
        services.AddSingleton<GrabReservationStrategySelector>();
        services.AddSingleton<IOccupyReReservationExecutor, OccupyReReservationExecutor>();
        services.AddSingleton<GrabSeatWorkflowRunner>();
        services.AddSingleton<GlobalLeakWorkflowRunner>();
        services.AddSingleton<OccupySeatWorkflowRunner>();
        services.AddSingleton<TomorrowReservationWorkflowRunner>();
        services.AddSingleton<IGrabSeatCoordinator, GrabSeatCoordinator>();
        services.AddSingleton<IGlobalLeakCoordinator, GlobalLeakCoordinator>();
        services.AddSingleton<IOccupySeatCoordinator, OccupySeatCoordinator>();
        services.AddSingleton<ITomorrowReservationCoordinator, TomorrowReservationCoordinator>();
        return services;
    }
}

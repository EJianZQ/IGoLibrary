using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using Microsoft.Extensions.DependencyInjection;

namespace IGoLibrary.Ex.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<AppRuntimeState>();
        services.AddSingleton<IActivityLogService, ActivityLogService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<ILibraryService, LibraryService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISessionWorkflowService, SessionWorkflowService>();
        services.AddSingleton<IVenueWorkflowService, VenueWorkflowService>();
        services.AddSingleton<IReservationWorkflowService, ReservationWorkflowService>();
        services.AddSingleton<ISettingsWorkflowService, SettingsWorkflowService>();
        services.AddSingleton<IProtocolTemplateEditorService, ProtocolTemplateEditorService>();
        services.AddSingleton<INotificationTestService, NotificationTestService>();
        services.AddSingleton<IGrabReservationAttemptStrategy, QueryThenReserveGrabReservationStrategy>();
        services.AddSingleton<IGrabReservationAttemptStrategy, DirectReserveGrabReservationStrategy>();
        services.AddSingleton<GrabReservationStrategySelector>();
        services.AddSingleton<IOccupyReReservationExecutor, OccupyReReservationExecutor>();
        services.AddSingleton<IGrabSeatCoordinator, GrabSeatCoordinator>();
        services.AddSingleton<IOccupySeatCoordinator, OccupySeatCoordinator>();
        return services;
    }
}

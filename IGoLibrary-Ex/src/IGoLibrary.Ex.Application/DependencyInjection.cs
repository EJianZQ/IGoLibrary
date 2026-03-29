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
        services.AddSingleton<IGrabSeatCoordinator, GrabSeatCoordinator>();
        services.AddSingleton<IOccupySeatCoordinator, OccupySeatCoordinator>();
        return services;
    }
}

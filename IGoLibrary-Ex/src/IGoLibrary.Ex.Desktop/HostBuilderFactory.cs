using IGoLibrary.Ex.Application;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Infrastructure;
using IGoLibrary.Ex.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop;

internal static class HostBuilderFactory
{
    public static IHostBuilder Create(string[] args, IAppLogWriter? sharedLogWriter = null)
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
                }

                services.AddApplication();
                services.AddInfrastructure();
                services.AddSingleton<IAppThemeService, AppThemeService>();
                services.AddSingleton<AppWindowService>();
                services.AddSingleton<IErrorDialogService, ErrorDialogService>();
                services.AddSingleton<ToastNotificationService>();
                services.AddSingleton<INotificationService>(serviceProvider => serviceProvider.GetRequiredService<ToastNotificationService>());
                services.AddSingleton<AlertSoundService>();
                services.AddSingleton<ITaskAlertService, TaskAlertService>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<MainWindowViewModel>();
            });
    }
}

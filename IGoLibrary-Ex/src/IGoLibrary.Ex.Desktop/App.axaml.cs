using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IGoLibrary.Ex.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            Program.Host is not null)
        {
            var services = Program.Host.Services;
            services.GetRequiredService<IAppDataInitializer>().InitializeAsync().GetAwaiter().GetResult();
            services.GetRequiredService<IAppThemeService>().InitializeAsync().GetAwaiter().GetResult();

            var mainWindow = services.GetRequiredService<MainWindow>();
            var viewModel = services.GetRequiredService<MainWindowViewModel>();

            DataContext = viewModel;
            mainWindow.DataContext = viewModel;
            desktop.MainWindow = mainWindow;
            var appWindowService = services.GetRequiredService<AppWindowService>();
            appWindowService.Attach(mainWindow);
            RegisterApplicationActivationHandler(appWindowService);
            services.GetRequiredService<IAppThemeService>().AttachTopLevel(mainWindow);

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await viewModel.InitializeAsync();
                }
                catch (Exception ex)
                {
                    services.GetRequiredService<IActivityLogService>()
                        .Write(IGoLibrary.Ex.Domain.Enums.LogEntryKind.Error, "Bootstrap", $"启动初始化失败：{ex.Message}");
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static bool ShouldRestoreMainWindowForActivation(ActivationKind kind)
    {
        return kind == ActivationKind.Reopen;
    }

    private void RegisterApplicationActivationHandler(AppWindowService appWindowService)
    {
        var activatableLifetime = ApplicationLifetime as IActivatableLifetime
            ?? TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;

        if (activatableLifetime is null)
        {
            return;
        }

        activatableLifetime.Activated += (_, args) =>
        {
            if (!ShouldRestoreMainWindowForActivation(args.Kind))
            {
                return;
            }

            Dispatcher.UIThread.Post(appWindowService.ShowMainWindow);
        };
    }
}

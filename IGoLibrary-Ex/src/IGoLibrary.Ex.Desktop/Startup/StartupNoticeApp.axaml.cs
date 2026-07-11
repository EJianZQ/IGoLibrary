using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace IGoLibrary.Ex.Desktop.Startup;

internal sealed partial class StartupNoticeApp : Avalonia.Application
{
    private static StartupNotice _notice = StartupNotice.DuplicateInstance;

    internal static void Configure(StartupNotice notice)
    {
        _notice = notice ?? throw new ArgumentNullException(nameof(notice));
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new StartupNoticeWindow(_notice);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

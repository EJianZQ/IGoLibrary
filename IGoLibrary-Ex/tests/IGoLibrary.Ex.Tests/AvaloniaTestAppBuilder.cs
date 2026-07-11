using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using IGoLibrary.Ex.Desktop.Startup;

[assembly: AvaloniaTestApplication(typeof(IGoLibrary.Ex.Tests.AvaloniaTestAppBuilder))]

namespace IGoLibrary.Ex.Tests;

public static class AvaloniaTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<StartupNoticeApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

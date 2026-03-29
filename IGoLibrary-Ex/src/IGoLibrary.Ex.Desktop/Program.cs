using Avalonia;
using Microsoft.Extensions.Hosting;

namespace IGoLibrary.Ex.Desktop;

internal static class Program
{
    public static IHost? Host { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        Host = HostBuilderFactory.Create(args).Build();
        Host.Start();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Host.StopAsync().GetAwaiter().GetResult();
            Host.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

using Avalonia;
using System.Diagnostics;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop;

internal static class Program
{
    private static bool _globalExceptionLoggingRegistered;
    private static int _skipNextUnhandledExceptionLog;
    public static IHost? Host { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        using var sharedLogWriter = new AppLogFileWriter();
        RegisterGlobalExceptionLogging(sharedLogWriter);

        try
        {
            Host = HostBuilderFactory.Create(args, sharedLogWriter).Build();
            Host.Start();
            Host.Services.GetRequiredService<TraceListenerRegistrar>().Attach();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _skipNextUnhandledExceptionLog, 1);
            sharedLogWriter.Write(LogLevel.Critical, "Bootstrap", "应用启动失败。", ex);
            sharedLogWriter.Flush();
            throw;
        }
        finally
        {
            try
            {
                Trace.Flush();
            }
            catch
            {
            }

            if (Host is not null)
            {
                try
                {
                    Host.StopAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    sharedLogWriter.Write(LogLevel.Error, "Bootstrap", "停止主机时发生异常。", ex);
                    sharedLogWriter.Flush();
                }
                finally
                {
                    try
                    {
                        Host.Dispose();
                    }
                    catch (Exception ex)
                    {
                        sharedLogWriter.Write(LogLevel.Error, "Bootstrap", "释放主机时发生异常。", ex);
                        sharedLogWriter.Flush();
                    }
                    finally
                    {
                        Host = null;
                    }
                }
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void RegisterGlobalExceptionLogging(IAppLogWriter logWriter)
    {
        if (_globalExceptionLoggingRegistered)
        {
            return;
        }

        _globalExceptionLoggingRegistered = true;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (Interlocked.Exchange(ref _skipNextUnhandledExceptionLog, 0) == 1)
            {
                return;
            }

            if (args.ExceptionObject is Exception exception)
            {
                logWriter.Write(LogLevel.Critical, "Global", "捕获到未处理的应用程序异常。", exception);
                logWriter.Flush();
                return;
            }

            logWriter.Write(
                LogLevel.Critical,
                "Global",
                $"捕获到未处理的应用程序异常：{args.ExceptionObject}");
            logWriter.Flush();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logWriter.Write(LogLevel.Error, "Global", "捕获到未观察的后台任务异常。", args.Exception);
            logWriter.Flush();
        };
    }
}

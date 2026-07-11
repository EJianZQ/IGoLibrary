using Avalonia;
using System.Diagnostics;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Startup;
using IGoLibrary.Ex.Infrastructure.Logging;
using IGoLibrary.Ex.Infrastructure.Persistence;
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
        var restartArguments = RestartArguments.Parse(args);
        new SingleInstanceStartupCoordinator(
                parentProcessId => RestartParentProcessWaiter.WaitForExitAsync(parentProcessId),
                () => SingleInstanceLock.TryAcquire(),
                ShowStartupNotice,
                RunPrimaryApplication)
            .Run(restartArguments);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    internal static AppBuilder BuildStartupNoticeApp(StartupNotice notice)
    {
        StartupNoticeApp.Configure(notice);
        return AppBuilder.Configure<StartupNoticeApp>()
            .UsePlatformDetect()
            .WithInterFont();
    }

    private static void ShowStartupNotice(StartupNotice notice)
    {
        BuildStartupNoticeApp(notice).StartWithClassicDesktopLifetime([]);
    }

    private static void RunPrimaryApplication(RestartArguments restartArguments)
    {
        var storageLocationManager = new StorageLocationManager();
        var storageLocations = storageLocationManager
            .InitializeAsync()
            .GetAwaiter()
            .GetResult();
        using var sharedLogWriter = new AppLogFileWriter(storageLocations, startUnconfigured: true);
        RegisterGlobalExceptionLogging(sharedLogWriter);
        var logWriterConfigured = false;

        try
        {
            Host = HostBuilderFactory.Create(
                    restartArguments.ApplicationArguments,
                    sharedLogWriter,
                    storageLocationManager,
                    storageLocations)
                .Build();

            Host.Services.GetRequiredService<IAppDataInitializer>()
                .InitializeAsync()
                .GetAwaiter()
                .GetResult();

            LogFileSettings loggingSettings;
            try
            {
                loggingSettings = Host.Services.GetRequiredService<ISettingsService>()
                    .LoadAsync()
                    .GetAwaiter()
                    .GetResult()
                    .Logging;
            }
            catch (Exception ex)
            {
                loggingSettings = LogFileSettings.Default;
                var fallbackResult = sharedLogWriter.ApplyAsync(loggingSettings)
                    .GetAwaiter()
                    .GetResult();
                logWriterConfigured = true;
                sharedLogWriter.Write(
                    LogLevel.Warning,
                    "Bootstrap",
                    "读取日志设置失败，已使用默认日志设置。",
                    ex);
                WriteLogCleanupWarning(sharedLogWriter, fallbackResult);
            }

            if (!logWriterConfigured)
            {
                var applyResult = sharedLogWriter.ApplyAsync(loggingSettings)
                    .GetAwaiter()
                    .GetResult();
                logWriterConfigured = true;
                WriteLogCleanupWarning(sharedLogWriter, applyResult);
            }

            Host.Start();
            Host.Services.GetRequiredService<TraceListenerRegistrar>().Attach();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(restartArguments.ApplicationArguments);
        }
        catch (Exception ex)
        {
            if (!logWriterConfigured)
            {
                sharedLogWriter.ApplyAsync(LogFileSettings.Default).GetAwaiter().GetResult();
                logWriterConfigured = true;
            }

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

    private static void WriteLogCleanupWarning(
        IAppLogWriter logWriter,
        LogRuntimeApplyResult result)
    {
        if (result.TotalDeleteFailureCount <= 0)
        {
            return;
        }

        logWriter.Write(
            LogLevel.Warning,
            "Logging",
            $"有 {result.TotalDeleteFailureCount} 个旧日志文件暂时无法清理，将在后续启动或设置变更时重试。");
    }

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

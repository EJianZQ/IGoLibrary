using Avalonia;
using System.Diagnostics;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
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
    public static string? UpdateTransactionId { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        var restartArguments = RestartArguments.Parse(args);
        BootstrapDiagnostics.Record(LogLevel.Information, "应用启动入口已执行。");
        try
        {
            new SingleInstanceStartupCoordinator(
                    parentProcessId => RestartParentProcessWaiter.WaitForExitAsync(parentProcessId),
                    () => SingleInstanceLock.TryAcquire(),
                    ShowStartupNotice,
                    RunPrimaryApplication,
                    BootstrapDiagnostics.Record)
                .Run(restartArguments);
        }
        catch (Exception ex)
        {
            BootstrapDiagnostics.Record(LogLevel.Critical, "应用在日志系统初始化前发生未处理异常。", ex);
            throw;
        }
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
        UpdateTransactionId = restartArguments.UpdateTransactionId;
        var updateMaintenanceResult = UpdateStartupMaintenanceResult.Empty;
        if (OperatingSystem.IsWindows())
        {
            updateMaintenanceResult = UpdateStartupMaintenance.Run(UpdateTransactionId);
        }
        var storageLocationManager = new StorageLocationManager
        {
            DiagnosticSink = BootstrapDiagnostics.Record
        };
        BootstrapDiagnostics.Record(LogLevel.Information, "开始解析并准备存储位置。");
        var storageLocations = storageLocationManager
            .InitializeAsync()
            .GetAwaiter()
            .GetResult();
        using var sharedLogWriter = new AppLogFileWriter(storageLocations, startUnconfigured: true);
        BootstrapDiagnostics.Attach(sharedLogWriter);
        BootstrapDiagnostics.Record(LogLevel.Information, "存储位置已准备完成，主日志写入器已接管启动日志。");
        RegisterGlobalExceptionLogging(sharedLogWriter);
        var logWriterConfigured = false;
        var restoreAwaitingCommit = false;

        try
        {
            Host = HostBuilderFactory.Create(
                    restartArguments.ApplicationArguments,
                    sharedLogWriter,
                    storageLocationManager,
                    storageLocations)
                .Build();

            var restoreStartupService = Host.Services.GetRequiredService<IBackupRestoreStartupService>();
            if (restartArguments.RestoreTransactionId is { } restoreTransactionId)
            {
                restoreAwaitingCommit = restoreStartupService.ApplyAsync(restoreTransactionId)
                    .GetAwaiter()
                    .GetResult()
                    .Succeeded;
            }
            else
            {
                restoreStartupService.RecoverIncompleteAsync()
                    .GetAwaiter()
                    .GetResult();
            }

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

            WriteUpdateMaintenanceResult(sharedLogWriter, updateMaintenanceResult);
            Host.Start();
            if (restoreAwaitingCommit && restartArguments.RestoreTransactionId is { } completedRestoreId)
            {
                restoreStartupService.CompleteAsync(completedRestoreId)
                    .GetAwaiter()
                    .GetResult();
                restoreAwaitingCommit = false;
            }
            Host.Services.GetRequiredService<TraceListenerRegistrar>().Attach();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(restartArguments.ApplicationArguments);
        }
        catch (Exception ex)
        {
            if (restoreAwaitingCommit && Host is not null)
            {
                try
                {
                    Host.Services.GetRequiredService<IBackupRestoreStartupService>()
                        .RecoverIncompleteAsync()
                        .GetAwaiter()
                        .GetResult();
                    restoreAwaitingCommit = false;
                }
                catch (Exception rollbackException)
                {
                    sharedLogWriter.Write(
                        LogLevel.Critical,
                        "Backup",
                        "恢复后的应用初始化失败，且立即回滚未能完成；下次启动会继续恢复事务。",
                        rollbackException);
                }
            }

            if (!logWriterConfigured)
            {
                try
                {
                    sharedLogWriter.ApplyAsync(LogFileSettings.Default).GetAwaiter().GetResult();
                    logWriterConfigured = true;
                }
                catch (Exception loggingConfigurationException)
                {
                    BootstrapDiagnostics.RecordEmergency(
                        LogLevel.Critical,
                        "应用启动失败时无法启用默认主日志。",
                        loggingConfigurationException);
                }
            }

            Interlocked.Exchange(ref _skipNextUnhandledExceptionLog, 1);
            WriteAndFlushSafely(
                sharedLogWriter,
                LogLevel.Critical,
                "Bootstrap",
                "应用启动失败。",
                ex);
            throw;
        }
        finally
        {
            try
            {
                Trace.Flush();
            }
            catch (Exception ex)
            {
                sharedLogWriter.Write(LogLevel.Warning, "Bootstrap", "刷新跟踪监听器失败。", ex);
            }

            if (Host is not null)
            {
                try
                {
                    Host.StopAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    WriteAndFlushSafely(
                        sharedLogWriter,
                        LogLevel.Error,
                        "Bootstrap",
                        "停止主机时发生异常。",
                        ex);
                }
                finally
                {
                    try
                    {
                        Host.Dispose();
                    }
                    catch (Exception ex)
                    {
                        WriteAndFlushSafely(
                            sharedLogWriter,
                            LogLevel.Error,
                            "Bootstrap",
                            "释放主机时发生异常。",
                            ex);
                    }
                    finally
                    {
                        Host = null;
                        UpdateTransactionId = null;
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

    private static void WriteUpdateMaintenanceResult(
        IAppLogWriter logWriter,
        UpdateStartupMaintenanceResult result)
    {
        if (result.DeletedIncompleteDownloadCount > 0 ||
            result.DeletedInvalidOrExpiredCacheCount > 0 ||
            result.RetainedVerifiedCacheCount > 0 ||
            result.RestoredVerifiedCacheCount > 0 ||
            result.DeletedUpdaterTransactionCount > 0 ||
            result.DeletedLogCount > 0)
        {
            logWriter.Write(
                LogLevel.Information,
                "UpdateMaintenance",
                $"更新启动维护完成：清理未完成下载 {result.DeletedIncompleteDownloadCount} 个，" +
                $"清理损坏或过期验签缓存 {result.DeletedInvalidOrExpiredCacheCount} 个，" +
                $"保留有效验签缓存 {result.RetainedVerifiedCacheCount} 个，" +
                $"恢复中断交接的验签缓存 {result.RestoredVerifiedCacheCount} 个，" +
                $"清理终态更新事务 {result.DeletedUpdaterTransactionCount} 个，" +
                $"清理旧更新日志 {result.DeletedLogCount} 个。");
        }

        foreach (var failure in result.Failures)
        {
            logWriter.Write(
                LogLevel.Warning,
                "UpdateMaintenance",
                $"{failure.Operation}：{failure.Item}。将在后续启动重试。",
                failure.Exception);
        }
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
                WriteAndFlushSafely(
                    logWriter,
                    LogLevel.Critical,
                    "Global",
                    "捕获到未处理的应用程序异常。",
                    exception);
                return;
            }

            WriteAndFlushSafely(
                logWriter,
                LogLevel.Critical,
                "Global",
                $"捕获到未处理的应用程序异常：{args.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteAndFlushSafely(
                logWriter,
                LogLevel.Error,
                "Global",
                "捕获到未观察的后台任务异常。",
                args.Exception);
        };
    }

    private static void WriteAndFlushSafely(
        IAppLogWriter logWriter,
        LogLevel level,
        string category,
        string message,
        Exception? exception = null)
    {
        try
        {
            logWriter.Write(level, category, message, exception);
            logWriter.Flush();
        }
        catch (Exception loggingException)
        {
            BootstrapDiagnostics.RecordEmergency(
                LogLevel.Critical,
                "主日志紧急写入或刷新失败。",
                loggingException);
            BootstrapDiagnostics.RecordEmergency(level, message, exception);
        }
    }
}

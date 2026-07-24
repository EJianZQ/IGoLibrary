using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Updater.Core;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class UpdateStartupHealthReporter
{
    public static void ReportReady(
        string transactionId,
        IAppVersionProvider appVersionProvider,
        IAppLogWriter? logWriter = null)
    {
        var transactionDirectory = GetTransactionDirectory(transactionId);
        var reportPath = Path.Combine(transactionDirectory, "health.json");
        UpdateJsonFile.WriteAtomic(
            reportPath,
            new UpdateHealthReport(
                UpdateProtocol.SchemaVersion,
                transactionId,
                appVersionProvider.CurrentVersionText,
                Environment.ProcessId,
                DateTimeOffset.UtcNow),
            UpdateJsonTypeInfo.HealthReport);
        logWriter?.Write(
            LogLevel.Information,
            "Update.Health",
            $"更新启动健康报告已写入。事务标识={transactionId}，版本={appVersionProvider.CurrentVersionText}，进程标识={Environment.ProcessId}。",
            eventId: new EventId(7101, "UpdateHealthReady"));
    }

    public static async Task ObserveCompletionAsync(
        string transactionId,
        IAppVersionProvider appVersionProvider,
        INotificationService notificationService,
        IAppLogWriter? logWriter = null)
    {
        var transactionDirectory = GetTransactionDirectory(transactionId);

        var statusPath = Path.Combine(transactionDirectory, "worker-status.json");
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
        var lastIoFailureLoggedAt = DateTimeOffset.MinValue;
        logWriter?.Write(
            LogLevel.Information,
            "Update.Health",
            $"开始观察更新事务完成状态。事务标识={transactionId}。",
            eventId: new EventId(7102, "UpdateHealthObserveStarted"));
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(statusPath))
            {
                try
                {
                    var status = UpdateJsonFile.Read(statusPath, UpdateJsonTypeInfo.WorkerStatus);
                    if (string.Equals(status.TransactionId, transactionId, StringComparison.Ordinal))
                    {
                        if (status.Phase == UpdateWorkerPhase.Committed)
                        {
                            logWriter?.Write(
                                LogLevel.Information,
                                "Update.Health",
                                $"更新事务已提交并通过新进程验证。事务标识={transactionId}，版本={appVersionProvider.CurrentVersionText}。",
                                eventId: new EventId(7103, "UpdateHealthCommitted"));
                            await notificationService.ShowSuccessAsync(
                                $"已更新到 v{appVersionProvider.CurrentVersionText}",
                                "新版本已启动并通过验证");
                            return;
                        }

                        if (status.Phase is UpdateWorkerPhase.RolledBack or UpdateWorkerPhase.Failed)
                        {
                            logWriter?.Write(
                                LogLevel.Warning,
                                "Update.Health",
                                $"更新事务未提交。事务标识={transactionId}，阶段={status.Phase}。",
                                eventId: new EventId(7104, "UpdateHealthNotCommitted"));
                            return;
                        }
                    }
                }
                catch (IOException ex)
                {
                    var now = DateTimeOffset.UtcNow;
                    if (now - lastIoFailureLoggedAt >= TimeSpan.FromSeconds(10))
                    {
                        lastIoFailureLoggedAt = now;
                        logWriter?.Write(
                            LogLevel.Warning,
                            "Update.Health",
                            $"读取更新事务状态文件失败，将继续重试。事务标识={transactionId}。",
                            ex,
                            new EventId(7105, "UpdateHealthStatusReadFailed"));
                    }
                }
            }

            await Task.Delay(250);
        }

        logWriter?.Write(
            LogLevel.Warning,
            "Update.Health",
            $"观察更新事务完成状态超时。事务标识={transactionId}，超时秒数=120。",
            eventId: new EventId(7106, "UpdateHealthObserveTimedOut"));
    }

    private static string GetTransactionDirectory(string transactionId)
    {
        if (!Guid.TryParseExact(transactionId, "N", out var parsed))
        {
            throw new InvalidDataException("更新事务标识无效");
        }

        var updatesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UpdateProtocol.ProductName,
            "updates");
        var transactionDirectory = Path.Combine(updatesRoot, parsed.ToString("N"));
        if (!Directory.Exists(transactionDirectory))
        {
            throw new DirectoryNotFoundException("更新事务目录不存在");
        }

        UpdatePathSafety.RejectReparsePoint(transactionDirectory);
        return transactionDirectory;
    }
}

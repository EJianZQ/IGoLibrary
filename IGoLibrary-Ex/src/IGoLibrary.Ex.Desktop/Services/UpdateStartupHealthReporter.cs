using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class UpdateStartupHealthReporter
{
    public static void ReportReady(
        string transactionId,
        IAppVersionProvider appVersionProvider)
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
    }

    public static async Task ObserveCompletionAsync(
        string transactionId,
        IAppVersionProvider appVersionProvider,
        INotificationService notificationService)
    {
        var transactionDirectory = GetTransactionDirectory(transactionId);

        var statusPath = Path.Combine(transactionDirectory, "worker-status.json");
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
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
                            await notificationService.ShowSuccessAsync(
                                $"已更新到 v{appVersionProvider.CurrentVersionText}",
                                "新版本已启动并通过验证");
                            return;
                        }

                        if (status.Phase is UpdateWorkerPhase.RolledBack or UpdateWorkerPhase.Failed)
                        {
                            return;
                        }
                    }
                }
                catch (IOException)
                {
                }
            }

            await Task.Delay(250);
        }
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

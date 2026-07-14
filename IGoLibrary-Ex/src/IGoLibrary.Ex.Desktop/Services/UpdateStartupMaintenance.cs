using System.Diagnostics;
using System.Runtime.Versioning;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Desktop.Services;

[SupportedOSPlatform("windows")]
internal static class UpdateStartupMaintenance
{
    private static readonly TimeSpan TransactionRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan LogRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan MaximumFutureCacheSkew = TimeSpan.FromMinutes(5);

    public static UpdateStartupMaintenanceResult Run(string? currentTransactionId)
    {
        return RunCore(
            WindowsUpdateWorkspaceManager.GetUpdatesRoot(),
            currentTransactionId,
            DateTimeOffset.UtcNow);
    }

    internal static UpdateStartupMaintenanceResult RunForTests(
        string updatesRoot,
        string? currentTransactionId,
        DateTimeOffset now)
    {
        return RunCore(Path.GetFullPath(updatesRoot), currentTransactionId, now);
    }

    private static UpdateStartupMaintenanceResult RunCore(
        string updatesRoot,
        string? currentTransactionId,
        DateTimeOffset now)
    {
        var result = new ResultBuilder();
        try
        {
            if (!Directory.Exists(updatesRoot))
            {
                return result.Build();
            }

            UpdatePathSafety.RejectReparsePoint(updatesRoot);
            CleanOldLogs(Path.Combine(updatesRoot, "logs"), result);
            foreach (var directory in Directory.EnumerateDirectories(updatesRoot))
            {
                var transactionId = Path.GetFileName(directory);
                if (!Guid.TryParseExact(transactionId, "N", out _) ||
                    string.Equals(transactionId, currentTransactionId, StringComparison.Ordinal))
                {
                    continue;
                }

                TryCleanTransaction(updatesRoot, directory, transactionId, now, result);
            }
        }
        catch (Exception exception)
        {
            result.AddFailure("updates", "枚举更新目录失败", exception);
        }

        return result.Build();
    }

    private static void TryCleanTransaction(
        string updatesRoot,
        string directory,
        string transactionId,
        DateTimeOffset now,
        ResultBuilder result)
    {
        try
        {
            EnsureSafeTransactionDirectory(updatesRoot, directory, transactionId);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                DeleteTransactionDirectory(directory);
                result.DeletedIncompleteDownloadCount++;
                return;
            }

            var requestPath = Path.Combine(directory, "request.json");
            if (!File.Exists(requestPath))
            {
                CleanDownloadWorkspace(directory, transactionId, now, result);
                return;
            }

            CleanUpdaterTransaction(directory, transactionId, requestPath, now, result);
        }
        catch (Exception exception)
        {
            result.AddFailure(transactionId, "清理更新事务失败", exception);
        }
    }

    private static void CleanDownloadWorkspace(
        string directory,
        string transactionId,
        DateTimeOffset now,
        ResultBuilder result)
    {
        var cachePath = Path.Combine(directory, "verified-cache.json");
        if (!File.Exists(cachePath))
        {
            DeleteTransactionDirectory(directory);
            result.DeletedIncompleteDownloadCount++;
            return;
        }

        try
        {
            UpdatePathSafety.RejectReparsePoint(cachePath);
            var cache = UpdateJsonFile.Read<VerifiedUpdateCache>(cachePath);
            var structurallyValid = VerifiedUpdateCache.IsStructurallyValid(
                cache,
                transactionId);
            var requiredFilesExist =
                File.Exists(Path.Combine(directory, "package.zip")) &&
                Directory.Exists(Path.Combine(directory, "staging")) &&
                File.Exists(Path.Combine(
                    directory,
                    "staging",
                    UpdateProtocol.ManifestFileName));
            if (requiredFilesExist)
            {
                UpdatePathSafety.RejectReparsePoint(Path.Combine(directory, "package.zip"));
                UpdatePathSafety.RejectReparsePoint(Path.Combine(directory, "staging"));
                UpdatePathSafety.RejectReparsePoint(Path.Combine(
                    directory,
                    "staging",
                    UpdateProtocol.ManifestFileName));
            }
            var age = now - cache.VerifiedAtUtc;
            if (!structurallyValid ||
                !requiredFilesExist ||
                cache.VerifiedAtUtc > now + MaximumFutureCacheSkew ||
                age >= WindowsUpdateWorkspaceManager.VerifiedCacheRetention)
            {
                DeleteTransactionDirectory(directory);
                result.DeletedInvalidOrExpiredCacheCount++;
                return;
            }

            result.RetainedVerifiedCacheCount++;
        }
        catch (Exception exception) when (exception is not UnauthorizedAccessException)
        {
            try
            {
                DeleteTransactionDirectory(directory);
                result.DeletedInvalidOrExpiredCacheCount++;
            }
            catch (Exception cleanupException)
            {
                result.AddFailure(
                    transactionId,
                    "验签缓存损坏且无法清理",
                    new AggregateException(exception, cleanupException));
            }
        }
    }

    private static void CleanUpdaterTransaction(
        string directory,
        string transactionId,
        string requestPath,
        DateTimeOffset now,
        ResultBuilder result)
    {
        var request = UpdateJsonFile.Read<UpdateTransactionRequest>(requestPath);
        UpdateTransaction.ValidateRequestFile(requestPath, request);
        if (!string.Equals(request.TransactionId, transactionId, StringComparison.Ordinal) ||
            IsProcessAlive(request.ParentProcessId) ||
            HasLiveLaunchedProcess(request))
        {
            return;
        }

        var secureWorkingDirectory = UpdateTransaction.GetSecureWorkingDirectory(request);
        var preparationWorkspace = request.CandidateDirectory + ".workspace";
        if (!Directory.Exists(request.BackupDirectory) &&
            !Directory.Exists(request.CandidateDirectory) &&
            !Directory.Exists(preparationWorkspace) &&
            !Directory.Exists(secureWorkingDirectory) &&
            IsStaleHeartbeat(request))
        {
            UpdateRecoveryRegistration.Unregister(transactionId);
        }

        var statusPath = request.WorkerStatusPath;
        if (!File.Exists(statusPath))
        {
            var signalPath = request.CoordinatorReadyPath;
            if (File.Exists(signalPath))
            {
                var signal = UpdateJsonFile.Read<UpdateCoordinatorSignal>(signalPath);
                if (string.Equals(signal.TransactionId, transactionId, StringComparison.Ordinal) &&
                    signal.Signal is UpdateCoordinatorSignalKind.Canceled or UpdateCoordinatorSignalKind.Failed &&
                    now - signal.CreatedAtUtc >= TransactionRetention &&
                    !Directory.Exists(request.CandidateDirectory) &&
                    !Directory.Exists(preparationWorkspace) &&
                    !Directory.Exists(request.BackupDirectory) &&
                    !Directory.Exists(secureWorkingDirectory))
                {
                    UpdateRecoveryRegistration.Unregister(transactionId);
                    DeleteTransactionDirectory(directory);
                    result.DeletedUpdaterTransactionCount++;
                }
            }

            return;
        }

        var status = UpdateJsonFile.Read<UpdateWorkerStatus>(statusPath);
        if (!string.Equals(status.TransactionId, transactionId, StringComparison.Ordinal) ||
            now - status.UpdatedAtUtc < TransactionRetention)
        {
            return;
        }

        if (status.Phase == UpdateWorkerPhase.Failed)
        {
            if (!Directory.Exists(request.BackupDirectory) &&
                !Directory.Exists(request.CandidateDirectory) &&
                !Directory.Exists(preparationWorkspace) &&
                !Directory.Exists(secureWorkingDirectory))
            {
                UpdateRecoveryRegistration.Unregister(transactionId);
                DeleteTransactionDirectory(directory);
                result.DeletedUpdaterTransactionCount++;
            }

            return;
        }

        if (status.Phase is not (UpdateWorkerPhase.Committed or UpdateWorkerPhase.RolledBack) ||
            Directory.Exists(secureWorkingDirectory))
        {
            return;
        }

        if (status.Phase == UpdateWorkerPhase.Committed)
        {
            UpdateTransaction.Commit(request);
        }
        else
        {
            UpdateTransaction.CleanupRollbackArtifacts(request);
        }

        UpdateRecoveryRegistration.Unregister(transactionId);
        DeleteTransactionDirectory(directory);
        result.DeletedUpdaterTransactionCount++;
    }

    private static void EnsureSafeTransactionDirectory(
        string updatesRoot,
        string directory,
        string transactionId)
    {
        var root = Path.GetFullPath(updatesRoot);
        var target = Path.GetFullPath(directory);
        if (!string.Equals(Path.GetDirectoryName(target), root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(target), transactionId, StringComparison.Ordinal) ||
            !Guid.TryParseExact(transactionId, "N", out _))
        {
            throw new InvalidDataException("更新事务目录越出 updates 根目录");
        }
    }

    private static void DeleteTransactionDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        WindowsUpdateWorkspaceManager.DeleteDirectoryWithoutFollowingReparsePoints(directory);
    }

    private static bool HasLiveLaunchedProcess(UpdateTransactionRequest request)
    {
        try
        {
            if (!File.Exists(request.LaunchedProcessPath))
            {
                return false;
            }

            var launched = UpdateJsonFile.Read<UpdateLaunchedProcessInfo>(request.LaunchedProcessPath);
            return string.Equals(launched.TransactionId, request.TransactionId, StringComparison.Ordinal) &&
                   IsProcessAlive(launched.ProcessId);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static void CleanOldLogs(string logDirectory, ResultBuilder result)
    {
        if (!Directory.Exists(logDirectory))
        {
            return;
        }

        if ((File.GetAttributes(logDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            result.AddFailure(
                "logs",
                "拒绝清理重解析点日志目录",
                new InvalidDataException("更新日志目录不能是符号链接或目录联接"));
            return;
        }

        foreach (var path in Directory.EnumerateFiles(logDirectory, "*.log"))
        {
            try
            {
                if (IsOlderThan(path, LogRetention))
                {
                    File.Delete(path);
                    result.DeletedLogCount++;
                }
            }
            catch (Exception exception)
            {
                result.AddFailure(Path.GetFileName(path), "清理旧更新日志失败", exception);
            }
        }
    }

    private static bool IsOlderThan(string path, TimeSpan retention)
    {
        return DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path) >= retention;
    }

    private static bool IsStaleHeartbeat(UpdateTransactionRequest request)
    {
        try
        {
            var path = File.Exists(request.HeartbeatPath)
                ? request.HeartbeatPath
                : Path.Combine(Path.GetDirectoryName(request.HealthReportPath)!, "request.json");
            return IsOlderThan(path, TimeSpan.FromMinutes(1));
        }
        catch
        {
            return false;
        }
    }

    private sealed class ResultBuilder
    {
        private readonly List<UpdateStartupMaintenanceFailure> _failures = [];

        public int DeletedIncompleteDownloadCount { get; set; }

        public int DeletedInvalidOrExpiredCacheCount { get; set; }

        public int RetainedVerifiedCacheCount { get; set; }

        public int DeletedUpdaterTransactionCount { get; set; }

        public int DeletedLogCount { get; set; }

        public void AddFailure(string item, string operation, Exception exception)
        {
            _failures.Add(new UpdateStartupMaintenanceFailure(item, operation, exception));
        }

        public UpdateStartupMaintenanceResult Build()
        {
            return new UpdateStartupMaintenanceResult(
                DeletedIncompleteDownloadCount,
                DeletedInvalidOrExpiredCacheCount,
                RetainedVerifiedCacheCount,
                DeletedUpdaterTransactionCount,
                DeletedLogCount,
                _failures.ToArray());
        }
    }
}

internal sealed record UpdateStartupMaintenanceResult(
    int DeletedIncompleteDownloadCount,
    int DeletedInvalidOrExpiredCacheCount,
    int RetainedVerifiedCacheCount,
    int DeletedUpdaterTransactionCount,
    int DeletedLogCount,
    IReadOnlyList<UpdateStartupMaintenanceFailure> Failures)
{
    public static UpdateStartupMaintenanceResult Empty { get; } = new(0, 0, 0, 0, 0, []);
}

internal sealed record UpdateStartupMaintenanceFailure(
    string Item,
    string Operation,
    Exception Exception);

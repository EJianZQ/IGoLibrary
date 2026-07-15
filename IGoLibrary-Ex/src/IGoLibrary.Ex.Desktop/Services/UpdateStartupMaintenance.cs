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
                CleanDownloadWorkspace(
                    updatesRoot,
                    directory,
                    transactionId,
                    now,
                    result);
                return;
            }

            CleanUpdaterTransaction(
                updatesRoot,
                directory,
                transactionId,
                requestPath,
                now,
                result);
        }
        catch (Exception exception)
        {
            result.AddFailure(transactionId, "清理更新事务失败", exception);
        }
    }

    private static void CleanDownloadWorkspace(
        string updatesRoot,
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
            var cache = WindowsUpdateWorkspaceManager.ReadValidVerifiedCacheLayout(
                directory,
                transactionId);
            var age = now - cache.VerifiedAtUtc;
            if (cache.VerifiedAtUtc > now + MaximumFutureCacheSkew ||
                age >= WindowsUpdateWorkspaceManager.VerifiedCacheRetention)
            {
                DeleteTransactionDirectory(directory);
                result.DeletedInvalidOrExpiredCacheCount++;
                return;
            }

            if (!WindowsUpdateWorkspaceManager.IsPureVerifiedCacheDirectory(directory))
            {
                try
                {
                    WindowsUpdateWorkspaceManager.RestoreVerifiedCacheDirectory(
                        updatesRoot,
                        directory,
                        transactionId);
                    result.RestoredVerifiedCacheCount++;
                }
                catch (Exception exception)
                {
                    result.AddFailure(
                        transactionId,
                        "清理验签缓存中的中断交接产物失败",
                        exception);
                }

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
        string updatesRoot,
        string directory,
        string transactionId,
        string requestPath,
        DateTimeOffset now,
        ResultBuilder result)
    {
        var request = UpdateJsonFile.Read(requestPath, UpdateJsonTypeInfo.TransactionRequest);
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
            IsStaleHeartbeat(request, now))
        {
            UpdateRecoveryRegistration.Unregister(transactionId);
        }

        var statusPath = request.WorkerStatusPath;
        UpdateWorkerStatus? status = null;
        if (File.Exists(statusPath))
        {
            status = UpdateJsonFile.Read(statusPath, UpdateJsonTypeInfo.WorkerStatus);
            if (!string.Equals(status.TransactionId, transactionId, StringComparison.Ordinal))
            {
                return;
            }
        }

        UpdateCoordinatorSignal? signal = null;
        var signalPath = request.CoordinatorReadyPath;
        if (File.Exists(signalPath))
        {
            signal = UpdateJsonFile.Read(signalPath, UpdateJsonTypeInfo.CoordinatorSignal);
            if (signal.SchemaVersion != UpdateProtocol.SchemaVersion ||
                !string.Equals(signal.TransactionId, transactionId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("更新协调器信号无效");
            }
        }

        if (TryRestoreAbortedVerifiedCache(
                updatesRoot,
                directory,
                transactionId,
                request,
                status,
                signal,
                preparationWorkspace,
                secureWorkingDirectory,
                now,
                result))
        {
            return;
        }

        if (status is null)
        {
            if (signal?.Signal is UpdateCoordinatorSignalKind.Canceled or UpdateCoordinatorSignalKind.Failed &&
                now - signal.CreatedAtUtc >= TransactionRetention &&
                !Directory.Exists(request.CandidateDirectory) &&
                !Directory.Exists(preparationWorkspace) &&
                !Directory.Exists(request.BackupDirectory) &&
                !Directory.Exists(secureWorkingDirectory))
            {
                if (HasRetainableVerifiedCache(directory, transactionId, now))
                {
                    return;
                }

                UpdateRecoveryRegistration.Unregister(transactionId);
                DeleteTransactionDirectory(directory);
                result.DeletedUpdaterTransactionCount++;
            }

            return;
        }

        if (now - status.UpdatedAtUtc < TransactionRetention)
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
                if (HasRetainableVerifiedCache(directory, transactionId, now))
                {
                    return;
                }

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

    private static bool TryRestoreAbortedVerifiedCache(
        string updatesRoot,
        string directory,
        string transactionId,
        UpdateTransactionRequest request,
        UpdateWorkerStatus? status,
        UpdateCoordinatorSignal? signal,
        string preparationWorkspace,
        string secureWorkingDirectory,
        DateTimeOffset now,
        ResultBuilder result)
    {
        var signalAllowsRestore =
            signal is null ||
            signal.Signal is
                UpdateCoordinatorSignalKind.Canceled or
                UpdateCoordinatorSignalKind.Failed;
        var statusAllowsRestore =
            status is null ||
            status.Phase is
                UpdateWorkerPhase.Starting or
                UpdateWorkerPhase.Ready or
                UpdateWorkerPhase.WaitingForParent or
                UpdateWorkerPhase.Failed;
        if (!signalAllowsRestore ||
            !statusAllowsRestore ||
            Directory.Exists(request.CandidateDirectory) ||
            Directory.Exists(preparationWorkspace) ||
            Directory.Exists(request.BackupDirectory) ||
            Directory.Exists(secureWorkingDirectory) ||
            !IsStaleHeartbeat(request, now) ||
            !File.Exists(Path.Combine(directory, "verified-cache.json")))
        {
            return false;
        }

        try
        {
            var cache = WindowsUpdateWorkspaceManager.ReadValidVerifiedCacheLayout(
                directory,
                transactionId);
            if (cache.VerifiedAtUtc > now + MaximumFutureCacheSkew ||
                now - cache.VerifiedAtUtc >= WindowsUpdateWorkspaceManager.VerifiedCacheRetention)
            {
                return false;
            }

            WindowsUpdateWorkspaceManager.RestoreVerifiedCacheDirectory(
                updatesRoot,
                directory,
                transactionId);
            UpdateRecoveryRegistration.Unregister(transactionId);
            result.RestoredVerifiedCacheCount++;
            return true;
        }
        catch (Exception exception)
        {
            result.AddFailure(transactionId, "恢复中断交接的验签缓存失败", exception);
            return false;
        }
    }

    private static bool HasRetainableVerifiedCache(
        string directory,
        string transactionId,
        DateTimeOffset now)
    {
        try
        {
            var cache = WindowsUpdateWorkspaceManager.ReadValidVerifiedCacheLayout(
                directory,
                transactionId);
            return cache.VerifiedAtUtc <= now + MaximumFutureCacheSkew &&
                   now - cache.VerifiedAtUtc < WindowsUpdateWorkspaceManager.VerifiedCacheRetention;
        }
        catch
        {
            return false;
        }
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

            var launched = UpdateJsonFile.Read(
                request.LaunchedProcessPath,
                UpdateJsonTypeInfo.LaunchedProcessInfo);
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

    private static bool IsStaleHeartbeat(
        UpdateTransactionRequest request,
        DateTimeOffset now)
    {
        try
        {
            var path = File.Exists(request.HeartbeatPath)
                ? request.HeartbeatPath
                : Path.Combine(Path.GetDirectoryName(request.HealthReportPath)!, "request.json");
            return now - File.GetLastWriteTimeUtc(path) >= TimeSpan.FromMinutes(1);
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

        public int RestoredVerifiedCacheCount { get; set; }

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
                RestoredVerifiedCacheCount,
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
    int RestoredVerifiedCacheCount,
    int DeletedUpdaterTransactionCount,
    int DeletedLogCount,
    IReadOnlyList<UpdateStartupMaintenanceFailure> Failures)
{
    public static UpdateStartupMaintenanceResult Empty { get; } = new(0, 0, 0, 0, 0, 0, []);
}

internal sealed record UpdateStartupMaintenanceFailure(
    string Item,
    string Operation,
    Exception Exception);

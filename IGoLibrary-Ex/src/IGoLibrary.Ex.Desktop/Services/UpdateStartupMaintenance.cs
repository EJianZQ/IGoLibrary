using System.Diagnostics;
using System.Runtime.Versioning;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Desktop.Services;

[SupportedOSPlatform("windows")]
internal static class UpdateStartupMaintenance
{
    private static readonly TimeSpan TransactionRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan LogRetention = TimeSpan.FromDays(30);

    public static void Run(string? currentTransactionId)
    {
        try
        {
            var updatesRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                UpdateProtocol.ProductName,
                "updates");
            if (!Directory.Exists(updatesRoot))
            {
                return;
            }

            CleanOldLogs(Path.Combine(updatesRoot, "logs"));
            foreach (var directory in Directory.EnumerateDirectories(updatesRoot))
            {
                var transactionId = Path.GetFileName(directory);
                if (!Guid.TryParseExact(transactionId, "N", out _) ||
                    string.Equals(transactionId, currentTransactionId, StringComparison.Ordinal))
                {
                    continue;
                }

                TryCleanTransaction(directory, transactionId);
            }
        }
        catch
        {
        }
    }

    private static void TryCleanTransaction(string directory, string transactionId)
    {
        try
        {
            var requestPath = Path.Combine(directory, "request.json");
            if (!File.Exists(requestPath))
            {
                var cachePath = Path.Combine(directory, "verified-cache.json");
                if (File.Exists(cachePath) && IsOlderThan(cachePath, TransactionRetention))
                {
                    Directory.Delete(directory, recursive: true);
                }

                return;
            }

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
                         DateTimeOffset.UtcNow - signal.CreatedAtUtc >= TransactionRetention &&
                         !Directory.Exists(request.CandidateDirectory) &&
                         !Directory.Exists(preparationWorkspace) &&
                         !Directory.Exists(request.BackupDirectory) &&
                         !Directory.Exists(secureWorkingDirectory))
                    {
                        UpdateRecoveryRegistration.Unregister(transactionId);
                        Directory.Delete(directory, recursive: true);
                    }
                }

                return;
            }

            var status = UpdateJsonFile.Read<UpdateWorkerStatus>(statusPath);
            if (!string.Equals(status.TransactionId, transactionId, StringComparison.Ordinal) ||
                DateTimeOffset.UtcNow - status.UpdatedAtUtc < TransactionRetention)
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
                    Directory.Delete(directory, recursive: true);
                }

                return;
            }

            if (status.Phase is not (UpdateWorkerPhase.Committed or UpdateWorkerPhase.RolledBack))
            {
                return;
            }

            if (Directory.Exists(secureWorkingDirectory))
            {
                return;
            }

            if (status.Phase == UpdateWorkerPhase.Committed)
            {
                try
                {
                    UpdateTransaction.Commit(request);
                }
                catch
                {
                    return;
                }
            }
            else
            {
                try
                {
                    UpdateTransaction.CleanupRollbackArtifacts(request);
                }
                catch
                {
                    return;
                }
            }

            UpdateRecoveryRegistration.Unregister(transactionId);
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
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

    private static void CleanOldLogs(string logDirectory)
    {
        if (!Directory.Exists(logDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(logDirectory, "*.log"))
        {
            try
            {
                if (IsOlderThan(path, LogRetention))
                {
                    File.Delete(path);
                }
            }
            catch
            {
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
                : Path.Combine(
                    Path.GetDirectoryName(request.HealthReportPath)!,
                    "request.json");
            return IsOlderThan(path, TimeSpan.FromMinutes(1));
        }
        catch
        {
            return false;
        }
    }
}

using System.Diagnostics;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Updater;

internal static class WorkerRunner
{
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DecisionTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan CoordinatorHeartbeatTimeout = TimeSpan.FromSeconds(15);

    public static async Task<int> RunAsync(string requestPath)
    {
        UpdateTransactionRequest? request = null;
        UpdaterLog? log = null;
        var applied = false;
        try
        {
            request = UpdateJsonFile.Read(requestPath, UpdateJsonTypeInfo.TransactionRequest);
            UpdateTransaction.ValidateRequestFile(requestPath, request);
            UpdateTransaction.ValidateRequest(request);
            using var parentProcess = GetValidatedParentProcess(request);
            await using var packageLease = new FileStream(
                request.PackagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (packageLease.Length != request.PackageSize)
            {
                throw new InvalidDataException("更新包大小与可信事务不一致");
            }

            log = new UpdaterLog(
                GetWorkerLogDirectory(request),
                request.TransactionId,
                "worker");
            log.Info("更新工作进程已启动。");

            WriteStatus(request, UpdateWorkerPhase.Starting, "正在复核更新请求…");
            EnsureCoordinatorAlive(request);
            WriteStatus(request, UpdateWorkerPhase.Ready, "已准备好，等待应用退出");
            UpdateJsonFile.WriteAtomic(
                request.WorkerReadyPath,
                new UpdateCoordinatorSignal(
                    UpdateProtocol.SchemaVersion,
                    request.TransactionId,
                    UpdateCoordinatorSignalKind.Ready,
                    "worker-ready",
                    DateTimeOffset.UtcNow),
                UpdateJsonTypeInfo.CoordinatorSignal);

            WriteStatus(request, UpdateWorkerPhase.WaitingForParent, "正在等待应用安全退出…");
            if (!await WaitForProcessExitAsync(parentProcess, request, ParentExitTimeout))
            {
                throw new TimeoutException("等待旧版本退出超时，安装目录未被修改");
            }

            EnsureCoordinatorAlive(request);
            WriteStatus(request, UpdateWorkerPhase.Preparing, "正在构建新版本候选目录…");
            await UpdateTransaction.PrepareCandidateFromArchiveAsync(request, packageLease);

            EnsureCoordinatorAlive(request);
            WriteStatus(request, UpdateWorkerPhase.Applying, "正在替换程序文件…");
            UpdateTransaction.Apply(request);
            applied = true;
            WriteStatus(request, UpdateWorkerPhase.Applied, "新版本文件已应用，等待启动验证");

            var decision = await WaitForDecisionAsync(request, DecisionTimeout);
            if (decision?.Decision == UpdateDecisionKind.Commit)
            {
                WriteStatus(request, UpdateWorkerPhase.Committing, "正在提交更新…");
                try
                {
                    UpdateTransaction.Commit(request);
                }
                catch (Exception cleanupException)
                {
                    log.Error("更新成功，但备份清理失败。", cleanupException);
                }
                WriteStatus(request, UpdateWorkerPhase.Committed, "更新已完成");
                RecoveryRunner.ScheduleSecureCleanup(
                    request,
                    requestPath,
                    [Environment.ProcessId]);

                log.Info("更新工作进程已提交更新。");
                return 0;
            }

            WriteStatus(request, UpdateWorkerPhase.RollingBack, "新版本验证失败，正在恢复旧版本…");
            TryStopLaunchedProcess(request, log);
            await RollbackWithRetriesAsync(request, log);
            applied = false;
            WriteStatus(request, UpdateWorkerPhase.RolledBack, "旧版本已恢复");
            TryCleanupRollbackArtifacts(request, log);
            RecoveryRunner.ScheduleSecureCleanup(
                request,
                requestPath,
                [Environment.ProcessId]);
            log.Info("更新工作进程已回滚更新。");
            return 0;
        }
        catch (Exception exception)
        {
            log?.Error("更新工作进程运行失败。", exception);
            if (request is not null &&
                (applied || Directory.Exists(request.BackupDirectory)))
            {
                try
                {
                    WriteStatus(request, UpdateWorkerPhase.RollingBack, "更新异常，正在恢复旧版本…");
                    TryStopLaunchedProcess(request, log);
                    await RollbackWithRetriesAsync(request, log);
                    WriteStatus(request, UpdateWorkerPhase.RolledBack, "旧版本已恢复");
                    TryCleanupRollbackArtifacts(request, log);
                    RecoveryRunner.ScheduleSecureCleanup(
                        request,
                        requestPath,
                        [Environment.ProcessId]);
                    return 1;
                }
                catch (Exception rollbackException)
                {
                    log?.Error("回滚失败。", rollbackException);
                    exception = new AggregateException(exception, rollbackException);
                }
            }

            if (request is not null)
            {
                if (!Directory.Exists(request.BackupDirectory) &&
                    Directory.Exists(request.InstallationDirectory))
                {
                    TryCleanupRollbackArtifacts(request, log);
                    RecoveryRunner.ScheduleSecureCleanup(
                        request,
                        requestPath,
                        [Environment.ProcessId]);
                }

                TryWriteFailure(request, exception.Message);
            }

            return 1;
        }
    }

    private static Process GetValidatedParentProcess(UpdateTransactionRequest request)
    {
        var process = Process.GetProcessById(request.ParentProcessId);
        try
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException("主程序已在更新组件就绪前退出");
            }

            var expectedExecutable = Path.GetFullPath(
                Path.Combine(request.InstallationDirectory, request.EntryExecutable));
            var actualExecutable = process.MainModule?.FileName;
            var actualStartedAt = new DateTimeOffset(
                process.StartTime.ToUniversalTime(),
                TimeSpan.Zero);
            if (string.IsNullOrWhiteSpace(actualExecutable) ||
                !string.Equals(
                    Path.GetFullPath(actualExecutable),
                    expectedExecutable,
                    StringComparison.OrdinalIgnoreCase) ||
                Math.Abs((actualStartedAt - request.ParentProcessStartedAtUtc).TotalSeconds) > 2)
            {
                throw new InvalidDataException("更新请求绑定的主程序进程身份无效");
            }

            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static async Task<bool> WaitForProcessExitAsync(
        Process process,
        UpdateTransactionRequest request,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            EnsureCoordinatorAlive(request);
            process.Refresh();
            if (process.HasExited)
            {
                return true;
            }

            await Task.Delay(500);
        }

        return false;
    }

    private static void EnsureExactPath(string actual, string expected)
    {
        if (!string.Equals(
                Path.GetFullPath(actual),
                Path.GetFullPath(expected),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"更新事务路径不符合安全约束：{actual}");
        }
    }

    private static async Task<UpdateDecision?> WaitForDecisionAsync(
        UpdateTransactionRequest request,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(request.DecisionPath))
            {
                var decision = UpdateJsonFile.Read(request.DecisionPath, UpdateJsonTypeInfo.Decision);
                if (decision.SchemaVersion != UpdateProtocol.SchemaVersion ||
                    !string.Equals(decision.TransactionId, request.TransactionId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("协调器决策内容无效");
                }

                return decision;
            }

            if (!HasRecentHeartbeat(request))
            {
                return null;
            }

            await Task.Delay(500);
        }

        return null;
    }

    private static void EnsureCoordinatorAlive(UpdateTransactionRequest request)
    {
        if (!HasRecentHeartbeat(request))
        {
            throw new IOException("更新协调器已失联，安装目录未被修改");
        }
    }

    private static bool HasRecentHeartbeat(UpdateTransactionRequest request)
    {
        try
        {
            return File.Exists(request.HeartbeatPath) &&
                   DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(request.HeartbeatPath) <=
                   CoordinatorHeartbeatTimeout;
        }
        catch
        {
            return false;
        }
    }

    private static void TryStopLaunchedProcess(UpdateTransactionRequest request, UpdaterLog? log)
    {
        try
        {
            if (!File.Exists(request.LaunchedProcessPath))
            {
                return;
            }

            var launched = UpdateJsonFile.Read(
                request.LaunchedProcessPath,
                UpdateJsonTypeInfo.LaunchedProcessInfo);
            var expectedExecutable = Path.GetFullPath(
                Path.Combine(request.InstallationDirectory, request.EntryExecutable));
            if (!string.Equals(launched.TransactionId, request.TransactionId, StringComparison.Ordinal) ||
                !string.Equals(
                    Path.GetFullPath(launched.ExecutablePath),
                    expectedExecutable,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using var process = Process.GetProcessById(launched.ProcessId);
            var actualExecutable = process.MainModule?.FileName;
            var processStartedAt = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            if (!process.HasExited &&
                !string.IsNullOrWhiteSpace(actualExecutable) &&
                string.Equals(
                    Path.GetFullPath(actualExecutable),
                    expectedExecutable,
                    StringComparison.OrdinalIgnoreCase) &&
                Math.Abs((processStartedAt - launched.CreatedAtUtc).TotalSeconds) <= 30)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
        catch (ArgumentException)
        {
        }
        catch (Exception exception)
        {
            log?.Error("无法停止启动失败的新版本进程。", exception);
        }
    }

    private static void WriteStatus(
        UpdateTransactionRequest request,
        UpdateWorkerPhase phase,
        string message)
    {
        UpdateJsonFile.WriteAtomic(
            request.WorkerStatusPath,
            new UpdateWorkerStatus(
                UpdateProtocol.SchemaVersion,
                request.TransactionId,
                phase,
                message,
                DateTimeOffset.UtcNow),
            UpdateJsonTypeInfo.WorkerStatus);
    }

    private static void TryWriteFailure(UpdateTransactionRequest request, string message)
    {
        try
        {
            WriteStatus(request, UpdateWorkerPhase.Failed, message);
        }
        catch
        {
        }
    }

    private static string GetWorkerLogDirectory(UpdateTransactionRequest request)
    {
        var secureDirectory = UpdateTransaction.GetSecureWorkingDirectory(request);
        return string.Equals(
            Path.GetFullPath(request.WorkingDirectory),
            Path.GetFullPath(secureDirectory),
            StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(secureDirectory, "logs")
            : request.LogDirectory;
    }

    private static void TryCleanupRollbackArtifacts(
        UpdateTransactionRequest request,
        UpdaterLog? log)
    {
        try
        {
            UpdateTransaction.CleanupRollbackArtifacts(request);
        }
        catch (Exception exception)
        {
            log?.Error("无法清理回滚产物。", exception);
        }
    }

    private static async Task RollbackWithRetriesAsync(
        UpdateTransactionRequest request,
        UpdaterLog? log)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                UpdateTransaction.Rollback(request);
                return;
            }
            catch (Exception exception)
            {
                lastException = exception;
                log?.Error($"第 {attempt} 次回滚尝试失败。", exception);
                if (attempt < 10)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                }
            }
        }

        throw new IOException("多次尝试后仍无法恢复旧版本", lastException);
    }
}

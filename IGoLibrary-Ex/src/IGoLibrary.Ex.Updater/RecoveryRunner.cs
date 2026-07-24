using System.ComponentModel;
using System.Diagnostics;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Updater;

internal static class RecoveryRunner
{
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromMinutes(3);

    public static async Task<int> RunCoordinatorAsync(string requestPath)
    {
        UpdateTransactionRequest? request = null;
        var log = UpdaterLog.TryCreateEmergency("recovery-coordinator");
        try
        {
            request = ReadAndValidateRecoveryRequest(requestPath);
            log = new UpdaterLog(request.LogDirectory, request.TransactionId, "recovery-coordinator");
            log.Info("开始执行更新恢复协调流程。");
            var processPath = ValidateRecoveryExecutable(request);
            var requiresElevation = UpdateInstallationPermissions.RequiresElevation(
                request.InstallationDirectory);
            bool succeeded;
            if (requiresElevation)
            {
                EnsureProtectedRecoverySource(request, processPath);
                using var worker = Process.Start(
                    UpdateProcessStartInfoFactory.CreateRecoveryWorker(
                        processPath,
                        requestPath,
                        elevate: true,
                        Environment.ProcessId))
                    ?? throw new InvalidOperationException("无法启动管理员恢复组件");
                using var timeout = new CancellationTokenSource(RecoveryTimeout);
                await worker.WaitForExitAsync(timeout.Token);
                succeeded = worker.ExitCode == 0;
                log.Info($"管理员恢复组件已退出。退出码={worker.ExitCode}。");
            }
            else
            {
                succeeded = await RecoverAsync(request, log);
                if (succeeded)
                {
                    ScheduleSecureCleanup(request, requestPath, [Environment.ProcessId]);
                    log.Info("已安排受保护更新目录清理。");
                }
            }

            if (!succeeded)
            {
                log.Error("更新恢复流程返回失败结果。");
                return 1;
            }

            AuthorizeSecureCleanup(request, log);
            UpdateRecoveryRegistration.Unregister(request.TransactionId);
            StartApplication(request);
            log.Info("更新恢复完成，主应用已重新启动。");
            return 0;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            log?.Error("用户取消了更新恢复提权。", exception);
            return 1;
        }
        catch (Exception ex)
        {
            log?.Error("更新恢复协调流程发生未处理异常。", ex);
            return 1;
        }
    }

    public static async Task<int> RunWorkerAsync(
        string requestPath,
        int recoveryCoordinatorProcessId)
    {
        var log = UpdaterLog.TryCreateEmergency("recovery-worker");
        try
        {
            var request = ReadAndValidateRecoveryRequest(requestPath);
            log = new UpdaterLog(request.LogDirectory, request.TransactionId, "recovery-worker");
            log.Info("开始执行管理员更新恢复工作流程。");
            ValidateRecoveryExecutable(request);
            if (!await RecoverAsync(request, log))
            {
                log.Error("管理员更新恢复工作流程返回失败结果。");
                return 1;
            }

            AuthorizeSecureCleanup(request, log);
            ScheduleSecureCleanup(
                request,
                requestPath,
                [Environment.ProcessId, recoveryCoordinatorProcessId]);
            log.Info("管理员更新恢复完成，已安排安全清理。");
            return 0;
        }
        catch (Exception ex)
        {
            log?.Error("管理员更新恢复工作流程发生未处理异常。", ex);
            return 1;
        }
    }

    public static void ScheduleSecureCleanup(
        UpdateTransactionRequest request,
        string requestPath,
        IReadOnlyList<int> processIdsToWaitFor)
    {
        var secureDirectory = UpdateTransaction.GetSecureWorkingDirectory(request);
        if (!string.Equals(
                Path.GetFullPath(request.WorkingDirectory),
                Path.GetFullPath(secureDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var installedUpdater = Path.Combine(
            request.InstallationDirectory,
            UpdateProtocol.UpdaterExecutableName);
        if (!File.Exists(installedUpdater))
        {
            return;
        }

        using var cleanup = Process.Start(
            UpdateProcessStartInfoFactory.CreateCleanup(
                installedUpdater,
                requestPath,
                processIdsToWaitFor));
    }

    public static void AuthorizeSecureCleanup(
        UpdateTransactionRequest request,
        UpdaterLog? log = null)
    {
        try
        {
            var controlDirectory = Path.GetDirectoryName(request.HealthReportPath)
                                   ?? throw new InvalidDataException("无法确定更新控制目录");
            UpdateJsonFile.WriteAtomic(
                Path.Combine(controlDirectory, "cleanup-ready.json"),
                new UpdateCleanupAuthorization(
                    UpdateProtocol.SchemaVersion,
                    request.TransactionId,
                    DateTimeOffset.UtcNow),
                UpdateJsonTypeInfo.CleanupAuthorization);
            log?.Info("已授权清理受保护更新目录。");
        }
        catch (Exception ex)
        {
            log?.Error("写入受保护更新目录清理授权失败。", ex);
        }
    }

    private static UpdateTransactionRequest ReadAndValidateRecoveryRequest(string requestPath)
    {
        var request = UpdateJsonFile.Read(requestPath, UpdateJsonTypeInfo.TransactionRequest);
        UpdateTransaction.ValidateRequestFile(requestPath, request);
        return request;
    }

    private static string ValidateRecoveryExecutable(UpdateTransactionRequest request)
    {
        var processPath = Environment.ProcessPath
                          ?? throw new InvalidOperationException("无法确定恢复程序路径");
        var expectedPath = Path.Combine(
            request.WorkingDirectory,
            UpdateProtocol.UpdaterExecutableName);
        if (!string.Equals(
                Path.GetFullPath(processPath),
                Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("恢复程序不是事务工作目录中的 updater");
        }

        return processPath;
    }

    private static void EnsureProtectedRecoverySource(
        UpdateTransactionRequest request,
        string processPath)
    {
        var secureDirectory = UpdateTransaction.GetSecureWorkingDirectory(request);
        if (!string.Equals(
                Path.GetFullPath(request.WorkingDirectory),
                Path.GetFullPath(secureDirectory),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetFullPath(processPath),
                Path.Combine(secureDirectory, UpdateProtocol.UpdaterExecutableName),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("受保护安装只能由受保护事务目录中的 updater 恢复");
        }
    }

    private static async Task<bool> RecoverAsync(
        UpdateTransactionRequest request,
        UpdaterLog? log)
    {
        log?.Info("开始停止仍在运行的主应用进程。");
        StopRunningApplication(request, log);
        if (Directory.Exists(request.BackupDirectory))
        {
            log?.Info("检测到更新备份目录，开始恢复中断事务。");
            var recovered = await UpdateTransaction.RecoverInterruptedAsync(request);
            log?.Info($"中断事务恢复完成。成功={recovered}。");
            return recovered;
        }

        if (!Directory.Exists(request.InstallationDirectory))
        {
            log?.Error("安装目录不存在，无法执行更新恢复。");
            return false;
        }

        UpdateTransaction.CleanupRollbackArtifacts(request);
        log?.Info("未检测到备份目录，已清理残留回滚工件。");
        return true;
    }

    private static void StopRunningApplication(
        UpdateTransactionRequest request,
        UpdaterLog? log)
    {
        var expectedPath = Path.GetFullPath(
            Path.Combine(request.InstallationDirectory, request.EntryExecutable));
        foreach (var process in Process.GetProcessesByName(
                     Path.GetFileNameWithoutExtension(request.EntryExecutable)))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited &&
                        string.Equals(
                            Path.GetFullPath(process.MainModule?.FileName ?? string.Empty),
                            expectedPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(10_000);
                        log?.Info($"已停止主应用进程。进程标识={process.Id}。");
                    }
                }
                catch (Exception ex)
                {
                    log?.Error($"检查或停止主应用进程失败。进程标识={process.Id}。", ex);
                }
            }
        }
    }

    private static void StartApplication(UpdateTransactionRequest request)
    {
        var executable = Path.Combine(request.InstallationDirectory, request.EntryExecutable);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("恢复后仍找不到主程序", executable);
        }

        using var process = Process.Start(
            UpdateProcessStartInfoFactory.CreateApplication(
                executable,
                request.InstallationDirectory,
                updateTransactionId: null));
    }
}

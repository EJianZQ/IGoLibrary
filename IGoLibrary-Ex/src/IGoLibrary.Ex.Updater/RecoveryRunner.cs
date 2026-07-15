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
        try
        {
            request = ReadAndValidateRecoveryRequest(requestPath);
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
            }
            else
            {
                succeeded = await RecoverAsync(request);
                if (succeeded)
                {
                    ScheduleSecureCleanup(request, requestPath, [Environment.ProcessId]);
                }
            }

            if (!succeeded)
            {
                return 1;
            }

            AuthorizeSecureCleanup(request);
            UpdateRecoveryRegistration.Unregister(request.TransactionId);
            StartApplication(request);
            return 0;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return 1;
        }
        catch
        {
            return 1;
        }
    }

    public static async Task<int> RunWorkerAsync(
        string requestPath,
        int recoveryCoordinatorProcessId)
    {
        try
        {
            var request = ReadAndValidateRecoveryRequest(requestPath);
            ValidateRecoveryExecutable(request);
            if (!await RecoverAsync(request))
            {
                return 1;
            }

            AuthorizeSecureCleanup(request);
            ScheduleSecureCleanup(
                request,
                requestPath,
                [Environment.ProcessId, recoveryCoordinatorProcessId]);
            return 0;
        }
        catch
        {
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

    public static void AuthorizeSecureCleanup(UpdateTransactionRequest request)
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
        }
        catch
        {
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

    private static async Task<bool> RecoverAsync(UpdateTransactionRequest request)
    {
        StopRunningApplication(request);
        if (Directory.Exists(request.BackupDirectory))
        {
            return await UpdateTransaction.RecoverInterruptedAsync(request);
        }

        if (!Directory.Exists(request.InstallationDirectory))
        {
            return false;
        }

        UpdateTransaction.CleanupRollbackArtifacts(request);
        return true;
    }

    private static void StopRunningApplication(UpdateTransactionRequest request)
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
                    }
                }
                catch
                {
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

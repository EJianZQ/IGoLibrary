using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using IGoLibrary.Ex.Updater.Core;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class WindowsUpdateHandoffService(
    ILogger<WindowsUpdateHandoffService> logger) : IWindowsUpdateHandoffService
{
    private static readonly TimeSpan CoordinatorReadyTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan BootstrapTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessShutdownTimeout = TimeSpan.FromSeconds(10);

    public async Task<WindowsUpdateHandoffResult> ExecuteAsync(
        PreparedWindowsUpdatePackage package,
        IProgress<WindowsUpdateProgress> progress,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows 更新交接仅支持 Windows");
        }

        var operationStage = "准备更新事务";
        var outcome = WindowsPortableUpdateOutcome.Failed;
        var message = "更新组件未能启动";
        Exception? failure = null;
        var handoffCompleted = false;
        string? registeredRecoveryTransactionId = null;
        UpdateTransactionRequest? registeredRecoveryRequest = null;
        int? externalWorkerProcessId = null;
        Process? coordinator = null;
        var updaterProcessesStopped = true;
        var cleanupCompleted = true;

        try
        {
            var prepared = await PrepareTransactionRequestAsync(package, cancellationToken);
            logger.LogInformation(
                "更新事务准备完成。事务={TransactionId}，请求文件已写入。",
                package.Workspace.TransactionId);

            operationStage = "检查安装目录权限";
            var requiresElevation = UpdateInstallationPermissions.RequiresElevation(
                package.InstallationDirectory);
            logger.LogInformation(
                "安装目录权限检查完成。事务={TransactionId}，需要 UAC 提权={RequiresElevation}。",
                package.Workspace.TransactionId,
                requiresElevation);
            if (requiresElevation)
            {
                operationStage = "验证 UAC 提权更新源";
                EnsureElevationSourceIsProtected(package.InstallationDirectory);
            }

            operationStage = "注册更新恢复信息";
            var recoveryWorkingDirectory = requiresElevation
                ? UpdateTransaction.GetSecureWorkingDirectory(prepared.Request)
                : prepared.Request.WorkingDirectory;
            UpdateRecoveryRegistration.Register(
                prepared.Request.TransactionId,
                Path.Combine(recoveryWorkingDirectory, UpdateProtocol.UpdaterExecutableName),
                Path.Combine(recoveryWorkingDirectory, "request.json"));
            registeredRecoveryTransactionId = prepared.Request.TransactionId;
            registeredRecoveryRequest = prepared.Request;

            cancellationToken.ThrowIfCancellationRequested();
            operationStage = requiresElevation
                ? "启动更新协调器并请求 UAC 提权"
                : "启动更新协调器";
            WindowsUpdateProgressReporter.Report(
                progress,
                WindowsUpdateStage.WaitingForExit,
                "更新组件正在准备；就绪后应用将正常退出…",
                actions: WindowsUpdateAvailableActions.Cancel);
            coordinator = StartCoordinator(
                prepared.CoordinatorPath,
                prepared.RequestPath,
                externalWorker: requiresElevation);
            if (requiresElevation)
            {
                logger.LogInformation(
                    "普通权限无法安装更新，正在请求 UAC 提权。事务={TransactionId}。",
                    package.Workspace.TransactionId);
                externalWorkerProcessId = await StartTrustedBootstrapAsync(
                    prepared,
                    workerProcessId => externalWorkerProcessId = workerProcessId,
                    cancellationToken);
            }

            operationStage = "等待更新协调器就绪";
            var signal = await WaitForCoordinatorSignalAsync(
                prepared.Request,
                coordinator,
                CoordinatorReadyTimeout,
                cancellationToken);
            if (signal.Signal == UpdateCoordinatorSignalKind.Canceled)
            {
                logger.LogInformation(
                    "更新协调器已取消安装。事务={TransactionId}，消息={CoordinatorMessage}。",
                    package.Workspace.TransactionId,
                    signal.Message);
                await WaitForCoordinatorExitAsync(coordinator);
                outcome = WindowsPortableUpdateOutcome.Canceled;
                message = signal.Message;
            }
            else if (signal.Signal != UpdateCoordinatorSignalKind.Ready)
            {
                logger.LogError(
                    "更新协调器未能就绪。事务={TransactionId}，信号={CoordinatorSignal}，消息={CoordinatorMessage}。",
                    package.Workspace.TransactionId,
                    signal.Signal,
                    signal.Message);
                await WaitForCoordinatorExitAsync(coordinator);
                outcome = WindowsPortableUpdateOutcome.Failed;
                message = signal.Message;
            }
            else
            {
                WindowsUpdateProgressReporter.Report(
                    progress,
                    WindowsUpdateStage.Installing,
                    "更新组件已就绪，正在安全退出应用…",
                    actions: WindowsUpdateAvailableActions.None);
                handoffCompleted = true;
                outcome = WindowsPortableUpdateOutcome.ExitRequested;
                message = "应用正在退出并安装更新";
                logger.LogInformation(
                    "更新组件已就绪，可以退出应用并交接安装。事务={TransactionId}，目标版本={TargetVersion}，需要 UAC 提权={RequiresElevation}。",
                    package.Workspace.TransactionId,
                    package.TargetVersion,
                    requiresElevation);
            }
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            outcome = WindowsPortableUpdateOutcome.Canceled;
            message = "已取消下载，未修改程序文件";
            failure = exception;
            logger.LogInformation(
                "用户取消了更新交接。阶段={Stage}，事务={TransactionId}。",
                operationStage,
                package.Workspace.TransactionId);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            outcome = WindowsPortableUpdateOutcome.Canceled;
            message = "已取消管理员授权，未修改任何程序文件";
            failure = exception;
            logger.LogWarning(
                exception,
                "用户取消了 UAC 管理员授权。阶段={Stage}，事务={TransactionId}。",
                operationStage,
                package.Workspace.TransactionId);
        }
        catch (Exception exception)
        {
            outcome = WindowsPortableUpdateOutcome.Failed;
            message = ToUserMessage(exception);
            failure = exception;
            logger.LogError(
                exception,
                "更新交接失败。阶段={Stage}，事务={TransactionId}，目标版本={TargetVersion}。",
                operationStage,
                package.Workspace.TransactionId,
                package.TargetVersion);
        }
        finally
        {
            if (!handoffCompleted && coordinator is not null)
            {
                updaterProcessesStopped = await TerminateProcessAsync(coordinator);
            }

            if (!handoffCompleted && externalWorkerProcessId is { } workerProcessId)
            {
                updaterProcessesStopped =
                    await WaitForProcessExitAsync(workerProcessId, TimeSpan.FromSeconds(30)) &&
                    updaterProcessesStopped;
            }

            coordinator?.Dispose();
            cleanupCompleted = updaterProcessesStopped;
            if (!handoffCompleted &&
                updaterProcessesStopped &&
                registeredRecoveryTransactionId is not null)
            {
                if (registeredRecoveryRequest is not null)
                {
                    AuthorizeSecureCleanup(registeredRecoveryRequest);
                    if (externalWorkerProcessId is not null)
                    {
                        cleanupCompleted = await WaitForSecureCleanupAsync(
                            registeredRecoveryRequest,
                            TimeSpan.FromSeconds(30));
                    }
                }

                if (cleanupCompleted)
                {
                    UpdateRecoveryRegistration.Unregister(registeredRecoveryTransactionId);
                }
            }

            if (!handoffCompleted && (!updaterProcessesStopped || !cleanupCompleted))
            {
                logger.LogWarning(
                    "更新交接收尾未完全完成，将由下次启动继续清理。事务={TransactionId}，更新进程已停止={UpdaterProcessesStopped}，安全清理已完成={CleanupCompleted}。",
                    package.Workspace.TransactionId,
                    updaterProcessesStopped,
                    cleanupCompleted);
            }
        }

        return new WindowsUpdateHandoffResult(
            outcome,
            message,
            CanRestoreVerifiedCache: !handoffCompleted && updaterProcessesStopped && cleanupCompleted,
            failure);
    }

    private static async Task<PreparedUpdateTransaction> PrepareTransactionRequestAsync(
        PreparedWindowsUpdatePackage package,
        CancellationToken cancellationToken)
    {
        var workspace = package.Workspace;
        var updaterSource = Path.Combine(
            package.InstallationDirectory,
            UpdateProtocol.UpdaterExecutableName);
        var updaterDestination = Path.Combine(
            workspace.TransactionDirectory,
            UpdateProtocol.UpdaterExecutableName);
        var currentManifest = UpdatePackageValidator.LoadAndValidateManifest(
            Path.Combine(package.InstallationDirectory, UpdateProtocol.ManifestFileName));
        var updaterEntry = currentManifest.Files.SingleOrDefault(file =>
            string.Equals(
                UpdatePathSafety.NormalizeRelativePath(file.Path),
                UpdateProtocol.UpdaterExecutableName,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("当前 manifest 缺少独立 updater 文件项");
        await CopyFileCreateNewAsync(
            updaterSource,
            updaterDestination,
            cancellationToken);
        await UpdatePackageValidator.ValidateFileAsync(
            updaterDestination,
            updaterEntry.Size,
            updaterEntry.Sha256,
            cancellationToken);

        var parent = Path.GetDirectoryName(package.InstallationDirectory)
                     ?? throw new InvalidOperationException("无法确定安装目录父目录");
        using var currentProcess = Process.GetCurrentProcess();
        var request = new UpdateTransactionRequest(
            UpdateProtocol.SchemaVersion,
            workspace.TransactionId,
            Environment.ProcessId,
            new DateTimeOffset(currentProcess.StartTime.ToUniversalTime(), TimeSpan.Zero),
            currentManifest.Version,
            package.TargetVersion,
            package.InstallationDirectory,
            workspace.StagingDirectory,
            workspace.TransactionDirectory,
            workspace.ArchivePath,
            Path.Combine(parent, $".IGoLibrary-Ex.update-{workspace.TransactionId}"),
            Path.Combine(parent, $".IGoLibrary-Ex.backup-{workspace.TransactionId}"),
            UpdateProtocol.EntryExecutableName,
            UpdateProtocol.ManifestFileName,
            package.Asset.Digest,
            package.Asset.Size,
            Path.Combine(workspace.TransactionDirectory, "health.json"),
            Path.Combine(workspace.TransactionDirectory, "coordinator-signal.json"),
            Path.Combine(workspace.TransactionDirectory, "worker-ready.json"),
            Path.Combine(workspace.TransactionDirectory, "worker-status.json"),
            Path.Combine(workspace.TransactionDirectory, "decision.json"),
            Path.Combine(workspace.TransactionDirectory, "heartbeat.txt"),
            Path.Combine(workspace.TransactionDirectory, "launched-process.json"),
            Path.Combine(WindowsUpdateWorkspaceManager.GetUpdatesRoot(), "logs"));
        var requestPath = Path.Combine(workspace.TransactionDirectory, "request.json");
        UpdateJsonFile.WriteAtomic(requestPath, request, UpdateJsonTypeInfo.TransactionRequest);
        UpdateTransaction.ValidateRequestFile(requestPath, request);
        UpdateTransaction.ValidateRequest(request);
        return new PreparedUpdateTransaction(
            request,
            requestPath,
            updaterDestination,
            updaterSource);
    }

    private static Process StartCoordinator(
        string updaterPath,
        string requestPath,
        bool externalWorker)
    {
        var startInfo = new ProcessStartInfo(updaterPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(updaterPath)!
        };
        startInfo.ArgumentList.Add("--request");
        startInfo.ArgumentList.Add(requestPath);
        if (externalWorker)
        {
            startInfo.ArgumentList.Add("--external-worker");
        }

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("无法启动独立更新组件");
    }

    private static async Task<UpdateCoordinatorSignal> WaitForCoordinatorSignalAsync(
        UpdateTransactionRequest request,
        Process coordinator,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(request.CoordinatorReadyPath))
            {
                var signal = UpdateJsonFile.Read(
                    request.CoordinatorReadyPath,
                    UpdateJsonTypeInfo.CoordinatorSignal);
                if (signal.SchemaVersion != UpdateProtocol.SchemaVersion ||
                    !string.Equals(signal.TransactionId, request.TransactionId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("更新协调器响应无效");
                }

                return signal;
            }

            if (coordinator.HasExited)
            {
                throw new InvalidOperationException("更新协调器提前退出，请查看更新日志后重试");
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("等待更新协调器就绪超时，应用不会退出");
    }

    private static async Task WaitForCoordinatorExitAsync(Process coordinator)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await coordinator.WaitForExitAsync(timeout.Token);
        }
        catch
        {
        }
    }

    private static void EnsureElevationSourceIsProtected(string installationDirectory)
    {
        var directoryProbe = Path.Combine(
            installationDirectory,
            $".IGoLibrary-Ex.elevation-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(directoryProbe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }

            throw new InvalidOperationException(
                "安装目录仍可被当前用户写入，拒绝把其中的更新组件用于 UAC 提权");
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            TryDeleteFile(directoryProbe);
        }

        foreach (var fileName in new[]
                 {
                     UpdateProtocol.UpdaterExecutableName,
                     UpdateProtocol.ManifestFileName
                 })
        {
            var path = Path.Combine(installationDirectory, fileName);
            try
            {
                using var ignored = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                throw new InvalidOperationException(
                    $"{fileName} 可被当前用户修改，拒绝将其用于 UAC 提权");
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<int> StartTrustedBootstrapAsync(
        PreparedUpdateTransaction prepared,
        Action<int> workerStarted,
        CancellationToken cancellationToken)
    {
        await using var packageLease = new FileStream(
            prepared.Request.PackagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (packageLease.Length != prepared.Request.PackageSize)
        {
            throw new InvalidDataException("更新包大小在提权交接前发生变化");
        }

        var pipeName = $"IGoLibrary.Ex.Update.{prepared.Request.TransactionId}.{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        using var bootstrap = Process.Start(
            UpdateProcessStartInfoFactory.CreateBootstrap(
                prepared.TrustedUpdaterPath,
                pipeName))
            ?? throw new InvalidOperationException("无法启动受信的管理员更新引导器");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BootstrapTimeout);
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientProcessId) ||
                clientProcessId != (uint)bootstrap.Id)
            {
                throw new InvalidDataException("管理员更新引导器的进程身份验证失败");
            }

            await UpdatePipeProtocol.WriteAsync(
                pipe,
                new UpdateBootstrapPayload(
                    UpdateProtocol.SchemaVersion,
                    prepared.RequestPath,
                    prepared.Request),
                UpdateJsonTypeInfo.BootstrapPayload,
                timeout.Token);
            var result = await UpdatePipeProtocol.ReadAsync(
                pipe,
                UpdateJsonTypeInfo.BootstrapResult,
                timeout.Token);
            if (result.SchemaVersion != UpdateProtocol.SchemaVersion ||
                !string.Equals(
                    result.TransactionId,
                    prepared.Request.TransactionId,
                    StringComparison.Ordinal) ||
                !result.Succeeded)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Message)
                        ? "管理员更新引导器未能建立受保护事务"
                        : result.Message);
            }

            if (result.WorkerProcessId is not > 0)
            {
                throw new InvalidDataException("管理员更新引导器未返回有效的 worker 进程标识");
            }

            workerStarted(result.WorkerProcessId.Value);
            await bootstrap.WaitForExitAsync(timeout.Token);
            if (bootstrap.ExitCode != 0)
            {
                throw new InvalidOperationException("管理员更新引导器异常退出");
            }

            return result.WorkerProcessId.Value;
        }
        catch
        {
            await TerminateProcessAsync(bootstrap);
            throw;
        }
    }

    private static async Task<bool> TerminateProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            using var timeout = new CancellationTokenSource(ProcessShutdownTimeout);
            await process.WaitForExitAsync(timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var cancellation = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cancellation.Token);
            return true;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitForSecureCleanupAsync(
        UpdateTransactionRequest request,
        TimeSpan timeout)
    {
        var controlDirectory = Path.GetDirectoryName(request.HealthReportPath);
        if (string.IsNullOrWhiteSpace(controlDirectory))
        {
            return false;
        }

        var completionPath = Path.Combine(controlDirectory, "cleanup-complete.json");
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(completionPath))
            {
                try
                {
                    var completion = UpdateJsonFile.Read(
                        completionPath,
                        UpdateJsonTypeInfo.CleanupCompletion);
                    return completion.SchemaVersion == UpdateProtocol.SchemaVersion &&
                           string.Equals(
                               completion.TransactionId,
                               request.TransactionId,
                               StringComparison.Ordinal);
                }
                catch (IOException)
                {
                }
            }

            await Task.Delay(250);
        }

        return false;
    }

    private static async Task CopyFileCreateNewAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, 128 * 1024, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);

    private static void AuthorizeSecureCleanup(UpdateTransactionRequest request)
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string ToUserMessage(Exception exception)
    {
        return exception switch
        {
            TimeoutException => exception.Message,
            UnauthorizedAccessException => "没有读取或写入更新文件所需的权限",
            InvalidDataException => $"更新包校验失败：{exception.Message}",
            IOException => $"更新文件处理失败：{exception.Message}",
            _ => $"自动更新失败：{exception.Message}"
        };
    }

    private sealed record PreparedUpdateTransaction(
        UpdateTransactionRequest Request,
        string RequestPath,
        string CoordinatorPath,
        string TrustedUpdaterPath);
}

internal interface IWindowsUpdateHandoffService
{
    Task<WindowsUpdateHandoffResult> ExecuteAsync(
        PreparedWindowsUpdatePackage package,
        IProgress<WindowsUpdateProgress> progress,
        CancellationToken cancellationToken);
}

internal sealed record WindowsUpdateHandoffResult(
    WindowsPortableUpdateOutcome Outcome,
    string Message,
    bool CanRestoreVerifiedCache,
    Exception? Failure)
{
    public bool ReadyForExit => Outcome == WindowsPortableUpdateOutcome.ExitRequested;
}

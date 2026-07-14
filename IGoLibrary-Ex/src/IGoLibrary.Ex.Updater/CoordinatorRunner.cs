using System.Diagnostics;
using System.Runtime.InteropServices;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Updater;

internal sealed class CoordinatorRunner
{
    private static readonly TimeSpan WorkerReadyTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FinalizeTimeout = TimeSpan.FromSeconds(45);
    private readonly string _requestPath;
    private readonly bool _externalWorker;
    private readonly Action<string> _reportStatus;
    private UpdateTransactionRequest? _request;
    private UpdaterLog? _log;
    private Process? _newProcess;
    private bool _applicationWasReleased;

    public CoordinatorRunner(
        string requestPath,
        bool externalWorker,
        Action<string> reportStatus)
    {
        _requestPath = requestPath;
        _externalWorker = externalWorker;
        _reportStatus = reportStatus;
    }

    public async Task<CoordinatorResult> RunAsync(CancellationToken cancellationToken)
    {
        using var heartbeatCancellation = new CancellationTokenSource();
        Task? heartbeatTask = null;
        try
        {
            if (!OperatingSystem.IsWindows() ||
                RuntimeInformation.OSArchitecture != Architecture.X64)
            {
                throw new PlatformNotSupportedException("自动更新仅支持 Windows x64 系统");
            }

            _request = UpdateJsonFile.Read<UpdateTransactionRequest>(_requestPath);
            UpdateTransaction.ValidateRequestFile(_requestPath, _request);
            UpdateTransaction.ValidateRequest(_request);
            _log = new UpdaterLog(_request.LogDirectory, _request.TransactionId, "coordinator");
            _log.Info("Coordinator started.");
            heartbeatTask = RunHeartbeatAsync(_request, heartbeatCancellation.Token);

            _reportStatus(_externalWorker
                ? "正在等待受保护的文件更新组件…"
                : "正在启动文件更新组件…");
            using var worker = _externalWorker ? null : StartWorker();
            var ready = await WaitForWorkerPhaseAsync(
                _request,
                worker,
                UpdateWorkerPhase.Ready,
                WorkerReadyTimeout,
                cancellationToken);
            if (!ready)
            {
                throw CreateWorkerFailure(_request, "更新组件未能就绪");
            }

            UpdateJsonFile.WriteAtomic(
                _request.CoordinatorReadyPath,
                new UpdateCoordinatorSignal(
                    UpdateProtocol.SchemaVersion,
                    _request.TransactionId,
                    UpdateCoordinatorSignalKind.Ready,
                    "文件更新组件已就绪",
                    DateTimeOffset.UtcNow));
            _applicationWasReleased = true;

            _reportStatus("正在等待主程序安全退出…");
            if (!await WaitForWorkerPhaseAsync(
                    _request,
                    worker,
                    UpdateWorkerPhase.Applied,
                    ApplyTimeout,
                    cancellationToken))
            {
                throw CreateWorkerFailure(_request, "文件替换未完成");
            }

            _reportStatus("正在启动并验证新版本…");
            _newProcess = StartApplication(_request, isHealthCheck: true);
            UpdateJsonFile.WriteAtomic(
                _request.LaunchedProcessPath,
                new UpdateLaunchedProcessInfo(
                    UpdateProtocol.SchemaVersion,
                    _request.TransactionId,
                    _newProcess.Id,
                    Path.Combine(_request.InstallationDirectory, _request.EntryExecutable),
                    DateTimeOffset.UtcNow));

            var healthy = await WaitForHealthAsync(_request, _newProcess, cancellationToken);
            if (healthy)
            {
                WriteDecision(_request, UpdateDecisionKind.Commit, _newProcess.Id);
                await WaitForFinalPhaseAsync(
                    _request,
                    worker,
                    UpdateWorkerPhase.Committed,
                    FinalizeTimeout,
                    cancellationToken);
                await TryDeleteSuccessfulPayloadAsync(_request, cancellationToken);
                RecoveryRunner.AuthorizeSecureCleanup(_request);
                UpdateRecoveryRegistration.Unregister(_request.TransactionId);
                _log.Info("Coordinator committed the update.");
                return new CoordinatorResult(true, "更新成功，新版本已经启动", false);
            }

            _reportStatus("新版本启动失败，正在恢复旧版本…");
            StopNewProcess();
            WriteDecision(_request, UpdateDecisionKind.Rollback, _newProcess.Id);
            await WaitForFinalPhaseAsync(
                _request,
                worker,
                UpdateWorkerPhase.RolledBack,
                FinalizeTimeout,
                cancellationToken);
            RecoveryRunner.AuthorizeSecureCleanup(_request);
            UpdateRecoveryRegistration.Unregister(_request.TransactionId);
            StartApplication(_request, isHealthCheck: false);
            _log.Info("Coordinator requested rollback and restarted the old version.");
            return new CoordinatorResult(false, "新版本启动验证失败，已恢复并重新启动旧版本", true);
        }
        catch (Exception exception)
        {
            _log?.Error("Coordinator failed.", exception);
            if (_request is not null && _applicationWasReleased)
            {
                await TryRollbackAndRestartAsync(cancellationToken);
            }
            else
            {
                TryWriteCoordinatorSignal(UpdateCoordinatorSignalKind.Failed, exception.Message);
            }

            return new CoordinatorResult(false, ToUserMessage(exception), _applicationWasReleased);
        }
        finally
        {
            heartbeatCancellation.Cancel();
            if (heartbeatTask is not null)
            {
                try
                {
                    await heartbeatTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    private Process StartWorker()
    {
        var executablePath = Environment.ProcessPath
                             ?? throw new InvalidOperationException("无法确定更新程序路径");
        var startInfo = UpdateProcessStartInfoFactory.CreateWorker(
            executablePath,
            _requestPath);
        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("无法启动文件更新组件");
    }

    private static async Task RunHeartbeatAsync(
        UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(request.HeartbeatPath)!);
            await File.WriteAllTextAsync(
                request.HeartbeatPath,
                DateTimeOffset.UtcNow.ToString("O"),
                cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private static async Task<bool> WaitForWorkerPhaseAsync(
        UpdateTransactionRequest request,
        Process? worker,
        UpdateWorkerPhase expectedPhase,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expectedPhase == UpdateWorkerPhase.Ready &&
                TryReadWorkerReadySignal(request))
            {
                return true;
            }

            if (TryReadWorkerStatus(request, out var status))
            {
                if (status!.Phase == expectedPhase)
                {
                    return true;
                }

                if (status.Phase is UpdateWorkerPhase.Failed or UpdateWorkerPhase.RolledBack)
                {
                    return false;
                }
            }

            if (worker?.HasExited == true)
            {
                return false;
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private static bool TryReadWorkerReadySignal(UpdateTransactionRequest request)
    {
        try
        {
            if (!File.Exists(request.WorkerReadyPath))
            {
                return false;
            }

            var signal = UpdateJsonFile.Read<UpdateCoordinatorSignal>(request.WorkerReadyPath);
            return signal.SchemaVersion == UpdateProtocol.SchemaVersion &&
                   string.Equals(signal.TransactionId, request.TransactionId, StringComparison.Ordinal) &&
                   signal.Signal == UpdateCoordinatorSignalKind.Ready;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static async Task WaitForFinalPhaseAsync(
        UpdateTransactionRequest request,
        Process? worker,
        UpdateWorkerPhase expectedPhase,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!await WaitForWorkerPhaseAsync(request, worker, expectedPhase, timeout, cancellationToken))
        {
            throw CreateWorkerFailure(request, "更新事务未能安全结束");
        }
    }

    private static async Task<bool> WaitForHealthAsync(
        UpdateTransactionRequest request,
        Process process,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + HealthTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                return false;
            }

            if (File.Exists(request.HealthReportPath))
            {
                try
                {
                    var report = UpdateJsonFile.Read<UpdateHealthReport>(request.HealthReportPath);
                    return report.SchemaVersion == UpdateProtocol.SchemaVersion &&
                           string.Equals(report.TransactionId, request.TransactionId, StringComparison.Ordinal) &&
                           string.Equals(report.Version, request.TargetVersion, StringComparison.OrdinalIgnoreCase) &&
                           report.ProcessId == process.Id;
                }
                catch (IOException)
                {
                }
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private static Process StartApplication(UpdateTransactionRequest request, bool isHealthCheck)
    {
        var executablePath = Path.Combine(request.InstallationDirectory, request.EntryExecutable);
        var startInfo = UpdateProcessStartInfoFactory.CreateApplication(
            executablePath,
            request.InstallationDirectory,
            isHealthCheck ? request.TransactionId : null);

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("无法启动应用程序");
    }

    private static void WriteDecision(
        UpdateTransactionRequest request,
        UpdateDecisionKind decision,
        int? newProcessId)
    {
        UpdateJsonFile.WriteAtomic(
            request.DecisionPath,
            new UpdateDecision(
                UpdateProtocol.SchemaVersion,
                request.TransactionId,
                decision,
                DateTimeOffset.UtcNow,
                newProcessId));
    }

    private async Task TryRollbackAndRestartAsync(CancellationToken cancellationToken)
    {
        if (_request is null)
        {
            return;
        }

        try
        {
            StopNewProcess();
            if (Directory.Exists(_request.InstallationDirectory) &&
                !Directory.Exists(_request.BackupDirectory))
            {
                RecoveryRunner.AuthorizeSecureCleanup(_request);
                UpdateRecoveryRegistration.Unregister(_request.TransactionId);
                StartApplication(_request, isHealthCheck: false);
                return;
            }

            WriteDecision(_request, UpdateDecisionKind.Rollback, _newProcess?.Id);
            var deadline = DateTimeOffset.UtcNow + FinalizeTimeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryReadWorkerStatus(_request, out var status) &&
                    status!.Phase == UpdateWorkerPhase.RolledBack)
                {
                    RecoveryRunner.AuthorizeSecureCleanup(_request);
                    UpdateRecoveryRegistration.Unregister(_request.TransactionId);
                    StartApplication(_request, isHealthCheck: false);
                    return;
                }

                if (TryReadWorkerStatus(_request, out status) &&
                    status!.Phase == UpdateWorkerPhase.Failed &&
                    Directory.Exists(_request.InstallationDirectory) &&
                    !Directory.Exists(_request.BackupDirectory))
                {
                    RecoveryRunner.AuthorizeSecureCleanup(_request);
                    UpdateRecoveryRegistration.Unregister(_request.TransactionId);
                    StartApplication(_request, isHealthCheck: false);
                    return;
                }

                await Task.Delay(250, cancellationToken);
            }

            TryStartRecoveryCoordinator(_request);
        }
        catch (Exception exception)
        {
            _log?.Error("Emergency rollback orchestration failed.", exception);
        }
    }

    private void TryStartRecoveryCoordinator(UpdateTransactionRequest request)
    {
        try
        {
            var secureDirectory = UpdateTransaction.GetSecureWorkingDirectory(request);
            var secureRequest = Path.Combine(secureDirectory, "request.json");
            var secureUpdater = Path.Combine(
                secureDirectory,
                UpdateProtocol.UpdaterExecutableName);
            string updaterPath;
            string requestPath;
            if (File.Exists(secureUpdater) && File.Exists(secureRequest))
            {
                updaterPath = secureUpdater;
                requestPath = secureRequest;
            }
            else
            {
                updaterPath = Environment.ProcessPath
                              ?? throw new InvalidOperationException("无法确定恢复程序路径");
                requestPath = _requestPath;
            }

            using var recovery = Process.Start(
                UpdateProcessStartInfoFactory.CreateRecoveryCoordinator(
                    updaterPath,
                    requestPath));
            _log?.Info("Started persistent recovery coordinator.");
        }
        catch (Exception exception)
        {
            _log?.Error("Unable to start persistent recovery coordinator.", exception);
        }
    }

    private void StopNewProcess()
    {
        try
        {
            if (_newProcess is { HasExited: false })
            {
                _newProcess.Kill(entireProcessTree: true);
                _newProcess.WaitForExit(10_000);
            }
        }
        catch (Exception exception)
        {
            _log?.Error("Unable to stop the new process.", exception);
        }
    }

    private void TryWriteCoordinatorSignal(UpdateCoordinatorSignalKind signal, string message)
    {
        if (_request is null)
        {
            return;
        }

        try
        {
            UpdateJsonFile.WriteAtomic(
                _request.CoordinatorReadyPath,
                new UpdateCoordinatorSignal(
                    UpdateProtocol.SchemaVersion,
                    _request.TransactionId,
                    signal,
                    message,
                    DateTimeOffset.UtcNow));
        }
        catch (Exception exception)
        {
            _log?.Error("Unable to write coordinator signal.", exception);
        }
    }

    private static bool TryReadWorkerStatus(
        UpdateTransactionRequest request,
        out UpdateWorkerStatus? status)
    {
        status = null;
        try
        {
            if (!File.Exists(request.WorkerStatusPath))
            {
                return false;
            }

            status = UpdateJsonFile.Read<UpdateWorkerStatus>(request.WorkerStatusPath);
            return status.SchemaVersion == UpdateProtocol.SchemaVersion &&
                   string.Equals(status.TransactionId, request.TransactionId, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static Exception CreateWorkerFailure(UpdateTransactionRequest request, string fallback)
    {
        return TryReadWorkerStatus(request, out var status)
            ? new InvalidOperationException(status!.Message)
            : new InvalidOperationException(fallback);
    }

    private async Task TryDeleteSuccessfulPayloadAsync(
        UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var transactionDirectory = Path.GetDirectoryName(request.HealthReportPath)
                                   ?? throw new InvalidDataException("无法确定更新控制目录");
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var archivePath = Path.Combine(transactionDirectory, "package.zip");
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                }

                var cachePath = Path.Combine(transactionDirectory, "verified-cache.json");
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }

                if (Directory.Exists(request.StagingDirectory))
                {
                    Directory.Delete(request.StagingDirectory, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 20)
                {
                    _log?.Error("Unable to clean the successful update payload.", exception);
                    return;
                }

                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private static string ToUserMessage(Exception exception)
    {
        return exception switch
        {
            TimeoutException => exception.Message,
            UnauthorizedAccessException => "没有替换程序文件所需的权限，请重试并允许管理员授权",
            IOException => $"更新文件处理失败：{exception.Message}",
            _ => $"自动更新失败：{exception.Message}"
        };
    }

}

internal sealed record CoordinatorResult(
    bool Succeeded,
    string Message,
    bool ShouldShowMessage);

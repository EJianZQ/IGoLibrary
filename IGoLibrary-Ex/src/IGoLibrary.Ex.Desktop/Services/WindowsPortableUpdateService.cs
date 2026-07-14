using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Updater.Core;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class WindowsPortableUpdateService(
    IReleaseAssetDownloader downloader,
    IUpdateInstallGuard installGuard,
    IAppVersionProvider appVersionProvider,
    AppWindowService appWindowService,
    ILogger<WindowsPortableUpdateService> logger) : IWindowsPortableUpdateService
{
    private const long DiskSpaceMargin = 256L * 1024 * 1024;
    private static readonly TimeSpan CoordinatorReadyTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan BootstrapTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessShutdownTimeout = TimeSpan.FromSeconds(10);

    public async Task<WindowsPortableUpdateResult> DownloadAndInstallAsync(
        ReleaseUpdateInfo release,
        IProgress<WindowsUpdateProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(progress);
        var currentVersion = appVersionProvider.CurrentVersionText;
        var targetVersion = release.Version.ToString();
        if (!OperatingSystem.IsWindows() ||
            RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            logger.LogWarning(
                "已拒绝自动更新：当前平台不受支持。目标版本={TargetVersion}，操作系统={OperatingSystem}，架构={Architecture}。",
                targetVersion,
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture);
            return Failed("自动更新仅支持 Windows 10/11 x64 绿色版");
        }

        if (release.WindowsX64Package is not { } asset)
        {
            logger.LogWarning(
                "已拒绝自动更新：Release 没有可安装的 Windows x64 绿色版资源。当前版本={CurrentVersion}，目标版本={TargetVersion}。",
                currentVersion,
                targetVersion);
            return Failed("此版本没有可自动安装的 Windows x64 更新包，请前往 GitHub 下载");
        }

        logger.LogInformation(
            "开始 Windows 绿色版自动更新：{CurrentVersion} -> {TargetVersion}；资源={AssetName}，大小={PackageSize} 字节，摘要={PackageDigest}。",
            currentVersion,
            targetVersion,
            asset.Name,
            asset.Size,
            asset.Digest);

        Report(progress, WindowsUpdateStage.Checking, "正在检查运行中的任务…");
        var blockingTasks = installGuard.GetBlockingTaskNames();
        if (blockingTasks.Count > 0)
        {
            logger.LogWarning(
                "自动更新被运行中的任务阻止：{BlockingTasks}。目标版本={TargetVersion}。",
                string.Join("、", blockingTasks),
                targetVersion);
            return Blocked(blockingTasks);
        }

        var operationStage = "验证当前绿色版安装包";
        string? transactionDirectory = null;
        var preserveVerifiedCache = false;
        var handoffCompleted = false;
        string? registeredRecoveryTransactionId = null;
        UpdateTransactionRequest? registeredRecoveryRequest = null;
        int? externalWorkerProcessId = null;
        Process? coordinator = null;
        try
        {
            var installationDirectory = ValidateInstallationDirectory(currentVersion);
            logger.LogInformation(
                "当前绿色版安装包验证通过：版本={CurrentVersion}，目录={InstallationDirectory}。",
                currentVersion,
                installationDirectory);

            operationStage = "查找已验证更新缓存";
            var updatesRoot = GetUpdatesRoot();
            Directory.CreateDirectory(updatesRoot);

            var cached = await TryFindVerifiedCacheAsync(
                updatesRoot,
                asset,
                release.Version.ToString(),
                cancellationToken);
            string transactionId;
            string archivePath;
            string stagingDirectory;
            if (cached is not null)
            {
                transactionId = cached.TransactionId;
                transactionDirectory = cached.TransactionDirectory;
                archivePath = cached.ArchivePath;
                stagingDirectory = cached.StagingDirectory;
                Report(
                    progress,
                    WindowsUpdateStage.Verifying,
                    "已找到校验通过的更新缓存，正在复核…");
                logger.LogInformation(
                    "找到已验证更新缓存：事务={TransactionId}，目标版本={TargetVersion}，目录={TransactionDirectory}。",
                    transactionId,
                    targetVersion,
                    transactionDirectory);
            }
            else
            {
                operationStage = "准备更新下载目录";
                EnsureAvailableSpace(updatesRoot, asset.Size + DiskSpaceMargin, "更新下载目录");
                transactionId = Guid.NewGuid().ToString("N");
                transactionDirectory = Path.Combine(updatesRoot, transactionId);
                Directory.CreateDirectory(transactionDirectory);
                archivePath = Path.Combine(transactionDirectory, "package.zip");
                stagingDirectory = Path.Combine(transactionDirectory, "staging");

                operationStage = "下载更新包";
                logger.LogInformation(
                    "开始下载更新包：事务={TransactionId}，目标版本={TargetVersion}，资源={AssetName}，保存路径={ArchivePath}。",
                    transactionId,
                    targetVersion,
                    asset.Name,
                    archivePath);
                Report(
                    progress,
                    WindowsUpdateStage.Downloading,
                    "正在从 GitHub 下载新版本…",
                    0,
                    asset.Size);
                var downloadProgress = new Progress<ReleaseAssetDownloadProgress>(value =>
                    Report(
                        progress,
                        WindowsUpdateStage.Downloading,
                        "正在从 GitHub 下载新版本…",
                        value.DownloadedBytes,
                        value.TotalBytes));
                await downloader.DownloadAsync(asset, archivePath, downloadProgress, cancellationToken);
                logger.LogInformation(
                    "更新包下载完成：事务={TransactionId}，目标版本={TargetVersion}，实际大小={DownloadedSize} 字节。",
                    transactionId,
                    targetVersion,
                    new FileInfo(archivePath).Length);

                operationStage = "校验更新包摘要";
                Report(progress, WindowsUpdateStage.Verifying, "下载完成，正在校验更新包…");
                await VerifyArchiveDigestAsync(archivePath, asset, cancellationToken);
                logger.LogInformation(
                    "更新包 SHA-256 校验通过：事务={TransactionId}，摘要={PackageDigest}。",
                    transactionId,
                    asset.Digest);

                operationStage = "解压并验证更新包";
                var extractProgress = new Progress<(long Completed, long Total)>(value =>
                    Report(
                        progress,
                        WindowsUpdateStage.Extracting,
                        "正在安全解压并验证程序文件…",
                        value.Completed,
                        value.Total));
                var manifest = await UpdatePackageValidator.ExtractAndValidateAsync(
                    archivePath,
                    stagingDirectory,
                    targetVersion,
                    extractProgress,
                    cancellationToken);
                logger.LogInformation(
                    "更新包解压及清单校验通过：事务={TransactionId}，版本={ManifestVersion}，文件数={ManifestFileCount}，暂存目录={StagingDirectory}。",
                    transactionId,
                    manifest.Version,
                    manifest.Files.Count,
                    stagingDirectory);

                operationStage = "写入已验证更新缓存";
                UpdateJsonFile.WriteAtomic(
                    Path.Combine(transactionDirectory, "verified-cache.json"),
                    new VerifiedUpdateCache(
                        UpdateProtocol.SchemaVersion,
                        transactionId,
                        release.Version.ToString(),
                        asset.Digest,
                        asset.Size,
                        DateTimeOffset.UtcNow));
                logger.LogInformation(
                    "已记录校验通过的更新缓存：事务={TransactionId}，目标版本={TargetVersion}。",
                    transactionId,
                    targetVersion);
            }

            operationStage = "验证安装空间和当前安装包完整性";
            await ValidateSpaceAndCurrentPackageAsync(
                installationDirectory,
                stagingDirectory,
                currentVersion,
                updatesRoot,
                asset.Size,
                cancellationToken);
            preserveVerifiedCache = true;
            logger.LogInformation(
                "当前安装包完整性和磁盘空间检查通过：事务={TransactionId}，目标版本={TargetVersion}。",
                transactionId,
                targetVersion);

            Report(progress, WindowsUpdateStage.Checking, "正在再次检查运行中的任务…");
            blockingTasks = installGuard.GetBlockingTaskNames();
            if (blockingTasks.Count > 0)
            {
                logger.LogWarning(
                    "更新包已缓存，但安装被运行中的任务阻止：{BlockingTasks}。事务={TransactionId}，目标版本={TargetVersion}。",
                    string.Join("、", blockingTasks),
                    transactionId,
                    targetVersion);
                return Blocked(blockingTasks, "更新包已安全缓存；停止任务后可重新检查更新并安装");
            }

            operationStage = "准备更新事务";
            var prepared = await PrepareTransactionRequestAsync(
                transactionId,
                transactionDirectory,
                installationDirectory,
                stagingDirectory,
                asset,
                targetVersion,
                cancellationToken);
            logger.LogInformation(
                "更新事务准备完成：事务={TransactionId}，请求文件={RequestPath}。",
                transactionId,
                prepared.RequestPath);

            operationStage = "检查安装目录权限";
            var requiresElevation = UpdateInstallationPermissions.RequiresElevation(installationDirectory);
            logger.LogInformation(
                "安装目录权限检查完成：事务={TransactionId}，需要 UAC 提权={RequiresElevation}。",
                transactionId,
                requiresElevation);
            if (requiresElevation)
            {
                operationStage = "验证 UAC 提权更新源";
                EnsureElevationSourceIsProtected(installationDirectory);
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
            Report(
                progress,
                WindowsUpdateStage.WaitingForExit,
                "更新组件正在准备；就绪后应用将正常退出…",
                canCancel: false);
            coordinator = StartCoordinator(
                prepared.CoordinatorPath,
                prepared.RequestPath,
                externalWorker: requiresElevation);
            if (requiresElevation)
            {
                logger.LogInformation(
                    "普通权限无法安装更新，正在请求 UAC 提权：事务={TransactionId}。",
                    transactionId);
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
                CancellationToken.None);
            if (signal.Signal == UpdateCoordinatorSignalKind.Canceled)
            {
                logger.LogInformation(
                    "更新协调器已取消安装：事务={TransactionId}，消息={CoordinatorMessage}。",
                    transactionId,
                    signal.Message);
                preserveVerifiedCache = false;
                await WaitForCoordinatorExitAsync(coordinator);
                TryDeletePayload(transactionDirectory);
                return new WindowsPortableUpdateResult(
                    WindowsPortableUpdateOutcome.Canceled,
                    signal.Message);
            }

            if (signal.Signal != UpdateCoordinatorSignalKind.Ready)
            {
                logger.LogError(
                    "更新协调器未能就绪：事务={TransactionId}，信号={CoordinatorSignal}，消息={CoordinatorMessage}。",
                    transactionId,
                    signal.Signal,
                    signal.Message);
                preserveVerifiedCache = false;
                await WaitForCoordinatorExitAsync(coordinator);
                return Failed(signal.Message);
            }

            Report(
                progress,
                WindowsUpdateStage.Installing,
                "更新组件已就绪，正在安全退出应用…",
                canCancel: false);
            handoffCompleted = true;
            logger.LogInformation(
                "更新组件已就绪，正在退出应用并交接安装：事务={TransactionId}，目标版本={TargetVersion}，需要 UAC 提权={RequiresElevation}。",
                transactionId,
                targetVersion,
                requiresElevation);
            appWindowService.QuitApplication();
            return new WindowsPortableUpdateResult(
                WindowsPortableUpdateOutcome.ExitRequested,
                "应用正在退出并安装更新");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "用户取消了 Windows 绿色版自动更新：阶段={Stage}，目标版本={TargetVersion}，事务目录={TransactionDirectory}。",
                operationStage,
                targetVersion,
                transactionDirectory ?? "未创建");
            throw;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            logger.LogWarning(
                exception,
                "用户取消了 UAC 管理员授权：阶段={Stage}，目标版本={TargetVersion}，事务目录={TransactionDirectory}。",
                operationStage,
                targetVersion,
                transactionDirectory ?? "未创建");
            preserveVerifiedCache = false;
            return new WindowsPortableUpdateResult(
                WindowsPortableUpdateOutcome.Canceled,
                "已取消管理员授权，未修改任何程序文件");
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Windows 绿色版自动更新失败：阶段={Stage}，当前版本={CurrentVersion}，目标版本={TargetVersion}，资源={AssetName}，事务目录={TransactionDirectory}。",
                operationStage,
                currentVersion,
                targetVersion,
                asset.Name,
                transactionDirectory ?? "未创建");
            preserveVerifiedCache = false;
            return Failed(ToUserMessage(exception));
        }
        finally
        {
            var updaterProcessesStopped = true;
            if (!handoffCompleted && coordinator is not null)
            {
                updaterProcessesStopped = await TerminateProcessAsync(coordinator);
            }

            if (!handoffCompleted && externalWorkerProcessId is { } workerProcessId)
            {
                updaterProcessesStopped =
                    await WaitForProcessExitAsync(
                        workerProcessId,
                        TimeSpan.FromSeconds(30)) &&
                    updaterProcessesStopped;
            }

            coordinator?.Dispose();
            var cleanupCompleted = updaterProcessesStopped;
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

            if (!preserveVerifiedCache &&
                updaterProcessesStopped &&
                cleanupCompleted &&
                transactionDirectory is not null)
            {
                TryDeleteTransaction(transactionDirectory);
            }

            if (!handoffCompleted && (!updaterProcessesStopped || !cleanupCompleted))
            {
                logger.LogWarning(
                    "自动更新收尾未完全完成，可能需要下次启动继续清理：目标版本={TargetVersion}，事务目录={TransactionDirectory}，更新进程已停止={UpdaterProcessesStopped}，安全清理已完成={CleanupCompleted}。",
                    targetVersion,
                    transactionDirectory ?? "未创建",
                    updaterProcessesStopped,
                    cleanupCompleted);
            }
        }
    }

    private static string ValidateInstallationDirectory(string currentVersion)
    {
        var installationDirectory = UpdatePathSafety.EnsureNotFileSystemRoot(
            AppContext.BaseDirectory,
            allowExistingRoot: true);
        UpdatePathSafety.RejectReparsePoint(installationDirectory);

        var executablePath = Environment.ProcessPath
                             ?? throw new InvalidOperationException("无法确定当前主程序路径");
        if (!string.Equals(
                Path.GetDirectoryName(Path.GetFullPath(executablePath)),
                installationDirectory,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetFileName(executablePath),
                UpdateProtocol.EntryExecutableName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前程序不是受支持的 Windows 绿色版发布包");
        }

        var manifestPath = Path.Combine(installationDirectory, UpdateProtocol.ManifestFileName);
        UpdatePackageValidator.LoadAndValidateManifest(manifestPath, currentVersion);
        UpdatePackageValidator.ValidatePortableMarker(installationDirectory);
        var updaterPath = Path.Combine(installationDirectory, UpdateProtocol.UpdaterExecutableName);
        if (!File.Exists(updaterPath))
        {
            throw new FileNotFoundException("当前绿色版缺少独立更新组件，请先手动安装带自动更新功能的版本", updaterPath);
        }

        return installationDirectory;
    }

    private static async Task ValidateSpaceAndCurrentPackageAsync(
        string installationDirectory,
        string stagingDirectory,
        string currentVersion,
        string updatesRoot,
        long archiveBytes,
        CancellationToken cancellationToken)
    {
        var currentManifest = UpdatePackageValidator.LoadAndValidateManifest(
            Path.Combine(installationDirectory, UpdateProtocol.ManifestFileName),
            currentVersion);
        await UpdatePackageValidator.ValidateDirectoryAsync(
            installationDirectory,
            currentManifest,
            allowAdditionalFiles: true,
            cancellationToken);

        var owned = currentManifest.Files
            .Select(static file => UpdatePathSafety.NormalizeRelativePath(file.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        owned.Add(UpdateProtocol.ManifestFileName);
        long unknownBytes = 0;
        foreach (var path in EnumerateFiles(installationDirectory))
        {
            var relative = UpdatePathSafety.NormalizeRelativePath(
                Path.GetRelativePath(installationDirectory, path));
            if (!owned.Contains(relative))
            {
                checked
                {
                    unknownBytes += new FileInfo(path).Length;
                }
            }
        }

        long stagingBytes = 0;
        foreach (var path in EnumerateFiles(stagingDirectory))
        {
            checked
            {
                stagingBytes += new FileInfo(path).Length;
            }
        }

        EnsureAvailableSpace(
            installationDirectory,
            checked(stagingBytes + unknownBytes + archiveBytes + DiskSpaceMargin),
            "程序所在磁盘");
        EnsureAvailableSpace(updatesRoot, DiskSpaceMargin, "更新暂存磁盘");
    }

    private static async Task<PreparedUpdateTransaction> PrepareTransactionRequestAsync(
        string transactionId,
        string transactionDirectory,
        string installationDirectory,
        string stagingDirectory,
        ReleaseAssetInfo asset,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        var updaterSource = Path.Combine(installationDirectory, UpdateProtocol.UpdaterExecutableName);
        var updaterDestination = Path.Combine(transactionDirectory, UpdateProtocol.UpdaterExecutableName);
        var currentManifest = UpdatePackageValidator.LoadAndValidateManifest(
            Path.Combine(installationDirectory, UpdateProtocol.ManifestFileName));
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

        var parent = Path.GetDirectoryName(installationDirectory)
                      ?? throw new InvalidOperationException("无法确定安装目录父目录");
        using var currentProcess = Process.GetCurrentProcess();
        var request = new UpdateTransactionRequest(
            UpdateProtocol.SchemaVersion,
            transactionId,
            Environment.ProcessId,
            new DateTimeOffset(currentProcess.StartTime.ToUniversalTime(), TimeSpan.Zero),
            currentManifest.Version,
            targetVersion,
            installationDirectory,
            stagingDirectory,
            transactionDirectory,
            Path.Combine(transactionDirectory, "package.zip"),
            Path.Combine(parent, $".IGoLibrary-Ex.update-{transactionId}"),
            Path.Combine(parent, $".IGoLibrary-Ex.backup-{transactionId}"),
            UpdateProtocol.EntryExecutableName,
            UpdateProtocol.ManifestFileName,
            asset.Digest,
            asset.Size,
            Path.Combine(transactionDirectory, "health.json"),
            Path.Combine(transactionDirectory, "coordinator-signal.json"),
            Path.Combine(transactionDirectory, "worker-ready.json"),
            Path.Combine(transactionDirectory, "worker-status.json"),
            Path.Combine(transactionDirectory, "decision.json"),
            Path.Combine(transactionDirectory, "heartbeat.txt"),
            Path.Combine(transactionDirectory, "launched-process.json"),
            Path.Combine(GetUpdatesRoot(), "logs"));
        var requestPath = Path.Combine(transactionDirectory, "request.json");
        UpdateJsonFile.WriteAtomic(requestPath, request);
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
                var signal = UpdateJsonFile.Read<UpdateCoordinatorSignal>(request.CoordinatorReadyPath);
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
                timeout.Token);
            var result = await UpdatePipeProtocol.ReadAsync<UpdateBootstrapResult>(
                pipe,
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

    private static async Task<bool> WaitForProcessExitAsync(
        int processId,
        TimeSpan timeout)
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
                    var completion = UpdateJsonFile.Read<UpdateCleanupCompletion>(completionPath);
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

    private static async Task VerifyArchiveDigestAsync(
        string archivePath,
        ReleaseAssetInfo asset,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(archivePath);
        if (!info.Exists || info.Length != asset.Size)
        {
            throw new InvalidDataException("更新包实际大小与 GitHub 声明不一致");
        }

        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(digest, asset.Digest[7..], StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新包 SHA-256 校验失败");
        }
    }

    private static async Task<VerifiedCachePaths?> TryFindVerifiedCacheAsync(
        string updatesRoot,
        ReleaseAssetInfo asset,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(updatesRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (!Guid.TryParseExact(name, "N", out _) ||
                File.Exists(Path.Combine(directory, "request.json")))
            {
                continue;
            }

            var markerPath = Path.Combine(directory, "verified-cache.json");
            if (!File.Exists(markerPath))
            {
                continue;
            }

            try
            {
                var cache = UpdateJsonFile.Read<VerifiedUpdateCache>(markerPath);
                if (cache.SchemaVersion != UpdateProtocol.SchemaVersion ||
                    !string.Equals(cache.TransactionId, name, StringComparison.Ordinal) ||
                    !string.Equals(cache.TargetVersion, targetVersion, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(cache.PackageDigest, asset.Digest, StringComparison.OrdinalIgnoreCase) ||
                    cache.PackageSize != asset.Size ||
                    DateTimeOffset.UtcNow - cache.VerifiedAtUtc > TimeSpan.FromDays(7))
                {
                    continue;
                }

                var archivePath = Path.Combine(directory, "package.zip");
                var stagingDirectory = Path.Combine(directory, "staging");
                await VerifyArchiveDigestAsync(archivePath, asset, cancellationToken);
                var manifest = UpdatePackageValidator.LoadAndValidateManifest(
                    Path.Combine(stagingDirectory, UpdateProtocol.ManifestFileName),
                    targetVersion);
                await UpdatePackageValidator.ValidateDirectoryAsync(
                    stagingDirectory,
                    manifest,
                    allowAdditionalFiles: false,
                    cancellationToken);
                return new VerifiedCachePaths(name, directory, archivePath, stagingDirectory);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                TryDeleteTransaction(directory);
            }
        }

        return null;
    }

    private static void EnsureAvailableSpace(string path, long requiredBytes, string label)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))
                   ?? throw new InvalidOperationException($"无法确定{label}卷");
        var drive = new DriveInfo(root);
        if (drive.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException(
                $"{label}空间不足，至少还需要 {FormatMiB(requiredBytes - drive.AvailableFreeSpace)} MiB");
        }
    }

    private static IEnumerable<string> EnumerateFiles(string rootDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(rootDirectory);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"目录包含不受支持的链接或联接：{entry}");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }

    private static string GetUpdatesRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UpdateProtocol.ProductName,
            "updates");
    }

    private static void Report(
        IProgress<WindowsUpdateProgress> progress,
        WindowsUpdateStage stage,
        string status,
        long completedBytes = 0,
        long totalBytes = 0,
        bool canCancel = true)
    {
        progress.Report(new WindowsUpdateProgress(
            stage,
            completedBytes,
            totalBytes,
            status,
            canCancel));
    }

    private static WindowsPortableUpdateResult Blocked(
        IReadOnlyList<string> tasks,
        string? suffix = null)
    {
        var message = $"以下任务仍在运行，请先停止：{string.Join("、", tasks)}";
        if (!string.IsNullOrWhiteSpace(suffix))
        {
            message += suffix;
        }

        return new WindowsPortableUpdateResult(WindowsPortableUpdateOutcome.Blocked, message);
    }

    private static WindowsPortableUpdateResult Failed(string message)
    {
        return new WindowsPortableUpdateResult(WindowsPortableUpdateOutcome.Failed, message);
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

    private static long FormatMiB(long bytes)
    {
        return Math.Max(1, (long)Math.Ceiling(bytes / 1024d / 1024d));
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
                    DateTimeOffset.UtcNow));
        }
        catch
        {
        }
    }

    private static void TryDeleteTransaction(string transactionDirectory)
    {
        try
        {
            var root = Path.GetFullPath(GetUpdatesRoot());
            var target = Path.GetFullPath(transactionDirectory);
            if (target.StartsWith(UpdatePathSafety.EnsureTrailingSeparator(root), StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeletePayload(string transactionDirectory)
    {
        try
        {
            var archive = Path.Combine(transactionDirectory, "package.zip");
            if (File.Exists(archive))
            {
                File.Delete(archive);
            }

            var staging = Path.Combine(transactionDirectory, "staging");
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed record VerifiedUpdateCache(
        int SchemaVersion,
        string TransactionId,
        string TargetVersion,
        string PackageDigest,
        long PackageSize,
        DateTimeOffset VerifiedAtUtc);

    private sealed record VerifiedCachePaths(
        string TransactionId,
        string TransactionDirectory,
        string ArchivePath,
        string StagingDirectory);

    private sealed record PreparedUpdateTransaction(
        UpdateTransactionRequest Request,
        string RequestPath,
        string CoordinatorPath,
        string TrustedUpdaterPath);
}

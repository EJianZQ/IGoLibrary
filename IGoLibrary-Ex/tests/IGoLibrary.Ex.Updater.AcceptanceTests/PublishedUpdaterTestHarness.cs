using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Updater.AcceptanceTests;

internal static class PublishedUpdaterEnvironment
{
    public static string AotUpdaterPath => GetRequiredFile("IGOLIBRARY_AOT_UPDATER_PATH");

    public static string ManagedUpdaterBaselinePath =>
        GetRequiredFile("IGOLIBRARY_MANAGED_UPDATER_BASELINE_PATH");

    public static string TestProcessOutputDirectory =>
        GetRequiredDirectory("IGOLIBRARY_TEST_PROCESS_OUTPUT");

    private static string GetRequiredFile(string variableName)
    {
        var path = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"发布验收缺少环境变量：{variableName}");
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"发布验收文件不存在：{variableName}", fullPath);
        }

        return fullPath;
    }

    private static string GetRequiredDirectory(string variableName)
    {
        var path = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"发布验收缺少环境变量：{variableName}");
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"发布验收目录不存在：{variableName}={fullPath}");
        }

        return fullPath;
    }
}

internal sealed class AcceptanceDirectory : IAsyncDisposable
{
    public AcceptanceDirectory(string scenarioName)
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "IGoLibrary-AOT-验收",
            $"{scenarioName} {Guid.NewGuid():N}");
        var longUnicodeRoot = Path.Combine(
            Root,
            "包含 空格与中文",
            new string('长', 20));
        InstallationDirectory = Path.Combine(longUnicodeRoot, "安装 目录", "IGoLibrary-Ex");
        UpdatesDirectory = Path.Combine(longUnicodeRoot, "更新 事务");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string InstallationDirectory { get; }

    public string UpdatesDirectory { get; }

    public async ValueTask DisposeAsync()
    {
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 30)
                {
                    return;
                }

                await Task.Delay(100);
            }
        }
    }
}

internal sealed record TransactionArtifacts(
    string TransactionId,
    string ControlDirectory,
    string StagingDirectory,
    string PackagePath,
    string RequestPath,
    string TransactionUpdaterPath,
    long PackageSize,
    string PackageDigest);

internal static class PublishedUpdaterTestHarness
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(30);
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static async Task CreateInstallationAsync(
        AcceptanceDirectory scenario,
        string version,
        string updaterPath,
        bool createPreservedTools = true)
    {
        await CreatePayloadAsync(
            scenario.InstallationDirectory,
            version,
            updaterPath,
            invalidEntryExecutable: false,
            includeManagedCloudflared: false);
        if (createPreservedTools)
        {
            var toolsDirectory = Path.Combine(
                scenario.InstallationDirectory,
                UpdateProtocol.PreservedToolsDirectoryName,
                "cloudflared 子目录");
            Directory.CreateDirectory(toolsDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(toolsDirectory, "保留.txt"),
                "preserved-tools-content",
                Utf8NoBom);
        }
    }

    public static async Task<TransactionArtifacts> CreateTransactionArtifactsAsync(
        AcceptanceDirectory scenario,
        string targetVersion,
        string targetUpdaterPath,
        string transactionUpdaterPath,
        bool corruptPayloadAfterManifest = false,
        bool invalidEntryExecutable = false,
        bool corruptManagedCloudflaredAfterManifest = false,
        bool includeManagedCloudflared = true)
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var controlDirectory = Path.Combine(scenario.UpdatesDirectory, transactionId);
        var stagingDirectory = Path.Combine(controlDirectory, "staging");
        var packagePath = Path.Combine(controlDirectory, "package.zip");
        var requestPath = Path.Combine(controlDirectory, "request.json");
        var copiedUpdaterPath = Path.Combine(
            controlDirectory,
            UpdateProtocol.UpdaterExecutableName);
        Directory.CreateDirectory(controlDirectory);

        await CreatePayloadAsync(
            stagingDirectory,
            targetVersion,
            targetUpdaterPath,
            invalidEntryExecutable,
            includeManagedCloudflared);
        if (corruptPayloadAfterManifest)
        {
            var tamperPath = corruptManagedCloudflaredAfterManifest
                ? UpdatePathSafety.GetSafeChildPath(
                    stagingDirectory,
                    UpdateProtocol.ManagedCloudflaredExecutablePath)
                : Path.Combine(stagingDirectory, "version.txt");
            await File.WriteAllTextAsync(
                tamperPath,
                targetVersion + "-tampered",
                Utf8NoBom);
        }

        ZipFile.CreateFromDirectory(
            stagingDirectory,
            packagePath,
            CompressionLevel.Fastest,
            includeBaseDirectory: false);
        File.Copy(transactionUpdaterPath, copiedUpdaterPath, overwrite: false);

        var packageInfo = new FileInfo(packagePath);
        return new TransactionArtifacts(
            transactionId,
            controlDirectory,
            stagingDirectory,
            packagePath,
            requestPath,
            copiedUpdaterPath,
            packageInfo.Length,
            "sha256:" + await ComputeSha256Async(packagePath));
    }

    public static UpdateTransactionRequest CreateRequest(
        AcceptanceDirectory scenario,
        TransactionArtifacts artifacts,
        string currentVersion,
        string targetVersion,
        int parentProcessId,
        DateTimeOffset parentProcessStartedAtUtc)
    {
        var installationParent = Path.GetDirectoryName(scenario.InstallationDirectory)
                                 ?? throw new InvalidOperationException(
                                     "无法确定测试安装目录父目录");
        return new UpdateTransactionRequest(
            UpdateProtocol.SchemaVersion,
            artifacts.TransactionId,
            parentProcessId,
            parentProcessStartedAtUtc,
            currentVersion,
            targetVersion,
            scenario.InstallationDirectory,
            artifacts.StagingDirectory,
            artifacts.ControlDirectory,
            artifacts.PackagePath,
            Path.Combine(
                installationParent,
                ".IGoLibrary-Ex.update-" + artifacts.TransactionId),
            Path.Combine(
                installationParent,
                ".IGoLibrary-Ex.backup-" + artifacts.TransactionId),
            UpdateProtocol.EntryExecutableName,
            UpdateProtocol.ManifestFileName,
            artifacts.PackageDigest,
            artifacts.PackageSize,
            Path.Combine(artifacts.ControlDirectory, "health.json"),
            Path.Combine(artifacts.ControlDirectory, "coordinator-signal.json"),
            Path.Combine(artifacts.ControlDirectory, "worker-ready.json"),
            Path.Combine(artifacts.ControlDirectory, "worker-status.json"),
            Path.Combine(artifacts.ControlDirectory, "decision.json"),
            Path.Combine(artifacts.ControlDirectory, "heartbeat.txt"),
            Path.Combine(artifacts.ControlDirectory, "launched-process.json"),
            Path.Combine(scenario.UpdatesDirectory, "logs"));
    }

    public static void WriteRequest(
        TransactionArtifacts artifacts,
        UpdateTransactionRequest request)
    {
        UpdateJsonFile.WriteAtomic(
            artifacts.RequestPath,
            request,
            UpdateJsonTypeInfo.TransactionRequest);
    }

    public static async Task RunWorkerTransactionAsync(
        AcceptanceDirectory scenario,
        string currentVersion,
        string targetVersion,
        string targetUpdaterPath,
        string transactionUpdaterPath,
        UpdateDecisionKind? decision,
        bool corruptArchiveAfterDigest = false,
        bool corruptPayloadAfterManifest = false,
        bool corruptManagedCloudflaredAfterManifest = false,
        bool includeManagedCloudflared = true)
    {
        var artifacts = await CreateTransactionArtifactsAsync(
            scenario,
            targetVersion,
            targetUpdaterPath,
            transactionUpdaterPath,
            corruptPayloadAfterManifest,
            corruptManagedCloudflaredAfterManifest: corruptManagedCloudflaredAfterManifest,
            includeManagedCloudflared: includeManagedCloudflared);
        await using var parent = await ParentProcessLease.StartAsync(
            scenario.InstallationDirectory,
            artifacts.ControlDirectory);
        var request = CreateRequest(
            scenario,
            artifacts,
            currentVersion,
            targetVersion,
            parent.ProcessId,
            parent.StartedAtUtc);
        WriteRequest(artifacts, request);

        if (corruptArchiveAfterDigest)
        {
            CorruptOneByte(artifacts.PackagePath);
        }

        await using var heartbeat = await HeartbeatLease.StartAsync(request.HeartbeatPath);
        using var worker = StartProcess(
            artifacts.TransactionUpdaterPath,
            ["--worker", "--request", artifacts.RequestPath],
            artifacts.ControlDirectory);
        try
        {
            await WaitForFileAsync(
                request.WorkerReadyPath,
                ShortTimeout,
                "worker 就绪信号",
                worker);
            parent.Release();
            await parent.WaitForExitAsync(ShortTimeout);

            if (decision is null)
            {
                var failureExitCode = await WaitForExitCodeAsync(worker, ShortTimeout);
                Assert.Equal(1, failureExitCode);
                var failedStatus = await WaitForWorkerPhaseAsync(
                    request,
                    UpdateWorkerPhase.Failed,
                    ShortTimeout);
                Assert.Contains(
                    corruptArchiveAfterDigest ? "SHA-256" : "不匹配",
                    failedStatus.Message,
                    StringComparison.OrdinalIgnoreCase);
                Assert.False(Directory.Exists(request.BackupDirectory));
                Assert.False(Directory.Exists(request.CandidateDirectory));
                await AssertInstalledVersionAsync(
                    scenario.InstallationDirectory,
                    currentVersion,
                    expectedUpdaterPath: null,
                    expectPreservedTools: true);
                return;
            }

            await WaitForWorkerPhaseAsync(
                request,
                UpdateWorkerPhase.Applied,
                TimeSpan.FromMinutes(2));
            UpdateJsonFile.WriteAtomic(
                request.DecisionPath,
                new UpdateDecision(
                    UpdateProtocol.SchemaVersion,
                    request.TransactionId,
                    decision.Value,
                    DateTimeOffset.UtcNow),
                UpdateJsonTypeInfo.Decision);

            var expectedFinalPhase = decision == UpdateDecisionKind.Commit
                ? UpdateWorkerPhase.Committed
                : UpdateWorkerPhase.RolledBack;
            await WaitForWorkerPhaseAsync(
                request,
                expectedFinalPhase,
                TimeSpan.FromMinutes(2));
            Assert.Equal(0, await WaitForExitCodeAsync(worker, ShortTimeout));
            await AssertInstalledVersionAsync(
                scenario.InstallationDirectory,
                decision == UpdateDecisionKind.Commit ? targetVersion : currentVersion,
                decision == UpdateDecisionKind.Commit ? targetUpdaterPath : null,
                expectPreservedTools: true,
                expectedManagedCloudflaredVersion:
                    decision == UpdateDecisionKind.Commit && includeManagedCloudflared
                        ? targetVersion
                        : null);
            Assert.False(Directory.Exists(request.BackupDirectory));
            Assert.False(Directory.Exists(request.CandidateDirectory));
        }
        finally
        {
            if (!worker.HasExited)
            {
                worker.Kill(entireProcessTree: true);
                await worker.WaitForExitAsync();
            }
        }
    }

    public static async Task AssertInstalledVersionAsync(
        string installationDirectory,
        string expectedVersion,
        string? expectedUpdaterPath,
        bool expectPreservedTools,
        string? expectedManagedCloudflaredVersion = null)
    {
        var manifest = UpdatePackageValidator.LoadAndValidateManifest(
            Path.Combine(installationDirectory, UpdateProtocol.ManifestFileName),
            expectedVersion);
        await UpdatePackageValidator.ValidateInstalledDirectoryAsync(
            installationDirectory,
            manifest);
        Assert.Equal(
            expectedVersion,
            await File.ReadAllTextAsync(Path.Combine(installationDirectory, "version.txt")));
        if (expectedUpdaterPath is not null)
        {
            Assert.Equal(
                await ComputeSha256Async(expectedUpdaterPath),
                await ComputeSha256Async(Path.Combine(
                    installationDirectory,
                    UpdateProtocol.UpdaterExecutableName)));
        }

        var preservedPath = Path.Combine(
            installationDirectory,
            UpdateProtocol.PreservedToolsDirectoryName,
            "cloudflared 子目录",
            "保留.txt");
        Assert.Equal(expectPreservedTools, File.Exists(preservedPath));
        if (expectPreservedTools)
        {
            Assert.Equal("preserved-tools-content", await File.ReadAllTextAsync(preservedPath));
        }

        if (expectedManagedCloudflaredVersion is not null)
        {
            Assert.Equal(
                $"cloudflared-{expectedManagedCloudflaredVersion}",
                await File.ReadAllTextAsync(UpdatePathSafety.GetSafeChildPath(
                    installationDirectory,
                    UpdateProtocol.ManagedCloudflaredExecutablePath)));
            Assert.Equal(
                $"license-{expectedManagedCloudflaredVersion}",
                await File.ReadAllTextAsync(UpdatePathSafety.GetSafeChildPath(
                    installationDirectory,
                    UpdateProtocol.ManagedCloudflaredLicensePath)));
            Assert.Equal(
                $"notices-{expectedManagedCloudflaredVersion}",
                await File.ReadAllTextAsync(UpdatePathSafety.GetSafeChildPath(
                    installationDirectory,
                    UpdateProtocol.ManagedCloudflaredNoticesPath)));
        }
    }

    public static void AssertManagedCloudflaredAbsent(string installationDirectory)
    {
        foreach (var relativePath in UpdateProtocol.ManagedCloudflaredFilePaths)
        {
            Assert.False(File.Exists(UpdatePathSafety.GetSafeChildPath(
                installationDirectory,
                relativePath)));
        }
    }

    public static Process StartProcess(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        bool createNoWindow = true)
    {
        var startInfo = new ProcessStartInfo(Path.GetFullPath(executablePath))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetFullPath(workingDirectory),
            CreateNoWindow = createNoWindow
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException(
                   $"无法启动发布验收进程：{executablePath}");
    }

    public static async Task<int> WaitForExitCodeAsync(Process process, TimeSpan timeout)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout);
            return process.ExitCode;
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            throw new TimeoutException(
                $"进程 {process.Id} 在 {timeout} 内未退出");
        }
    }

    public static async Task<UpdateWorkerStatus> WaitForWorkerPhaseAsync(
        UpdateTransactionRequest request,
        UpdateWorkerPhase phase,
        TimeSpan timeout)
    {
        UpdateWorkerStatus? observed = null;
        await WaitUntilAsync(
            () =>
            {
                try
                {
                    if (!File.Exists(request.WorkerStatusPath))
                    {
                        return false;
                    }

                    observed = UpdateJsonFile.Read(
                        request.WorkerStatusPath,
                        UpdateJsonTypeInfo.WorkerStatus);
                    return observed.SchemaVersion == UpdateProtocol.SchemaVersion &&
                           string.Equals(
                               observed.TransactionId,
                               request.TransactionId,
                               StringComparison.Ordinal) &&
                           observed.Phase == phase;
                }
                catch (Exception exception) when (
                    exception is IOException or JsonException)
                {
                    return false;
                }
            },
            timeout,
            $"worker 阶段 {phase}");
        return observed!;
    }

    public static async Task<UpdateCoordinatorSignal> WaitForCoordinatorReadyAsync(
        UpdateTransactionRequest request,
        Process coordinator,
        TimeSpan timeout)
    {
        UpdateCoordinatorSignal? observed = null;
        await WaitUntilAsync(
            () =>
            {
                if (coordinator.HasExited)
                {
                    throw new InvalidOperationException(
                        $"协调器在就绪前退出，退出码：{coordinator.ExitCode}");
                }

                try
                {
                    if (!File.Exists(request.CoordinatorReadyPath))
                    {
                        return false;
                    }

                    observed = UpdateJsonFile.Read(
                        request.CoordinatorReadyPath,
                        UpdateJsonTypeInfo.CoordinatorSignal);
                    return observed.SchemaVersion == UpdateProtocol.SchemaVersion &&
                           string.Equals(
                               observed.TransactionId,
                               request.TransactionId,
                               StringComparison.Ordinal) &&
                           observed.Signal == UpdateCoordinatorSignalKind.Ready;
                }
                catch (Exception exception) when (
                    exception is IOException or JsonException)
                {
                    return false;
                }
            },
            timeout,
            "协调器就绪信号");
        return observed!;
    }

    public static async Task CloseFailureDialogAndWaitAsync(
        Process coordinator,
        TimeSpan timeout)
    {
        await WaitUntilAsync(
            () =>
            {
                if (coordinator.HasExited)
                {
                    throw new InvalidOperationException(
                        $"协调器未显示失败窗口便退出，退出码：{coordinator.ExitCode}");
                }

                coordinator.Refresh();
                if (string.Equals(
                        coordinator.MainWindowTitle,
                        "我去图书馆 - 更新程序",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "发布后的 updater 无法创建 TaskDialog，已退回 MessageBox");
                }

                return string.Equals(
                    coordinator.MainWindowTitle,
                    "我去图书馆 - 自动更新",
                    StringComparison.Ordinal);
            },
            timeout,
            "失败 TaskDialog");

        Assert.True(coordinator.CloseMainWindow(), "无法关闭失败 TaskDialog");
        Assert.Equal(1, await WaitForExitCodeAsync(coordinator, ShortTimeout));
    }

    public static async Task WaitForFileAsync(
        string path,
        TimeSpan timeout,
        string description,
        Process? watchedProcess = null)
    {
        await WaitUntilAsync(
            () =>
            {
                if (watchedProcess?.HasExited == true)
                {
                    throw new InvalidOperationException(
                        $"等待{description}时进程提前退出，退出码：{watchedProcess.ExitCode}");
                }

                return File.Exists(path);
            },
            timeout,
            description);
    }

    public static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string description)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"等待{description}超时：{timeout}");
    }

    public static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static async Task CreatePayloadAsync(
        string destinationDirectory,
        string version,
        string updaterPath,
        bool invalidEntryExecutable,
        bool includeManagedCloudflared)
    {
        Directory.CreateDirectory(destinationDirectory);
        var testProcessOutput = PublishedUpdaterEnvironment.TestProcessOutputDirectory;
        var testProcessExecutable = Path.Combine(
            testProcessOutput,
            "IGoLibrary.Ex.TestProcess.exe");
        var requiredHelperFiles = new[]
        {
            testProcessExecutable,
            Path.Combine(testProcessOutput, "IGoLibrary.Ex.TestProcess.dll"),
            Path.Combine(testProcessOutput, "IGoLibrary.Ex.TestProcess.deps.json"),
            Path.Combine(testProcessOutput, "IGoLibrary.Ex.TestProcess.runtimeconfig.json")
        };
        foreach (var requiredFile in requiredHelperFiles)
        {
            if (!File.Exists(requiredFile))
            {
                throw new FileNotFoundException("测试应用构建产物缺失", requiredFile);
            }
        }

        File.Copy(
            testProcessExecutable,
            Path.Combine(destinationDirectory, UpdateProtocol.EntryExecutableName));
        foreach (var helperFile in requiredHelperFiles.Skip(1))
        {
            File.Copy(helperFile, Path.Combine(destinationDirectory, Path.GetFileName(helperFile)));
        }

        if (invalidEntryExecutable)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(destinationDirectory, UpdateProtocol.EntryExecutableName),
                "not-a-windows-executable"u8.ToArray());
        }

        File.Copy(
            updaterPath,
            Path.Combine(destinationDirectory, UpdateProtocol.UpdaterExecutableName));
        await File.WriteAllBytesAsync(
            Path.Combine(destinationDirectory, UpdateProtocol.PortableMarkerFileName),
            Encoding.UTF8.GetBytes(UpdateProtocol.PortableMarkerContent));
        await File.WriteAllTextAsync(
            Path.Combine(destinationDirectory, "version.txt"),
            version,
            Utf8NoBom);
        if (includeManagedCloudflared)
        {
            await WriteManagedCloudflaredFileAsync(
                destinationDirectory,
                UpdateProtocol.ManagedCloudflaredExecutablePath,
                $"cloudflared-{version}");
            await WriteManagedCloudflaredFileAsync(
                destinationDirectory,
                UpdateProtocol.ManagedCloudflaredLicensePath,
                $"license-{version}");
            await WriteManagedCloudflaredFileAsync(
                destinationDirectory,
                UpdateProtocol.ManagedCloudflaredNoticesPath,
                $"notices-{version}");
        }

        var manifestFiles = new List<UpdateManifestFile>();
        var relativePaths = Directory.EnumerateFiles(
                destinationDirectory,
                "*",
                SearchOption.AllDirectories)
            .Select(path => UpdatePathSafety.NormalizeRelativePath(
                Path.GetRelativePath(destinationDirectory, path)))
            .Where(path => !string.Equals(
                path,
                UpdateProtocol.ManifestFileName,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.Ordinal);
        foreach (var relativePath in relativePaths)
        {
            var fullPath = UpdatePathSafety.GetSafeChildPath(
                destinationDirectory,
                relativePath);
            manifestFiles.Add(new UpdateManifestFile(
                relativePath,
                new FileInfo(fullPath).Length,
                await ComputeSha256Async(fullPath)));
        }

        UpdateJsonFile.WriteAtomic(
            Path.Combine(destinationDirectory, UpdateProtocol.ManifestFileName),
            new UpdatePackageManifest(
                UpdateProtocol.SchemaVersion,
                UpdateProtocol.ProductName,
                version,
                UpdateProtocol.WindowsX64Runtime,
                UpdateProtocol.EntryExecutableName,
                manifestFiles),
            UpdateJsonTypeInfo.PackageManifest);
    }

    private static async Task WriteManagedCloudflaredFileAsync(
        string rootDirectory,
        string relativePath,
        string content)
    {
        var path = UpdatePathSafety.GetSafeChildPath(rootDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, Utf8NoBom);
    }

    private static void CorruptOneByte(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        var position = Math.Max(0, stream.Length / 2);
        stream.Position = position;
        var value = stream.ReadByte();
        if (value < 0)
        {
            throw new InvalidDataException("无法篡改空更新包");
        }

        stream.Position = position;
        stream.WriteByte((byte)(value ^ 0x5A));
        stream.Flush(flushToDisk: true);
    }
}

internal sealed class ParentProcessLease : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _releasePath;
    private bool _released;

    private ParentProcessLease(Process process, string releasePath)
    {
        _process = process;
        _releasePath = releasePath;
        StartedAtUtc = new DateTimeOffset(
            process.StartTime.ToUniversalTime(),
            TimeSpan.Zero);
    }

    public int ProcessId => _process.Id;

    public DateTimeOffset StartedAtUtc { get; }

    public static async Task<ParentProcessLease> StartAsync(
        string installationDirectory,
        string controlDirectory)
    {
        var readyPath = Path.Combine(controlDirectory, "test-parent-ready.txt");
        var releasePath = Path.Combine(controlDirectory, "test-parent-release.txt");
        var executable = Path.Combine(
            installationDirectory,
            UpdateProtocol.EntryExecutableName);
        var process = PublishedUpdaterTestHarness.StartProcess(
            executable,
            ["wait-for-release", readyPath, releasePath],
            installationDirectory);
        var lease = new ParentProcessLease(process, releasePath);
        try
        {
            await PublishedUpdaterTestHarness.WaitForFileAsync(
                readyPath,
                TimeSpan.FromSeconds(30),
                "旧应用就绪信号",
                process);
            return lease;
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    public void Release()
    {
        if (_released)
        {
            return;
        }

        File.WriteAllText(_releasePath, "release");
        _released = true;
    }

    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        Assert.Equal(
            0,
            await PublishedUpdaterTestHarness.WaitForExitCodeAsync(_process, timeout));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                Release();
                try
                {
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
        }
        finally
        {
            _process.Dispose();
        }
    }
}

internal sealed class HeartbeatLease : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly Task _task;

    private HeartbeatLease(CancellationTokenSource cancellation, Task task)
    {
        _cancellation = cancellation;
        _task = task;
    }

    public static async Task<HeartbeatLease> StartAsync(string heartbeatPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(heartbeatPath)!);
        await File.WriteAllTextAsync(heartbeatPath, DateTimeOffset.UtcNow.ToString("O"));
        var cancellation = new CancellationTokenSource();
        var task = RunAsync(heartbeatPath, cancellation.Token);
        return new HeartbeatLease(cancellation, task);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        try
        {
            await _task;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

    private static async Task RunAsync(string heartbeatPath, CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            await File.WriteAllTextAsync(
                heartbeatPath,
                DateTimeOffset.UtcNow.ToString("O"),
                cancellationToken);
        }
    }
}

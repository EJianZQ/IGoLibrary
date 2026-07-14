namespace IGoLibrary.Ex.Updater.Core;

public static class UpdateTransaction
{
    private const string CandidatePrefix = ".IGoLibrary-Ex.update-";
    private const string BackupPrefix = ".IGoLibrary-Ex.backup-";
    private const string SecureWorkingPrefix = ".IGoLibrary-Ex.secure-";

    public static void ValidateRequest(UpdateTransactionRequest request)
    {
        ValidateRequestShape(request);

        var installation = UpdatePathSafety.EnsureNotFileSystemRoot(
            request.InstallationDirectory,
            allowExistingRoot: true);
        if (!Directory.Exists(installation))
        {
            throw new DirectoryNotFoundException($"安装目录不存在：{installation}");
        }

        UpdatePathSafety.RejectReparsePoint(installation);
        UpdatePackageValidator.ValidatePortableMarker(installation);
        var entryExecutable = Path.Combine(installation, request.EntryExecutable);
        if (!File.Exists(entryExecutable))
        {
            throw new FileNotFoundException("安装目录缺少主程序", entryExecutable);
        }

        var working = UpdatePathSafety.EnsureNotFileSystemRoot(
            request.WorkingDirectory,
            allowExistingRoot: true);
        if (!Directory.Exists(working))
        {
            throw new DirectoryNotFoundException($"更新工作目录不存在：{working}");
        }

        UpdatePathSafety.RejectReparsePoint(working);
        UpdatePathSafety.EnsureSiblingDirectory(
            installation,
            request.CandidateDirectory,
            CandidatePrefix + request.TransactionId);
        UpdatePathSafety.EnsureSiblingDirectory(
            installation,
            request.BackupDirectory,
            BackupPrefix + request.TransactionId);
        var installationParent = Path.GetDirectoryName(installation)
                                 ?? throw new InvalidOperationException("无法确定安装目录父目录");
        EnsureExactPath(
            request.CandidateDirectory,
            Path.Combine(installationParent, CandidatePrefix + request.TransactionId));
        EnsureExactPath(
            request.BackupDirectory,
            Path.Combine(installationParent, BackupPrefix + request.TransactionId));
    }

    public static void ValidateRequestFile(
        string requestPath,
        UpdateTransactionRequest request)
    {
        ValidateRequestShape(request);
        var fullRequestPath = Path.GetFullPath(requestPath);
        var requestDirectory = Path.GetDirectoryName(fullRequestPath)
                               ?? throw new InvalidDataException("无法确定更新请求目录");
        if (!Directory.Exists(requestDirectory))
        {
            throw new DirectoryNotFoundException($"更新请求目录不存在：{requestDirectory}");
        }

        UpdatePathSafety.RejectReparsePoint(requestDirectory);
        EnsureExactPath(requestDirectory, request.WorkingDirectory);
        if (!string.Equals(
                Path.GetFileName(fullRequestPath),
                "request.json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新请求文件名无效");
        }

        var controlDirectory = Path.GetDirectoryName(Path.GetFullPath(request.HealthReportPath))
                               ?? throw new InvalidDataException("无法确定更新控制目录");
        if (!Directory.Exists(controlDirectory) ||
            !string.Equals(
                Path.GetFileName(controlDirectory),
                request.TransactionId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("更新控制目录格式无效");
        }

        UpdatePathSafety.RejectReparsePoint(controlDirectory);
        EnsureExactPath(request.StagingDirectory, Path.Combine(controlDirectory, "staging"));
        EnsureExactPath(request.PackagePath, Path.Combine(request.WorkingDirectory, "package.zip"));
        EnsureExactPath(request.HealthReportPath, Path.Combine(controlDirectory, "health.json"));
        EnsureExactPath(request.CoordinatorReadyPath, Path.Combine(controlDirectory, "coordinator-signal.json"));
        EnsureExactPath(request.WorkerReadyPath, Path.Combine(controlDirectory, "worker-ready.json"));
        EnsureExactPath(request.WorkerStatusPath, Path.Combine(controlDirectory, "worker-status.json"));
        EnsureExactPath(request.DecisionPath, Path.Combine(controlDirectory, "decision.json"));
        EnsureExactPath(request.HeartbeatPath, Path.Combine(controlDirectory, "heartbeat.txt"));
        EnsureExactPath(request.LaunchedProcessPath, Path.Combine(controlDirectory, "launched-process.json"));

        var updatesRoot = Path.GetDirectoryName(controlDirectory)
                          ?? throw new InvalidDataException("无法确定更新根目录");
        EnsureExactPath(request.LogDirectory, Path.Combine(updatesRoot, "logs"));

        if (!string.Equals(
                request.WorkingDirectory,
                controlDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            EnsureExactPath(request.WorkingDirectory, GetSecureWorkingDirectory(request));
        }
    }

    public static string GetSecureWorkingDirectory(UpdateTransactionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parent = Path.GetDirectoryName(Path.GetFullPath(request.InstallationDirectory))
                     ?? throw new InvalidOperationException("无法确定安装目录父目录");
        return Path.Combine(parent, SecureWorkingPrefix + request.TransactionId);
    }

    public static async Task PrepareCandidateAsync(
        UpdateTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        if (Directory.Exists(request.CandidateDirectory))
        {
            throw new IOException($"候选目录已经存在：{request.CandidateDirectory}");
        }

        if (Directory.Exists(request.BackupDirectory))
        {
            throw new IOException($"备份目录已经存在：{request.BackupDirectory}");
        }

        var currentManifestPath = Path.Combine(request.InstallationDirectory, request.ManifestFileName);
        var currentManifest = UpdatePackageValidator.LoadAndValidateManifest(
            currentManifestPath,
            request.CurrentVersion);
        var nextManifest = UpdatePackageValidator.LoadAndValidateManifest(
            Path.Combine(request.StagingDirectory, request.ManifestFileName),
            request.TargetVersion);
        await UpdatePackageValidator.ValidateUpdatePayloadDirectoryAsync(
            request.StagingDirectory,
            nextManifest,
            cancellationToken);

        Directory.CreateDirectory(request.CandidateDirectory);
        try
        {
            await CopyDirectoryAsync(
                request.StagingDirectory,
                request.CandidateDirectory,
                overwrite: false,
                cancellationToken);

            var ownedPaths = currentManifest.Files
                .Select(static file => UpdatePathSafety.NormalizeRelativePath(file.Path))
                .Where(static path => !UpdateProtocol.IsPreservedInstallationPath(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            ownedPaths.Add(request.ManifestFileName);

            foreach (var sourcePath in UpdatePackageValidator.EnumerateFilesWithoutReparsePoints(
                         request.InstallationDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = UpdatePathSafety.NormalizeRelativePath(
                    Path.GetRelativePath(request.InstallationDirectory, sourcePath));
                if (ownedPaths.Contains(relativePath))
                {
                    continue;
                }

                var destinationPath = UpdatePathSafety.GetSafeChildPath(
                    request.CandidateDirectory,
                    relativePath);
                if (File.Exists(destinationPath))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, overwrite: false);
            }

            await UpdatePackageValidator.ValidateDirectoryAsync(
                request.CandidateDirectory,
                nextManifest,
                allowAdditionalFiles: true,
                cancellationToken);
        }
        catch
        {
            TryDeleteExpectedDirectory(
                request.InstallationDirectory,
                request.CandidateDirectory,
                CandidatePrefix + request.TransactionId);
            throw;
        }
    }

    public static async Task PrepareCandidateFromArchiveAsync(
        UpdateTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        await using var package = new FileStream(
            request.PackagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await PrepareCandidateFromArchiveAsync(request, package, cancellationToken);
    }

    public static async Task PrepareCandidateFromArchiveAsync(
        UpdateTransactionRequest request,
        Stream packageStream,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        ArgumentNullException.ThrowIfNull(packageStream);
        if (!packageStream.CanRead ||
            (packageStream.CanSeek && packageStream.Length != request.PackageSize))
        {
            throw new InvalidDataException("更新包流不可读或大小不匹配");
        }

        EnsureCandidatePathsDoNotExist(request);

        var currentManifest = UpdatePackageValidator.LoadAndValidateManifest(
            Path.Combine(request.InstallationDirectory, request.ManifestFileName),
            request.CurrentVersion);
        var workspaceDirectory = request.CandidateDirectory + ".workspace";
        if (Directory.Exists(workspaceDirectory))
        {
            throw new IOException($"候选工作目录已经存在：{workspaceDirectory}");
        }

        Directory.CreateDirectory(workspaceDirectory);
        var protectedArchive = Path.Combine(workspaceDirectory, "package.zip");
        var payloadDirectory = Path.Combine(workspaceDirectory, "payload");
        try
        {
            await CopyStreamAsync(
                packageStream,
                protectedArchive,
                cancellationToken);
            await UpdatePackageValidator.ValidateArchiveDigestAsync(
                protectedArchive,
                request.PackageDigest,
                cancellationToken);
            var nextManifest = await UpdatePackageValidator.ExtractAndValidateAsync(
                protectedArchive,
                payloadDirectory,
                request.TargetVersion,
                cancellationToken: cancellationToken);
            await PreserveUnknownFilesAsync(
                request.InstallationDirectory,
                payloadDirectory,
                currentManifest,
                cancellationToken);
            await UpdatePackageValidator.ValidateDirectoryAsync(
                payloadDirectory,
                nextManifest,
                allowAdditionalFiles: true,
                cancellationToken);

            File.Delete(protectedArchive);
            Directory.Move(payloadDirectory, request.CandidateDirectory);
            Directory.Delete(workspaceDirectory);
            await UpdatePackageValidator.ValidateDirectoryAsync(
                request.CandidateDirectory,
                nextManifest,
                allowAdditionalFiles: true,
                cancellationToken);
        }
        catch
        {
            TryDeleteExpectedDirectory(
                request.InstallationDirectory,
                request.CandidateDirectory,
                CandidatePrefix + request.TransactionId);
            TryDeleteExpectedDirectory(
                request.InstallationDirectory,
                workspaceDirectory,
                CandidatePrefix + request.TransactionId);
            throw;
        }
    }

    public static void Apply(UpdateTransactionRequest request)
    {
        ValidateRequest(request);
        if (!Directory.Exists(request.CandidateDirectory))
        {
            throw new DirectoryNotFoundException($"候选目录不存在：{request.CandidateDirectory}");
        }

        Directory.Move(request.InstallationDirectory, request.BackupDirectory);
        try
        {
            Directory.Move(request.CandidateDirectory, request.InstallationDirectory);
        }
        catch
        {
            if (!Directory.Exists(request.InstallationDirectory) &&
                Directory.Exists(request.BackupDirectory))
            {
                Directory.Move(request.BackupDirectory, request.InstallationDirectory);
            }

            throw;
        }
    }

    public static async Task<bool> RecoverInterruptedAsync(
        UpdateTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequestShape(request);
        ValidatePostApplyPaths(request);
        if (!Directory.Exists(request.BackupDirectory))
        {
            return false;
        }

        var backupManifest = UpdatePackageValidator.LoadAndValidateManifest(
            Path.Combine(request.BackupDirectory, request.ManifestFileName),
            request.CurrentVersion);
        await UpdatePackageValidator.ValidateInstalledDirectoryAsync(
            request.BackupDirectory,
            backupManifest,
            cancellationToken);

        var failedDirectory = request.CandidateDirectory + ".failed";
        TryDeleteExpectedDirectory(
            request.InstallationDirectory,
            failedDirectory,
            CandidatePrefix + request.TransactionId);
        if (Directory.Exists(request.InstallationDirectory))
        {
            Directory.Move(request.InstallationDirectory, failedDirectory);
        }

        try
        {
            Directory.Move(request.BackupDirectory, request.InstallationDirectory);
        }
        catch
        {
            if (!Directory.Exists(request.InstallationDirectory) &&
                Directory.Exists(failedDirectory))
            {
                Directory.Move(failedDirectory, request.InstallationDirectory);
            }

            throw;
        }

        TryDeleteExpectedDirectory(
            request.InstallationDirectory,
            failedDirectory,
            CandidatePrefix + request.TransactionId);
        TryDeleteExpectedDirectory(
            request.InstallationDirectory,
            request.CandidateDirectory,
            CandidatePrefix + request.TransactionId);
        return true;
    }

    public static void Commit(UpdateTransactionRequest request)
    {
        ValidatePostApplyPaths(request);
        TryDeleteExpectedDirectory(
            request.InstallationDirectory,
            request.BackupDirectory,
            BackupPrefix + request.TransactionId);
        TryDeleteExpectedDirectory(
            request.InstallationDirectory,
            request.CandidateDirectory,
            CandidatePrefix + request.TransactionId);
    }

    public static void Rollback(UpdateTransactionRequest request)
    {
        ValidatePostApplyPaths(request);
        if (!Directory.Exists(request.BackupDirectory))
        {
            throw new DirectoryNotFoundException($"无法回滚，备份目录不存在：{request.BackupDirectory}");
        }

        var failedDirectory = request.CandidateDirectory + ".failed";
        if (Directory.Exists(failedDirectory))
        {
            TryDeleteExpectedDirectory(
                request.InstallationDirectory,
                failedDirectory,
                CandidatePrefix + request.TransactionId);
        }

        if (Directory.Exists(request.InstallationDirectory))
        {
            Directory.Move(request.InstallationDirectory, failedDirectory);
        }

        try
        {
            Directory.Move(request.BackupDirectory, request.InstallationDirectory);
        }
        catch
        {
            if (!Directory.Exists(request.InstallationDirectory) &&
                Directory.Exists(failedDirectory))
            {
                Directory.Move(failedDirectory, request.InstallationDirectory);
            }

            throw;
        }

    }

    public static void CleanupRollbackArtifacts(UpdateTransactionRequest request)
    {
        ValidatePostApplyPaths(request);
        TryDeleteExpectedDirectory(
            request.InstallationDirectory,
            request.CandidateDirectory + ".workspace",
            CandidatePrefix + request.TransactionId);
        TryDeleteExpectedDirectory(
            request.InstallationDirectory,
            request.CandidateDirectory,
            CandidatePrefix + request.TransactionId);
        TryDeleteExpectedDirectory(
            request.InstallationDirectory,
            request.CandidateDirectory + ".failed",
            CandidatePrefix + request.TransactionId);
    }

    private static async Task CopyDirectoryAsync(
        string sourceDirectory,
        string destinationDirectory,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        foreach (var sourcePath in UpdatePackageValidator.EnumerateFilesWithoutReparsePoints(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var destinationPath = UpdatePathSafety.GetSafeChildPath(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destination = new FileStream(
                destinationPath,
                overwrite ? FileMode.Create : FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, 128 * 1024, cancellationToken);
        }
    }

    private static void EnsureCandidatePathsDoNotExist(UpdateTransactionRequest request)
    {
        if (Directory.Exists(request.CandidateDirectory))
        {
            throw new IOException($"候选目录已经存在：{request.CandidateDirectory}");
        }

        if (Directory.Exists(request.BackupDirectory))
        {
            throw new IOException($"备份目录已经存在：{request.BackupDirectory}");
        }
    }

    private static async Task PreserveUnknownFilesAsync(
        string installationDirectory,
        string destinationDirectory,
        UpdatePackageManifest currentManifest,
        CancellationToken cancellationToken)
    {
        var ownedPaths = currentManifest.Files
            .Select(static file => UpdatePathSafety.NormalizeRelativePath(file.Path))
            .Where(static path => !UpdateProtocol.IsPreservedInstallationPath(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ownedPaths.Add(UpdateProtocol.ManifestFileName);

        foreach (var sourcePath in UpdatePackageValidator.EnumerateFilesWithoutReparsePoints(
                     installationDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = UpdatePathSafety.NormalizeRelativePath(
                Path.GetRelativePath(installationDirectory, sourcePath));
            if (ownedPaths.Contains(relativePath))
            {
                continue;
            }

            var destinationPath = UpdatePathSafety.GetSafeChildPath(
                destinationDirectory,
                relativePath);
            if (File.Exists(destinationPath))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await CopyFileAsync(sourcePath, destinationPath, overwrite: false, cancellationToken);
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite,
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
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, 128 * 1024, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static async Task CopyStreamAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (source.CanSeek)
        {
            source.Position = 0;
        }

        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, 128 * 1024, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        if (source.CanSeek)
        {
            source.Position = 0;
        }
    }

    private static void ValidateRequestShape(UpdateTransactionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!StableUpdateVersion.TryParseCanonical(request.CurrentVersion, out var currentVersion) ||
            !StableUpdateVersion.TryParseCanonical(request.TargetVersion, out var targetVersion) ||
            targetVersion.CompareTo(currentVersion) <= 0 ||
            request.SchemaVersion != UpdateProtocol.SchemaVersion ||
            !Guid.TryParseExact(request.TransactionId, "N", out _) ||
            request.ParentProcessId <= 0 ||
            request.ParentProcessStartedAtUtc <= DateTimeOffset.UnixEpoch ||
            !string.Equals(request.EntryExecutable, UpdateProtocol.EntryExecutableName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.ManifestFileName, UpdateProtocol.ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
            !request.PackageDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            !UpdatePackageValidator.IsSha256(request.PackageDigest[7..]) ||
            request.PackageSize <= 0 ||
            request.PackageSize > UpdatePackageValidator.MaximumArchiveBytes)
        {
            throw new InvalidDataException("更新事务请求格式无效");
        }
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

    private static void ValidatePostApplyPaths(UpdateTransactionRequest request)
    {
        var installation = UpdatePathSafety.EnsureNotFileSystemRoot(
            request.InstallationDirectory,
            allowExistingRoot: true);
        UpdatePathSafety.EnsureSiblingDirectory(
            installation,
            request.CandidateDirectory,
            CandidatePrefix + request.TransactionId);
        UpdatePathSafety.EnsureSiblingDirectory(
            installation,
            request.BackupDirectory,
            BackupPrefix + request.TransactionId);
        var parent = Path.GetDirectoryName(installation)
                     ?? throw new InvalidOperationException("无法确定安装目录父目录");
        EnsureExactPath(
            request.CandidateDirectory,
            Path.Combine(parent, CandidatePrefix + request.TransactionId));
        EnsureExactPath(
            request.BackupDirectory,
            Path.Combine(parent, BackupPrefix + request.TransactionId));
    }

    private static void TryDeleteExpectedDirectory(
        string installationDirectory,
        string targetDirectory,
        string expectedPrefix)
    {
        if (!Directory.Exists(targetDirectory))
        {
            return;
        }

        UpdatePathSafety.EnsureSiblingDirectory(
            installationDirectory,
            targetDirectory,
            expectedPrefix);
        Directory.Delete(targetDirectory, recursive: true);
    }
}

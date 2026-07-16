using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Updates;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Updater.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class WindowsPortableUpdateOperationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-operation-tests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(WindowsPortableUpdateOutcome.Canceled, true, false)]
    [InlineData(WindowsPortableUpdateOutcome.Failed, true, true)]
    [InlineData(WindowsPortableUpdateOutcome.Canceled, false, false)]
    public async Task AbortedHandoff_RespectsCleanupSafetyAndUsesOutcomeLogLevel(
        WindowsPortableUpdateOutcome outcome,
        bool canRestoreVerifiedCache,
        bool expectsErrorLog)
    {
        if (!OperatingSystem.IsWindows() ||
            RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            return;
        }

        var archiveSource = CreatePackageArchive("1.0.1");
        var archiveBytes = await File.ReadAllBytesAsync(archiveSource);
        var asset = new ReleaseAssetInfo(
            "IGoLibrary-Ex-v1.0.1-windows-x64.zip",
            new Uri("https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/package.zip"),
            archiveBytes.LongLength,
            "sha256:" + Convert.ToHexString(SHA256.HashData(archiveBytes)),
            "application/zip");
        var release = new ReleaseUpdateInfo(
            new ReleaseVersion(1, 0, 1),
            "v1.0.1",
            "IGoLibrary-Ex v1.0.1",
            "测试更新",
            new Uri("https://github.com/EJianZQ/IGoLibrary/releases/tag/v1.0.1"),
            DateTimeOffset.UtcNow,
            asset);
        var workspaceManager = new WindowsUpdateWorkspaceManager(
            NullLogger<WindowsUpdateWorkspaceManager>.Instance,
            Path.Combine(_root, "updates"),
            TimeProvider.System);
        var packagePreparation = new TestPackagePreparationService(
            workspaceManager,
            archiveSource,
            Path.Combine(_root, "installation"));
        Exception failure = outcome == WindowsPortableUpdateOutcome.Canceled
            ? new OperationCanceledException("用户取消")
            : new IOException("协调器启动失败");
        var handoff = new ArtifactProducingHandoffService(
            outcome,
            canRestoreVerifiedCache,
            failure);
        var logger = new CapturingLogger<WindowsPortableUpdateOperation>();
        using var operation = new WindowsPortableUpdateOperation(
            release,
            new NoBlockingUpdateInstallGuard(),
            new FakeAppVersionProvider(new ReleaseVersion(1, 0, 0)),
            new AppWindowService(),
            packagePreparation,
            handoff,
            workspaceManager,
            logger);

        var result = await operation.RunAsync(
            new SynchronousProgress<WindowsUpdateProgress>(static _ => { }),
            CancellationToken.None);

        Assert.Equal(outcome, result.Outcome);
        var workspace = Assert.IsType<WindowsUpdateWorkspace>(handoff.Workspace);
        var reused = await workspaceManager.TryFindVerifiedAsync(
            asset,
            "1.0.1",
            CancellationToken.None);
        if (canRestoreVerifiedCache)
        {
            Assert.False(File.Exists(Path.Combine(workspace.TransactionDirectory, "request.json")));
            Assert.False(File.Exists(Path.Combine(
                workspace.TransactionDirectory,
                UpdateProtocol.UpdaterExecutableName)));
            Assert.Equal(
                ["package.zip", "staging", "verified-cache.json"],
                Directory.EnumerateFileSystemEntries(workspace.TransactionDirectory)
                    .Select(static entry => Path.GetFileName(entry)!)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
            Assert.NotNull(reused);
            Assert.Equal(workspace.TransactionId, reused.TransactionId);
        }
        else
        {
            Assert.True(File.Exists(Path.Combine(workspace.TransactionDirectory, "request.json")));
            Assert.True(File.Exists(Path.Combine(
                workspace.TransactionDirectory,
                UpdateProtocol.UpdaterExecutableName)));
            Assert.Null(reused);
        }

        Assert.Equal(
            expectsErrorLog,
            logger.Entries.Any(static entry => entry.Level == LogLevel.Error));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string CreatePackageArchive(string version)
    {
        var source = Path.Combine(_root, "package-source");
        UpdatePackageValidatorTests.WritePackage(
            source,
            version,
            new Dictionary<string, string>
            {
                [UpdateProtocol.EntryExecutableName] = "desktop",
                [UpdateProtocol.UpdaterExecutableName] = "updater",
                ["feature.dll"] = "feature",
                [UpdateProtocol.ManagedCloudflaredExecutablePath] = "release-cloudflared",
                [UpdateProtocol.ManagedCloudflaredLicensePath] = "release-license",
                [UpdateProtocol.ManagedCloudflaredNoticesPath] = "release-notices"
            });
        var archive = Path.Combine(_root, "package.zip");
        ZipFile.CreateFromDirectory(source, archive);
        return archive;
    }

    private sealed class NoBlockingUpdateInstallGuard : IUpdateInstallGuard
    {
        public IReadOnlyList<string> GetBlockingTaskNames() => [];
    }

    private sealed class TestPackagePreparationService(
        WindowsUpdateWorkspaceManager workspaceManager,
        string archiveSource,
        string installationDirectory) : IWindowsUpdatePackagePreparationService
    {
        public string ValidateInstallationDirectory(string currentVersion)
        {
            Directory.CreateDirectory(installationDirectory);
            return installationDirectory;
        }

        public async Task<PreparedWindowsUpdatePackage> PrepareAsync(
            WindowsUpdateWorkspace workspace,
            string validatedInstallationDirectory,
            ReleaseAssetInfo asset,
            string currentVersion,
            string targetVersion,
            ReleaseAssetDownloadPauseController transferController,
            IProgress<WindowsUpdateProgress> progress,
            CancellationToken cancellationToken)
        {
            File.Copy(archiveSource, workspace.ArchivePath);
            await UpdatePackageValidator.ExtractAndValidateAsync(
                workspace.ArchivePath,
                workspace.StagingDirectory,
                targetVersion,
                cancellationToken: cancellationToken);
            workspaceManager.WriteVerifiedMarker(workspace, targetVersion, asset);
            return new PreparedWindowsUpdatePackage(
                workspace,
                validatedInstallationDirectory,
                asset,
                currentVersion,
                targetVersion);
        }
    }

    private sealed class ArtifactProducingHandoffService(
        WindowsPortableUpdateOutcome outcome,
        bool canRestoreVerifiedCache,
        Exception failure) : IWindowsUpdateHandoffService
    {
        public WindowsUpdateWorkspace? Workspace { get; private set; }

        public Task<WindowsUpdateHandoffResult> ExecuteAsync(
            PreparedWindowsUpdatePackage package,
            IProgress<WindowsUpdateProgress> progress,
            CancellationToken cancellationToken)
        {
            Workspace = package.Workspace;
            File.WriteAllText(
                Path.Combine(
                    package.Workspace.TransactionDirectory,
                    UpdateProtocol.UpdaterExecutableName),
                "copied updater");
            File.WriteAllText(
                Path.Combine(package.Workspace.TransactionDirectory, "request.json"),
                "handoff request");
            File.WriteAllText(
                Path.Combine(package.Workspace.TransactionDirectory, "heartbeat.txt"),
                "heartbeat");
            return Task.FromResult(new WindowsUpdateHandoffResult(
                outcome,
                failure.Message,
                CanRestoreVerifiedCache: canRestoreVerifiedCache,
                failure));
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<CapturedLogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record CapturedLogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

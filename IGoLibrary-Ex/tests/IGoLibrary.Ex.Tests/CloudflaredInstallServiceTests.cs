using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class CloudflaredInstallServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-cloudflared-install-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallAsync_DownloadsValidBinaryAndPublishesCompleteManagedDirectory()
    {
        var executable = "test-cloudflared-binary"u8.ToArray();
        var context = CreateContext(executable, new WritingDownloader(executable));
        await context.Service.StartAsync(CancellationToken.None);

        await context.Service.InstallAsync();

        var availability = await context.Locator.FindAsync();
        Assert.True(availability.IsAvailable);
        Assert.Equal(CloudflaredToolSource.UserInstalled, availability.Source);
        Assert.Equal(executable, await File.ReadAllBytesAsync(availability.ExecutablePath!));
        var directory = Path.GetDirectoryName(availability.ExecutablePath!);
        Assert.True(File.Exists(Path.Combine(directory!, "LICENSE.txt")));
        Assert.True(File.Exists(Path.Combine(directory!, "THIRD-PARTY-NOTICES.txt")));
        Assert.Empty(Directory.EnumerateFiles(context.Paths.DownloadWorkspaceRoot, "*", SearchOption.AllDirectories));

        await context.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InstallAsync_ConcurrentCallsDownloadOnlyOnce()
    {
        var executable = "concurrent-cloudflared-binary"u8.ToArray();
        var downloader = new CountingDownloader(executable);
        var context = CreateContext(executable, downloader);
        await context.Service.StartAsync(CancellationToken.None);

        await Task.WhenAll(context.Service.InstallAsync(), context.Service.InstallAsync());

        Assert.Equal(1, downloader.CallCount);
        Assert.True((await context.Locator.FindAsync()).IsAvailable);
        await context.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InstallAsync_PauseAndResumeControlsActiveDownload()
    {
        var executable = "pause-resume-cloudflared-binary"u8.ToArray();
        var downloader = new PausableDownloader(executable);
        var context = CreateContext(executable, downloader);
        await context.Service.StartAsync(CancellationToken.None);
        var install = context.Service.InstallAsync(new InlineProgress<CloudflaredInstallProgress>(_ => { }));
        await downloader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(context.Service.TryPause());
        await downloader.Paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(context.Service.TryResume());

        await install.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(downloader.Resumed.Task.IsCompletedSuccessfully);
        Assert.True((await context.Locator.FindAsync()).IsAvailable);
        await context.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InstallAsync_AutomaticRetriesExhaustedWaitsForManualResume()
    {
        var executable = "manual-resume-cloudflared-binary"u8.ToArray();
        var downloader = new InterruptedThenWritingDownloader(executable, preservedBytes: 7);
        var context = CreateContext(executable, downloader);
        await context.Service.StartAsync(CancellationToken.None);
        var awaitingResume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new InlineProgress<CloudflaredInstallProgress>(value =>
        {
            if (value.Stage == CloudflaredInstallStage.Paused && value.CanResume)
            {
                awaitingResume.TrySetResult();
            }
        });

        var install = context.Service.InstallAsync(progress);
        await awaitingResume.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(context.Service.TryResume());
        await install.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, downloader.CallCount);
        Assert.True((await context.Locator.FindAsync()).IsAvailable);
        await context.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InstallAsync_UserCancellationPreservesPartialUntilServiceStops()
    {
        var executable = "cancel-cloudflared-binary"u8.ToArray();
        var downloader = new CancelablePartialDownloader(executable[..7]);
        var context = CreateContext(executable, downloader);
        await context.Service.StartAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var install = context.Service.InstallAsync(cancellationToken: cancellation.Token);
        await downloader.PartialWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => install);
        var partial = Assert.Single(Directory.EnumerateFiles(
            context.Paths.DownloadWorkspaceRoot,
            "*.partial",
            SearchOption.AllDirectories));
        Assert.Equal(7, new FileInfo(partial).Length);

        await context.Service.StopAsync(CancellationToken.None);
        Assert.Empty(Directory.EnumerateFileSystemEntries(context.Paths.DownloadWorkspaceRoot));
    }

    [Fact]
    public async Task InstallAsync_TerminalFailureCleansPartialImmediately()
    {
        var executable = "failed-cloudflared-binary"u8.ToArray();
        var context = CreateContext(executable, new FailingDownloader(executable[..5]));
        await context.Service.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => context.Service.InstallAsync());

        Assert.Empty(Directory.EnumerateFiles(
            context.Paths.DownloadWorkspaceRoot,
            "*",
            SearchOption.AllDirectories));
        await context.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_RemovesPreviousProcessWorkspace()
    {
        var executable = "cleanup-cloudflared-binary"u8.ToArray();
        var context = CreateContext(executable, new WritingDownloader(executable));
        var stale = Path.Combine(context.Paths.DownloadWorkspaceRoot, "stale-process");
        Directory.CreateDirectory(stale);
        await File.WriteAllTextAsync(Path.Combine(stale, "asset.partial"), "stale");

        await context.Service.StartAsync(CancellationToken.None);

        Assert.False(Directory.Exists(stale));
        await context.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_RemovesBlockingCurrentRidFileAndAllowsRepair()
    {
        var executable = "blocking-file-repair-cloudflared"u8.ToArray();
        var context = CreateContext(executable, new WritingDownloader(executable));
        var finalDirectory = context.Paths.GetManagedInstallDirectory(context.Catalog.Current);
        Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);
        await File.WriteAllTextAsync(finalDirectory, "blocking-file");

        await context.Service.StartAsync(CancellationToken.None);
        await context.Service.InstallAsync();

        var availability = await context.Locator.FindAsync();
        Assert.True(availability.IsAvailable);
        Assert.Equal(executable, await File.ReadAllBytesAsync(availability.ExecutablePath!));
        await context.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_RemovesCurrentRidLinkWithoutFollowingTargetAndAllowsRepair()
    {
        var executable = "linked-rid-repair-cloudflared"u8.ToArray();
        var context = CreateContext(executable, new WritingDownloader(executable));
        var finalDirectory = context.Paths.GetManagedInstallDirectory(context.Catalog.Current);
        var outsideDirectory = Path.Combine(_root, "outside-current-rid-link");
        var marker = Path.Combine(outsideDirectory, "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);
        Directory.CreateDirectory(outsideDirectory);
        await File.WriteAllTextAsync(marker, "keep");
        try
        {
            Directory.CreateSymbolicLink(finalDirectory, outsideDirectory);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            return;
        }

        await context.Service.StartAsync(CancellationToken.None);
        await context.Service.InstallAsync();

        Assert.Equal("keep", await File.ReadAllTextAsync(marker));
        Assert.True((await context.Locator.FindAsync()).IsAvailable);
        await context.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_RestoresValidTransactionBackupBeforeCleaningArtifacts()
    {
        var executable = "startup-backup-recovery-cloudflared"u8.ToArray();
        var context = CreateContext(executable, new WritingDownloader(executable));
        var finalDirectory = context.Paths.GetManagedInstallDirectory(context.Catalog.Current);
        var backupDirectory = finalDirectory + ".backup-test";
        Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);
        await File.WriteAllTextAsync(finalDirectory, "blocking-file");
        await WriteValidInstallAsync(backupDirectory, context.Catalog, executable);

        await context.Service.StartAsync(CancellationToken.None);

        var availability = await context.Locator.FindAsync();
        Assert.True(availability.IsAvailable);
        Assert.Equal(executable, await File.ReadAllBytesAsync(availability.ExecutablePath!));
        Assert.False(CloudflaredFileSystemSafety.EntryExists(backupDirectory));
        await context.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Locator_InvalidatesCachedResultWhenExecutableChanges()
    {
        var executable = "locator-cloudflared-binary"u8.ToArray();
        var context = CreateContext(executable, new WritingDownloader(executable));
        await context.Service.StartAsync(CancellationToken.None);
        await context.Service.InstallAsync();
        var first = await context.Locator.FindAsync();
        Assert.True(first.IsAvailable);

        await File.WriteAllTextAsync(first.ExecutablePath!, "tampered-and-longer");
        File.SetLastWriteTimeUtc(first.ExecutablePath!, DateTime.UtcNow.AddMinutes(1));

        var second = await context.Locator.FindAsync();
        Assert.False(second.IsAvailable);
        await context.Service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Locator_InvalidatesCachedResultWhenUnixExecuteModeChanges()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var executable = "locator-unix-mode-cloudflared"u8.ToArray();
        var context = CreateContext(executable, new WritingDownloader(executable));
        var managedDirectory = context.Paths.GetManagedInstallDirectory(context.Catalog.Current);
        await WriteValidInstallAsync(managedDirectory, context.Catalog, executable);
        var first = await context.Locator.FindAsync();
        Assert.True(first.IsAvailable);

        File.SetUnixFileMode(
            first.ExecutablePath!,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var second = await context.Locator.FindAsync();
        Assert.False(second.IsAvailable);
    }

    [Fact]
    public async Task Locator_PrefersValidBundledInstallAndRejectsIncompleteManagedInstall()
    {
        var executable = "locator-priority-cloudflared"u8.ToArray();
        var context = CreateContext(executable, new WritingDownloader(executable));
        var managedDirectory = context.Paths.GetManagedInstallDirectory(context.Catalog.Current);
        Directory.CreateDirectory(managedDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(managedDirectory, context.Catalog.Current.ExecutableName),
            executable);
        await File.WriteAllBytesAsync(
            Path.Combine(managedDirectory, "LICENSE.txt"),
            context.Catalog.LicenseBytes);

        Assert.False((await context.Locator.FindAsync()).IsAvailable);

        await WriteValidInstallAsync(context.Paths.BundledDirectory, context.Catalog, executable);
        await WriteValidInstallAsync(managedDirectory, context.Catalog, executable);
        context.Locator.Invalidate();

        var availability = await context.Locator.FindAsync();
        Assert.True(availability.IsAvailable);
        Assert.Equal(CloudflaredToolSource.Bundled, availability.Source);
    }

    [Fact]
    public async Task Locator_RejectsManagedInstallReachedThroughRootLink()
    {
        var executable = "linked-root-cloudflared"u8.ToArray();
        var context = CreateContext(executable, new WritingDownloader(executable));
        var outsideRoot = Path.Combine(_root, "outside-managed-root");
        var outsideInstall = Path.Combine(
            outsideRoot,
            context.Catalog.Current.Version,
            context.Catalog.Current.RuntimeIdentifier);
        await WriteValidInstallAsync(outsideInstall, context.Catalog, executable);
        Directory.CreateDirectory(Path.GetDirectoryName(context.Paths.ManagedInstallRoot)!);
        try
        {
            Directory.CreateSymbolicLink(context.Paths.ManagedInstallRoot, outsideRoot);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            return;
        }

        try
        {
            Assert.False((await context.Locator.FindAsync()).IsAvailable);
        }
        finally
        {
            Directory.Delete(context.Paths.ManagedInstallRoot);
        }

        Assert.True(File.Exists(Path.Combine(outsideInstall, context.Catalog.Current.ExecutableName)));
    }

    [Fact]
    public async Task ManagedInstaller_PostCommitValidationFailureRestoresExistingDirectory()
    {
        var executable = "rollback-cloudflared-binary"u8.ToArray();
        Directory.CreateDirectory(_root);
        var catalog = new CloudflaredAssetCatalog(
            BuildManifest(executable),
            "win-x64",
            NullLogger<CloudflaredAssetCatalog>.Instance);
        var paths = new CloudflaredPathProvider(
            Path.Combine(_root, "bundled"),
            Path.Combine(_root, "managed"),
            Path.Combine(_root, "downloads"));
        var finalDirectory = paths.GetManagedInstallDirectory(catalog.Current);
        Directory.CreateDirectory(finalDirectory);
        var sentinel = Path.Combine(finalDirectory, "existing-install.txt");
        await File.WriteAllTextAsync(sentinel, "keep-me");
        var payload = Path.Combine(_root, "payload.exe");
        await File.WriteAllBytesAsync(payload, executable);
        var installer = new CloudflaredManagedInstaller(
            catalog,
            paths,
            new PostCommitRejectingLocator(finalDirectory),
            new CloudflaredExtractor(NullLogger<CloudflaredExtractor>.Instance),
            NullLogger<CloudflaredManagedInstaller>.Instance);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync(payload, progress: null, CancellationToken.None));

        Assert.Equal("keep-me", await File.ReadAllTextAsync(sentinel));
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(Path.GetDirectoryName(finalDirectory)!),
            directory => directory.Contains(".staging-", StringComparison.Ordinal) ||
                         directory.Contains(".backup-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ManagedInstaller_RollbackRemovesUnexpectedBlockingFileBeforeRestoringBackup()
    {
        var executable = "rollback-blocking-file-cloudflared"u8.ToArray();
        Directory.CreateDirectory(_root);
        var catalog = new CloudflaredAssetCatalog(
            BuildManifest(executable),
            "win-x64",
            NullLogger<CloudflaredAssetCatalog>.Instance);
        var paths = new CloudflaredPathProvider(
            Path.Combine(_root, "bundled"),
            Path.Combine(_root, "managed"),
            Path.Combine(_root, "downloads"));
        var finalDirectory = paths.GetManagedInstallDirectory(catalog.Current);
        Directory.CreateDirectory(finalDirectory);
        var sentinel = Path.Combine(finalDirectory, "existing-install.txt");
        await File.WriteAllTextAsync(sentinel, "keep-me");
        var payload = Path.Combine(_root, "blocking-file-payload.exe");
        await File.WriteAllBytesAsync(payload, executable);
        var installer = new CloudflaredManagedInstaller(
            catalog,
            paths,
            new PostCommitReplacingWithFileLocator(finalDirectory),
            new CloudflaredExtractor(NullLogger<CloudflaredExtractor>.Instance),
            NullLogger<CloudflaredManagedInstaller>.Instance);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync(payload, progress: null, CancellationToken.None));

        Assert.Equal("keep-me", await File.ReadAllTextAsync(sentinel));
        Assert.DoesNotContain(
            Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(finalDirectory)!),
            entry => entry.Contains(".backup-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ManagedInstaller_RollbackCleanupFailurePreservesRecoverableBackup()
    {
        var executable = "rollback-preserved-backup-cloudflared"u8.ToArray();
        Directory.CreateDirectory(_root);
        var catalog = new CloudflaredAssetCatalog(
            BuildManifest(executable),
            "win-x64",
            NullLogger<CloudflaredAssetCatalog>.Instance);
        var paths = new CloudflaredPathProvider(
            Path.Combine(_root, "bundled"),
            Path.Combine(_root, "managed"),
            Path.Combine(_root, "downloads"));
        var finalDirectory = paths.GetManagedInstallDirectory(catalog.Current);
        await WriteValidInstallAsync(finalDirectory, catalog, executable);
        var sentinel = Path.Combine(finalDirectory, "existing-install.txt");
        await File.WriteAllTextAsync(sentinel, "keep-me");
        var payload = Path.Combine(_root, "preserved-backup-payload.exe");
        await File.WriteAllBytesAsync(payload, executable);
        var installer = new CloudflaredManagedInstaller(
            catalog,
            paths,
            new PostCommitRejectingLocator(finalDirectory),
            new CloudflaredExtractor(NullLogger<CloudflaredExtractor>.Instance),
            NullLogger<CloudflaredManagedInstaller>.Instance,
            (root, target) =>
            {
                if (string.Equals(
                        Path.GetFullPath(target),
                        Path.GetFullPath(finalDirectory),
                        OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                {
                    throw new IOException("simulated rollback cleanup failure");
                }

                CloudflaredFileSystemSafety.DeleteEntrySafely(root, target);
            });

        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            installer.InstallAsync(payload, progress: null, CancellationToken.None));

        Assert.Equal(2, exception.InnerExceptions.Count);
        var backupDirectory = Assert.Single(Directory.EnumerateDirectories(
            Path.GetDirectoryName(finalDirectory)!,
            "win-x64.backup-*"));
        Assert.Equal("keep-me", await File.ReadAllTextAsync(Path.Combine(
            backupDirectory,
            "existing-install.txt")));
        Assert.True(Directory.Exists(finalDirectory));
    }

    [Fact]
    public async Task Extractor_TgzAcceptsSingleCloudflaredFileAndRejectsExtraEntry()
    {
        var extractor = new CloudflaredExtractor(NullLogger<CloudflaredExtractor>.Instance);
        var executable = "mac-cloudflared"u8.ToArray();
        Directory.CreateDirectory(_root);
        var validArchive = Path.Combine(_root, "valid.tgz");
        await WriteTgzAsync(validArchive, ("cloudflared", executable));
        var destination = Path.Combine(_root, "cloudflared");
        var asset = Descriptor(executable, "tgz", validArchive);

        await extractor.PrepareExecutableAsync(asset, validArchive, destination);

        Assert.Equal(executable, await File.ReadAllBytesAsync(destination));
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(destination);
            Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
            Assert.True(mode.HasFlag(UnixFileMode.GroupExecute));
            Assert.True(mode.HasFlag(UnixFileMode.OtherExecute));
        }

        var invalidArchive = Path.Combine(_root, "invalid.tgz");
        await WriteTgzAsync(
            invalidArchive,
            ("cloudflared", executable),
            ("unexpected", "extra"u8.ToArray()));
        await Assert.ThrowsAsync<InvalidDataException>(() => extractor.PrepareExecutableAsync(
            asset,
            invalidArchive,
            Path.Combine(_root, "cloudflared-invalid")));
    }

    [Fact]
    public async Task Extractor_TgzRejectsWrongExecutableSizeBeforeCreatingDestination()
    {
        var extractor = new CloudflaredExtractor(NullLogger<CloudflaredExtractor>.Instance);
        var expectedExecutable = "expected-size"u8.ToArray();
        var actualExecutable = "unexpected-larger-executable"u8.ToArray();
        Directory.CreateDirectory(_root);
        var archive = Path.Combine(_root, "wrong-size.tgz");
        await WriteTgzAsync(archive, ("cloudflared", actualExecutable));
        var destination = Path.Combine(_root, "wrong-size-output");

        await Assert.ThrowsAsync<InvalidDataException>(() => extractor.PrepareExecutableAsync(
            Descriptor(expectedExecutable, "tgz", archive),
            archive,
            destination));

        Assert.False(File.Exists(destination));
    }

    [Theory]
    [InlineData("../cloudflared")]
    [InlineData("/cloudflared")]
    [InlineData("not-cloudflared")]
    public async Task Extractor_TgzRejectsUnsafeOrUnexpectedEntryName(string entryName)
    {
        var extractor = new CloudflaredExtractor(NullLogger<CloudflaredExtractor>.Instance);
        var executable = "unsafe-entry"u8.ToArray();
        Directory.CreateDirectory(_root);
        var archive = Path.Combine(_root, $"unsafe-{Guid.NewGuid():N}.tgz");
        await WriteTgzAsync(archive, (entryName, executable));

        await Assert.ThrowsAsync<InvalidDataException>(() => extractor.PrepareExecutableAsync(
            Descriptor(executable, "tgz", archive),
            archive,
            Path.Combine(_root, $"unsafe-output-{Guid.NewGuid():N}")));
    }

    [Fact]
    public async Task Extractor_TgzRejectsSymbolicLinkEntry()
    {
        var extractor = new CloudflaredExtractor(NullLogger<CloudflaredExtractor>.Instance);
        var executable = "link-entry"u8.ToArray();
        Directory.CreateDirectory(_root);
        var archive = Path.Combine(_root, "link.tgz");
        await using (var file = File.Create(archive))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize, leaveOpen: false))
        using (var writer = new TarWriter(gzip, leaveOpen: false))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "cloudflared")
            {
                LinkName = "elsewhere"
            });
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => extractor.PrepareExecutableAsync(
            Descriptor(executable, "tgz", archive),
            archive,
            Path.Combine(_root, "link-output")));
    }

    [Fact]
    public void FileSystemSafety_DeleteDirectorySafelyDoesNotFollowTargetLink()
    {
        var controlledRoot = Path.Combine(_root, "controlled");
        var outside = Path.Combine(_root, "outside");
        var marker = Path.Combine(outside, "keep.txt");
        Directory.CreateDirectory(controlledRoot);
        Directory.CreateDirectory(outside);
        File.WriteAllText(marker, "keep");
        var link = Path.Combine(controlledRoot, "linked-workspace");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            return;
        }

        CloudflaredFileSystemSafety.DeleteDirectorySafely(controlledRoot, link);

        Assert.False(Directory.Exists(link));
        Assert.Equal("keep", File.ReadAllText(marker));
    }

    [Fact]
    public void FileSystemSafety_DeleteDirectorySafelyRemovesDanglingLink()
    {
        var controlledRoot = Path.Combine(_root, "controlled-dangling");
        var missingTarget = Path.Combine(_root, "missing-link-target");
        var link = Path.Combine(controlledRoot, "dangling-workspace");
        Directory.CreateDirectory(controlledRoot);
        try
        {
            Directory.CreateSymbolicLink(link, missingTarget);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            return;
        }

        Assert.True(CloudflaredFileSystemSafety.EntryExists(link));

        CloudflaredFileSystemSafety.DeleteDirectorySafely(controlledRoot, link);

        Assert.False(CloudflaredFileSystemSafety.EntryExists(link));
    }

    [Fact]
    public void DownloadWorkspace_RetriesFailedCleanupBeforeProcessExit()
    {
        Directory.CreateDirectory(_root);
        var paths = new CloudflaredPathProvider(
            Path.Combine(_root, "bundled-workspace-retry"),
            Path.Combine(_root, "managed-workspace-retry"),
            Path.Combine(_root, "downloads-workspace-retry"));
        string? firstWorkspace = null;
        var firstFailureInjected = false;
        var workspace = new CloudflaredDownloadWorkspace(
            paths,
            NullLogger<CloudflaredDownloadWorkspace>.Instance,
            (root, target) =>
            {
                if (!firstFailureInjected && string.Equals(
                        target,
                        firstWorkspace,
                        OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                {
                    firstFailureInjected = true;
                    throw new IOException("simulated cleanup failure");
                }

                CloudflaredFileSystemSafety.DeleteEntrySafely(root, target);
            });
        workspace.Initialize();
        firstWorkspace = workspace.CurrentDirectory;
        File.WriteAllText(Path.Combine(firstWorkspace, "asset.partial"), "partial");

        workspace.CleanupAndRenew("terminal result");
        var renewedWorkspace = workspace.CurrentDirectory;

        Assert.True(CloudflaredFileSystemSafety.EntryExists(firstWorkspace));
        workspace.Cleanup("process exit");
        Assert.False(CloudflaredFileSystemSafety.EntryExists(firstWorkspace));
        Assert.False(CloudflaredFileSystemSafety.EntryExists(renewedWorkspace));
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

    private TestContext CreateContext(byte[] executable, IReleaseAssetDownloader downloader)
    {
        Directory.CreateDirectory(_root);
        var catalog = new CloudflaredAssetCatalog(
            BuildManifest(executable),
            "win-x64",
            NullLogger<CloudflaredAssetCatalog>.Instance);
        var paths = new CloudflaredPathProvider(
            Path.Combine(_root, "bundled"),
            Path.Combine(_root, "managed"),
            Path.Combine(_root, "downloads"));
        var locator = new CloudflaredToolLocator(
            catalog,
            paths,
            NullLogger<CloudflaredToolLocator>.Instance);
        var extractor = new CloudflaredExtractor(NullLogger<CloudflaredExtractor>.Instance);
        var workspace = new CloudflaredDownloadWorkspace(
            paths,
            NullLogger<CloudflaredDownloadWorkspace>.Instance);
        var managedInstaller = new CloudflaredManagedInstaller(
            catalog,
            paths,
            locator,
            extractor,
            NullLogger<CloudflaredManagedInstaller>.Instance);
        var service = new CloudflaredInstallService(
            catalog,
            paths,
            locator,
            workspace,
            managedInstaller,
            downloader,
            new ActivityLogService(),
            NullLogger<CloudflaredInstallService>.Instance);
        return new TestContext(service, locator, paths, catalog);
    }

    private static async Task WriteValidInstallAsync(
        string directory,
        CloudflaredAssetCatalog catalog,
        byte[] executable)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(
            Path.Combine(directory, catalog.Current.ExecutableName),
            executable);
        await File.WriteAllBytesAsync(Path.Combine(directory, "LICENSE.txt"), catalog.LicenseBytes);
        await File.WriteAllBytesAsync(
            Path.Combine(directory, "THIRD-PARTY-NOTICES.txt"),
            catalog.NoticesBytes);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                Path.Combine(directory, catalog.Current.ExecutableName),
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute |
                UnixFileMode.GroupRead |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead |
                UnixFileMode.OtherExecute);
        }
    }

    private static string BuildManifest(byte[] executable)
    {
        var hash = Convert.ToHexString(SHA256.HashData(executable)).ToLowerInvariant();
        return $$"""
                 {
                   "version": "2026.7.0",
                   "assets": {
                     "win-x64": {
                       "fileName": "cloudflared-windows-amd64.exe",
                       "size": {{executable.Length}},
                       "sha256": "{{hash}}",
                       "executableSize": {{executable.Length}},
                       "executableSha256": "{{hash}}",
                       "archiveType": "binary"
                     }
                   }
                 }
                 """;
    }

    private static CloudflaredAssetDescriptor Descriptor(
        byte[] executable,
        string archiveType,
        string archivePath)
    {
        var executableHash = Convert.ToHexString(SHA256.HashData(executable)).ToLowerInvariant();
        var archiveBytes = File.ReadAllBytes(archivePath);
        return new CloudflaredAssetDescriptor(
            "2026.7.0",
            "osx-x64",
            Path.GetFileName(archivePath),
            archiveType,
            archiveBytes.Length,
            Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant(),
            "cloudflared",
            executable.Length,
            executableHash,
            new Uri("https://github.com/cloudflare/cloudflared/releases/download/2026.7.0/test.tgz"));
    }

    private static async Task WriteTgzAsync(
        string path,
        params (string Name, byte[] Content)[] entries)
    {
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize, leaveOpen: false);
        using var writer = new TarWriter(gzip, leaveOpen: false);
        foreach (var item in entries)
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, item.Name)
            {
                DataStream = new MemoryStream(item.Content, writable: false)
            };
            writer.WriteEntry(entry);
        }
    }

    private sealed record TestContext(
        CloudflaredInstallService Service,
        CloudflaredToolLocator Locator,
        CloudflaredPathProvider Paths,
        CloudflaredAssetCatalog Catalog);

    private sealed class PostCommitRejectingLocator(string finalDirectory) : ICloudflaredToolLocator
    {
        public Task<CloudflaredToolAvailability> FindAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> ValidateDirectoryAsync(
            string directory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(!string.Equals(
                Path.GetFullPath(directory),
                Path.GetFullPath(finalDirectory),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

        public void Invalidate()
        {
        }
    }

    private sealed class PostCommitReplacingWithFileLocator(string finalDirectory) : ICloudflaredToolLocator
    {
        public Task<CloudflaredToolAvailability> FindAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> ValidateDirectoryAsync(
            string directory,
            CancellationToken cancellationToken = default)
        {
            if (!string.Equals(
                    Path.GetFullPath(directory),
                    Path.GetFullPath(finalDirectory),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return Task.FromResult(true);
            }

            Directory.Delete(finalDirectory, recursive: true);
            File.WriteAllText(finalDirectory, "unexpected-blocking-file");
            return Task.FromResult(false);
        }

        public void Invalidate()
        {
        }
    }

    private sealed class WritingDownloader(byte[] bytes) : IReleaseAssetDownloader
    {
        public Task DownloadAsync(
            ReleaseAssetInfo asset,
            string destinationPath,
            IProgress<ReleaseAssetDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            IReleaseAssetDownloadPauseSource? pauseSource = null,
            ReleaseAssetPartialRetentionPolicy partialRetentionPolicy =
                ReleaseAssetPartialRetentionPolicy.DeleteOnCancellation)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllBytes(destinationPath, bytes);
            progress?.Report(new ReleaseAssetDownloadProgress(bytes.Length, bytes.Length));
            return Task.CompletedTask;
        }
    }

    private sealed class CountingDownloader(byte[] bytes) : IReleaseAssetDownloader
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public async Task DownloadAsync(
            ReleaseAssetInfo asset,
            string destinationPath,
            IProgress<ReleaseAssetDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            IReleaseAssetDownloadPauseSource? pauseSource = null,
            ReleaseAssetPartialRetentionPolicy partialRetentionPolicy =
                ReleaseAssetPartialRetentionPolicy.DeleteOnCancellation)
        {
            Interlocked.Increment(ref _callCount);
            await Task.Delay(50, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken);
        }
    }

    private sealed class FailingDownloader(byte[] partial) : IReleaseAssetDownloader
    {
        public Task DownloadAsync(
            ReleaseAssetInfo asset,
            string destinationPath,
            IProgress<ReleaseAssetDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            IReleaseAssetDownloadPauseSource? pauseSource = null,
            ReleaseAssetPartialRetentionPolicy partialRetentionPolicy =
                ReleaseAssetPartialRetentionPolicy.DeleteOnCancellation)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllBytes(destinationPath + ".partial", partial);
            return Task.FromException(new IOException("terminal download failure"));
        }
    }

    private sealed class CancelablePartialDownloader(byte[] partial) : IReleaseAssetDownloader
    {
        public TaskCompletionSource PartialWritten { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DownloadAsync(
            ReleaseAssetInfo asset,
            string destinationPath,
            IProgress<ReleaseAssetDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            IReleaseAssetDownloadPauseSource? pauseSource = null,
            ReleaseAssetPartialRetentionPolicy partialRetentionPolicy =
                ReleaseAssetPartialRetentionPolicy.DeleteOnCancellation)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath + ".partial", partial, cancellationToken);
            PartialWritten.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class PausableDownloader(byte[] bytes) : IReleaseAssetDownloader
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Paused { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Resumed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DownloadAsync(
            ReleaseAssetInfo asset,
            string destinationPath,
            IProgress<ReleaseAssetDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            IReleaseAssetDownloadPauseSource? pauseSource = null,
            ReleaseAssetPartialRetentionPolicy partialRetentionPolicy =
                ReleaseAssetPartialRetentionPolicy.DeleteOnCancellation)
        {
            Assert.NotNull(pauseSource);
            progress?.Report(new ReleaseAssetDownloadProgress(
                1,
                bytes.Length,
                ReleaseAssetDownloadState.Downloading));
            Started.TrySetResult();
            var pauseToken = pauseSource.PauseToken;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken,
                       pauseToken))
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
                }
                catch (OperationCanceledException) when (pauseToken.IsCancellationRequested)
                {
                }
            }

            progress?.Report(new ReleaseAssetDownloadProgress(
                1,
                bytes.Length,
                ReleaseAssetDownloadState.Paused));
            Paused.TrySetResult();
            await pauseSource.WaitWhilePausedAsync(cancellationToken);
            Resumed.TrySetResult();
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken);
        }
    }

    private sealed class InterruptedThenWritingDownloader(byte[] bytes, long preservedBytes) :
        IReleaseAssetDownloader
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public async Task DownloadAsync(
            ReleaseAssetInfo asset,
            string destinationPath,
            IProgress<ReleaseAssetDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default,
            IReleaseAssetDownloadPauseSource? pauseSource = null,
            ReleaseAssetPartialRetentionPolicy partialRetentionPolicy =
                ReleaseAssetPartialRetentionPolicy.DeleteOnCancellation)
        {
            var call = Interlocked.Increment(ref _callCount);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            if (call == 1)
            {
                await File.WriteAllBytesAsync(
                    destinationPath + ".partial",
                    bytes[..checked((int)preservedBytes)],
                    cancellationToken);
                throw new ReleaseAssetDownloadInterruptedException(
                    "simulated automatic retry exhaustion",
                    preservedBytes);
            }

            await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

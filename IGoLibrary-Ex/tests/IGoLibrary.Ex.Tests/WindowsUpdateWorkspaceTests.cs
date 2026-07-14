using System.IO.Compression;
using System.Security.Cryptography;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Updater.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class WindowsUpdateWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-workspace-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TryFindVerifiedAsync_RevalidatesAndReusesCompleteCache()
    {
        var (manager, workspace, asset) = await CreateVerifiedWorkspaceAsync();

        var reused = await manager.TryFindVerifiedAsync(asset, "1.0.1", CancellationToken.None);

        Assert.NotNull(reused);
        Assert.Equal(workspace.TransactionId, reused.TransactionId);
        Assert.True(reused.IsVerifiedCache);
        Assert.True(File.Exists(Path.Combine(
            reused.TransactionDirectory,
            "verified-cache.json")));
    }

    [Fact]
    public async Task TryFindVerifiedAsync_WhenStagingWasModified_DeletesCache()
    {
        var (manager, workspace, asset) = await CreateVerifiedWorkspaceAsync();
        await File.AppendAllTextAsync(
            Path.Combine(workspace.StagingDirectory, "feature.dll"),
            "tampered");

        var reused = await manager.TryFindVerifiedAsync(asset, "1.0.1", CancellationToken.None);

        Assert.Null(reused);
        Assert.False(Directory.Exists(workspace.TransactionDirectory));
    }

    [Fact]
    public void ShouldDeleteWorkspace_PreservesVerifiedPackageBeforeHandoff()
    {
        var workspace = new WindowsUpdateWorkspace(
            Guid.NewGuid().ToString("N"),
            Path.Combine(_root, Guid.NewGuid().ToString("N")),
            isVerifiedCache: false);

        Assert.True(WindowsPortableUpdateOperation.ShouldDeleteWorkspace(
            preserveWorkspace: false,
            workspace));

        workspace.MarkVerified();

        Assert.False(WindowsPortableUpdateOperation.ShouldDeleteWorkspace(
            preserveWorkspace: false,
            workspace));
        Assert.False(WindowsPortableUpdateOperation.ShouldDeleteWorkspace(
            preserveWorkspace: true,
            workspace));
    }

    [Fact]
    public void TryDelete_RejectsDirectoryOutsideConfiguredUpdatesRoot()
    {
        var updatesRoot = Path.Combine(_root, "updates");
        var manager = CreateManager(updatesRoot);
        var outside = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");

        var deleted = manager.TryDelete(outside, "测试越界路径");

        Assert.False(deleted);
        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
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

    private async Task<(
        WindowsUpdateWorkspaceManager Manager,
        WindowsUpdateWorkspace Workspace,
        ReleaseAssetInfo Asset)> CreateVerifiedWorkspaceAsync()
    {
        var manager = CreateManager(Path.Combine(_root, "updates"));
        var workspace = manager.Create();
        var packageSource = Path.Combine(_root, "package-source");
        UpdatePackageValidatorTests.WritePackage(
            packageSource,
            "1.0.1",
            new Dictionary<string, string>
            {
                [UpdateProtocol.EntryExecutableName] = "desktop",
                [UpdateProtocol.UpdaterExecutableName] = "updater",
                ["feature.dll"] = "feature"
            });
        ZipFile.CreateFromDirectory(packageSource, workspace.ArchivePath);
        await UpdatePackageValidator.ExtractAndValidateAsync(
            workspace.ArchivePath,
            workspace.StagingDirectory,
            "1.0.1");
        var archiveBytes = await File.ReadAllBytesAsync(workspace.ArchivePath);
        var asset = new ReleaseAssetInfo(
            "IGoLibrary-Ex-v1.0.1-windows-x64.zip",
            new Uri("https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/package.zip"),
            archiveBytes.LongLength,
            $"sha256:{Convert.ToHexString(SHA256.HashData(archiveBytes))}",
            "application/zip");
        manager.WriteVerifiedMarker(workspace, "1.0.1", asset);
        return (manager, workspace, asset);
    }

    private static WindowsUpdateWorkspaceManager CreateManager(string updatesRoot)
    {
        return new WindowsUpdateWorkspaceManager(
            NullLogger<WindowsUpdateWorkspaceManager>.Instance,
            updatesRoot,
            TimeProvider.System);
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Diagnostics;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Tests;

public sealed class UpdateTransactionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-transaction-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PrepareApplyAndCommit_PreservesUnknownFilesAndRemovesOldOwnedFiles()
    {
        var request = CreateRequest();

        await UpdateTransaction.PrepareCandidateAsync(request);

        Assert.Equal("user", File.ReadAllText(Path.Combine(request.CandidateDirectory, "user.txt")));
        Assert.Equal("new-settings", File.ReadAllText(Path.Combine(request.CandidateDirectory, "appsettings.json")));
        Assert.False(File.Exists(Path.Combine(request.CandidateDirectory, "old-only.dll")));
        Assert.True(File.Exists(Path.Combine(request.CandidateDirectory, "new-only.dll")));

        UpdateTransaction.Apply(request);
        Assert.Equal("new-desktop", File.ReadAllText(Path.Combine(
            request.InstallationDirectory,
            UpdateProtocol.EntryExecutableName)));
        Assert.True(Directory.Exists(request.BackupDirectory));

        UpdateTransaction.Commit(request);
        Assert.False(Directory.Exists(request.BackupDirectory));
        Assert.False(Directory.Exists(request.CandidateDirectory));
        Assert.Equal("user", File.ReadAllText(Path.Combine(request.InstallationDirectory, "user.txt")));
    }

    [Fact]
    public async Task Rollback_RestoresOldDirectoryAfterApply()
    {
        var request = CreateRequest(
            includeCurrentManagedCloudflared: true,
            includeTargetManagedCloudflared: true);
        await UpdateTransaction.PrepareCandidateAsync(request);
        UpdateTransaction.Apply(request);

        Assert.Equal("new-cloudflared", File.ReadAllText(GetManagedPath(
            request.InstallationDirectory,
            UpdateProtocol.ManagedCloudflaredExecutablePath)));

        UpdateTransaction.Rollback(request);

        Assert.Equal("old-desktop", File.ReadAllText(Path.Combine(
            request.InstallationDirectory,
            UpdateProtocol.EntryExecutableName)));
        Assert.True(File.Exists(Path.Combine(request.InstallationDirectory, "old-only.dll")));
        Assert.False(File.Exists(Path.Combine(request.InstallationDirectory, "new-only.dll")));
        Assert.Equal("user", File.ReadAllText(Path.Combine(request.InstallationDirectory, "user.txt")));
        Assert.Equal("user-upgraded-cloudflared", File.ReadAllText(GetManagedPath(
            request.InstallationDirectory,
            UpdateProtocol.ManagedCloudflaredExecutablePath)));
        Assert.False(File.Exists(GetManagedPath(
            request.InstallationDirectory,
            UpdateProtocol.ManagedCloudflaredLicensePath)));
        Assert.False(Directory.Exists(request.BackupDirectory));
    }

    [Fact]
    public async Task PrepareApplyAndCommit_ReplacesManagedCloudflaredAndPreservesOtherTools()
    {
        var request = CreateRequest(
            includeCurrentManagedCloudflared: true,
            includeTargetManagedCloudflared: true);

        await UpdateTransaction.PrepareCandidateAsync(request);

        Assert.Equal("new-cloudflared", File.ReadAllText(GetManagedPath(
            request.CandidateDirectory,
            UpdateProtocol.ManagedCloudflaredExecutablePath)));
        Assert.Equal("new-license", File.ReadAllText(GetManagedPath(
            request.CandidateDirectory,
            UpdateProtocol.ManagedCloudflaredLicensePath)));
        Assert.Equal("new-notices", File.ReadAllText(GetManagedPath(
            request.CandidateDirectory,
            UpdateProtocol.ManagedCloudflaredNoticesPath)));
        Assert.Equal("user-added-tool", File.ReadAllText(Path.Combine(
            request.CandidateDirectory,
            "tools",
            "user-tool.txt")));
        Assert.Equal("custom-cloudflared-config", File.ReadAllText(Path.Combine(
            request.CandidateDirectory,
            "tools",
            "cloudflared",
            "custom.json")));

        UpdateTransaction.Apply(request);
        UpdateTransaction.Commit(request);

        Assert.Equal("new-cloudflared", File.ReadAllText(GetManagedPath(
            request.InstallationDirectory,
            UpdateProtocol.ManagedCloudflaredExecutablePath)));
        Assert.Equal("user-added-tool", File.ReadAllText(Path.Combine(
            request.InstallationDirectory,
            "tools",
            "user-tool.txt")));
        Assert.Equal("custom-cloudflared-config", File.ReadAllText(Path.Combine(
            request.InstallationDirectory,
            "tools",
            "cloudflared",
            "custom.json")));
    }

    [Fact]
    public async Task PrepareApplyAndCommit_InstallsManagedCloudflaredIntoLightweightInstallation()
    {
        var request = CreateRequest(includeTargetManagedCloudflared: true);

        await UpdateTransaction.PrepareCandidateAsync(request);
        UpdateTransaction.Apply(request);
        UpdateTransaction.Commit(request);

        Assert.Equal("new-cloudflared", File.ReadAllText(GetManagedPath(
            request.InstallationDirectory,
            UpdateProtocol.ManagedCloudflaredExecutablePath)));
        Assert.Equal("new-license", File.ReadAllText(GetManagedPath(
            request.InstallationDirectory,
            UpdateProtocol.ManagedCloudflaredLicensePath)));
        Assert.Equal("new-notices", File.ReadAllText(GetManagedPath(
            request.InstallationDirectory,
            UpdateProtocol.ManagedCloudflaredNoticesPath)));
    }

    [Fact]
    public async Task PrepareCandidateAsync_CreatesOnlyManagedToolsForLightweightInstallation()
    {
        var request = CreateRequest();

        await UpdateTransaction.PrepareCandidateAsync(request);

        var actualTools = Directory.EnumerateFiles(
                Path.Combine(request.CandidateDirectory, "tools"),
                "*",
                SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(request.CandidateDirectory, path).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(
            UpdateProtocol.ManagedCloudflaredFilePaths
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase),
            actualTools,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareCandidateAsync_RejectsTargetNonManagedToolsBeforeChangingInstallation()
    {
        var request = CreateRequest(includeTargetNonManagedTools: true);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateTransaction.PrepareCandidateAsync(request));

        Assert.Contains("非托管 tools", exception.Message);
        Assert.False(Directory.Exists(request.CandidateDirectory));
        Assert.Equal("old-desktop", File.ReadAllText(Path.Combine(
            request.InstallationDirectory,
            UpdateProtocol.EntryExecutableName)));
    }

    [Fact]
    public async Task PrepareCandidateAsync_RejectsTargetWithoutManagedCloudflaredBeforeChangingInstallation()
    {
        var request = CreateRequest(includeTargetManagedCloudflared: false);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateTransaction.PrepareCandidateAsync(request));

        Assert.Contains("必须完整声明", exception.Message);
        Assert.False(Directory.Exists(request.CandidateDirectory));
        Assert.Equal("old-desktop", File.ReadAllText(Path.Combine(
            request.InstallationDirectory,
            UpdateProtocol.EntryExecutableName)));
    }

    [Fact]
    public void Apply_DoesNotModifyInstallationWhenCandidateIsMissing()
    {
        var request = CreateRequest();

        Assert.Throws<DirectoryNotFoundException>(() => UpdateTransaction.Apply(request));

        Assert.Equal("old-desktop", File.ReadAllText(Path.Combine(
            request.InstallationDirectory,
            UpdateProtocol.EntryExecutableName)));
        Assert.False(Directory.Exists(request.BackupDirectory));
    }

    [Fact]
    public async Task PrepareCandidateFromArchiveAsync_RevalidatesProtectedArchiveInsteadOfTrustingStaging()
    {
        var request = CreateRequest(
            includeCurrentManagedCloudflared: true,
            includeTargetManagedCloudflared: true);
        ZipFile.CreateFromDirectory(request.StagingDirectory, request.PackagePath);
        var digest = "sha256:" + Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(request.PackagePath)));
        request = request with
        {
            PackageDigest = digest,
            PackageSize = new FileInfo(request.PackagePath).Length
        };
        File.WriteAllText(
            Path.Combine(request.StagingDirectory, UpdateProtocol.EntryExecutableName),
            "tampered-staging");

        await UpdateTransaction.PrepareCandidateFromArchiveAsync(request);

        Assert.Equal("new-desktop", File.ReadAllText(Path.Combine(
            request.CandidateDirectory,
            UpdateProtocol.EntryExecutableName)));
        Assert.Equal("user", File.ReadAllText(Path.Combine(request.CandidateDirectory, "user.txt")));
        Assert.Equal("new-cloudflared", File.ReadAllText(GetManagedPath(
            request.CandidateDirectory,
            UpdateProtocol.ManagedCloudflaredExecutablePath)));
        Assert.Equal("user-added-tool", File.ReadAllText(Path.Combine(
            request.CandidateDirectory,
            "tools",
            "user-tool.txt")));
    }

    [Fact]
    public async Task PrepareCandidateFromArchiveAsync_RejectsDigestMismatchBeforeCandidateIsUsed()
    {
        var request = CreateRequest();
        ZipFile.CreateFromDirectory(request.StagingDirectory, request.PackagePath);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateTransaction.PrepareCandidateFromArchiveAsync(request));

        Assert.False(Directory.Exists(request.CandidateDirectory));
        Assert.True(Directory.Exists(request.InstallationDirectory));
    }

    [Fact]
    public async Task RecoverInterruptedAsync_RestoresBackupWhenInstallPathIsMissing()
    {
        var request = CreateRequest(
            includeCurrentManagedCloudflared: true,
            includeTargetManagedCloudflared: true);
        await UpdateTransaction.PrepareCandidateAsync(request);
        Directory.Move(request.InstallationDirectory, request.BackupDirectory);

        var recovered = await UpdateTransaction.RecoverInterruptedAsync(request);

        Assert.True(recovered);
        Assert.True(Directory.Exists(request.InstallationDirectory));
        Assert.False(Directory.Exists(request.BackupDirectory));
        Assert.Equal("old-desktop", File.ReadAllText(Path.Combine(
            request.InstallationDirectory,
            UpdateProtocol.EntryExecutableName)));
        Assert.Equal("user-upgraded-cloudflared", File.ReadAllText(GetManagedPath(
            request.InstallationDirectory,
            UpdateProtocol.ManagedCloudflaredExecutablePath)));
    }

    [Fact]
    public void CleanupRollbackArtifacts_RemovesInterruptedPreparationDirectories()
    {
        var request = CreateRequest();
        Directory.CreateDirectory(request.CandidateDirectory);
        Directory.CreateDirectory(request.CandidateDirectory + ".workspace");
        Directory.CreateDirectory(request.CandidateDirectory + ".failed");

        UpdateTransaction.CleanupRollbackArtifacts(request);

        Assert.False(Directory.Exists(request.CandidateDirectory));
        Assert.False(Directory.Exists(request.CandidateDirectory + ".workspace"));
        Assert.False(Directory.Exists(request.CandidateDirectory + ".failed"));
    }

    [Fact]
    public void ValidateRequest_RejectsPrereleaseTargetVersion()
    {
        var request = CreateRequest() with { TargetVersion = "1.0.1-beta.1" };

        Assert.Throws<InvalidDataException>(() => UpdateTransaction.ValidateRequest(request));
    }

    [Fact]
    public void ValidateRequest_RejectsNonCanonicalTransactionDirectories()
    {
        var request = CreateRequest();
        request = request with { BackupDirectory = request.BackupDirectory + "-attacker" };

        Assert.Throws<InvalidDataException>(() => UpdateTransaction.ValidateRequest(request));
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

    private UpdateTransactionRequest CreateRequest(
        bool includeCurrentManagedCloudflared = false,
        bool includeTargetManagedCloudflared = true,
        bool includeTargetNonManagedTools = false)
    {
        Directory.CreateDirectory(_root);
        var installation = Path.Combine(_root, "IGoLibrary-Ex");
        var staging = Path.Combine(_root, "transaction", "staging");
        var installationFiles = new Dictionary<string, string>
        {
            [UpdateProtocol.EntryExecutableName] = "old-desktop",
            [UpdateProtocol.UpdaterExecutableName] = "old-updater",
            ["old-only.dll"] = "old"
        };
        if (includeCurrentManagedCloudflared)
        {
            installationFiles[UpdateProtocol.ManagedCloudflaredExecutablePath] = "release-cloudflared";
            installationFiles[UpdateProtocol.ManagedCloudflaredLicensePath] = "release-license";
            installationFiles[UpdateProtocol.ManagedCloudflaredNoticesPath] = "release-notices";
        }

        UpdatePackageValidatorTests.WritePackage(
            installation,
            "1.0.0",
            installationFiles);
        File.WriteAllText(Path.Combine(installation, "user.txt"), "user");
        File.WriteAllText(Path.Combine(installation, "appsettings.json"), "old-settings");
        if (includeCurrentManagedCloudflared)
        {
            File.WriteAllText(
                GetManagedPath(installation, UpdateProtocol.ManagedCloudflaredExecutablePath),
                "user-upgraded-cloudflared");
            File.Delete(GetManagedPath(installation, UpdateProtocol.ManagedCloudflaredLicensePath));
            File.WriteAllText(
                Path.Combine(installation, "tools", "user-tool.txt"),
                "user-added-tool");
            File.WriteAllText(
                Path.Combine(installation, "tools", "cloudflared", "custom.json"),
                "custom-cloudflared-config");
        }

        var stagingFiles = new Dictionary<string, string>
        {
            [UpdateProtocol.EntryExecutableName] = "new-desktop",
            [UpdateProtocol.UpdaterExecutableName] = "new-updater",
            ["new-only.dll"] = "new",
            ["appsettings.json"] = "new-settings"
        };
        if (includeTargetManagedCloudflared)
        {
            stagingFiles[UpdateProtocol.ManagedCloudflaredExecutablePath] = "new-cloudflared";
            stagingFiles[UpdateProtocol.ManagedCloudflaredLicensePath] = "new-license";
            stagingFiles[UpdateProtocol.ManagedCloudflaredNoticesPath] = "new-notices";
        }
        if (includeTargetNonManagedTools)
        {
            stagingFiles["tools/custom-tool.exe"] = "forbidden-tool";
        }

        UpdatePackageValidatorTests.WritePackage(
            staging,
            "1.0.1",
            stagingFiles);

        var id = Guid.NewGuid().ToString("N");
        var transaction = Path.GetDirectoryName(staging)!;
        return new UpdateTransactionRequest(
            UpdateProtocol.SchemaVersion,
            id,
            Environment.ProcessId,
            new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero),
            "1.0.0",
            "1.0.1",
            installation,
            staging,
            transaction,
            Path.Combine(transaction, "package.zip"),
            Path.Combine(_root, $".IGoLibrary-Ex.update-{id}"),
            Path.Combine(_root, $".IGoLibrary-Ex.backup-{id}"),
            UpdateProtocol.EntryExecutableName,
            UpdateProtocol.ManifestFileName,
            "sha256:" + new string('0', 64),
            1,
            Path.Combine(transaction, "health.json"),
            Path.Combine(transaction, "coordinator-signal.json"),
            Path.Combine(transaction, "worker-ready.json"),
            Path.Combine(transaction, "worker-status.json"),
            Path.Combine(transaction, "decision.json"),
            Path.Combine(transaction, "heartbeat.txt"),
            Path.Combine(transaction, "launched-process.json"),
            Path.Combine(_root, "logs"));
    }

    private static string GetManagedPath(string root, string relativePath)
    {
        return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}

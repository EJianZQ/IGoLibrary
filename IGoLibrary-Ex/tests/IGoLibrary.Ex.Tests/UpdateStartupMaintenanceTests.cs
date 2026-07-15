using System.Runtime.Versioning;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Tests;

[SupportedOSPlatform("windows")]
public sealed class UpdateStartupMaintenanceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-update-maintenance-tests",
        Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RunForTests_DeletesIncompleteDownloadImmediately()
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var directory = CreateTransactionDirectory(transactionId);
        File.WriteAllBytes(Path.Combine(directory, "package.zip.partial"), [1, 2, 3]);

        var result = UpdateStartupMaintenance.RunForTests(_root, null, _now);

        Assert.False(Directory.Exists(directory));
        Assert.Equal(1, result.DeletedIncompleteDownloadCount);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void RunForTests_RetainsValidVerifiedCacheWithinSevenDays()
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var directory = WriteVerifiedCache(transactionId, _now - TimeSpan.FromDays(6));

        var result = UpdateStartupMaintenance.RunForTests(_root, null, _now);

        Assert.True(Directory.Exists(directory));
        Assert.Equal(1, result.RetainedVerifiedCacheCount);
        Assert.Equal(0, result.DeletedInvalidOrExpiredCacheCount);
    }

    [Fact]
    public void RunForTests_DeletesVerifiedCacheAtRetentionBoundary()
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var directory = WriteVerifiedCache(transactionId, _now - TimeSpan.FromDays(7));

        var result = UpdateStartupMaintenance.RunForTests(_root, null, _now);

        Assert.False(Directory.Exists(directory));
        Assert.Equal(1, result.DeletedInvalidOrExpiredCacheCount);
    }

    [Fact]
    public void RunForTests_DeletesStructurallyInvalidVerifiedCache()
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var directory = WriteVerifiedCache(transactionId, _now - TimeSpan.FromDays(1));
        UpdateJsonFile.WriteAtomic(
            Path.Combine(directory, "verified-cache.json"),
            new VerifiedUpdateCache(
                UpdateProtocol.SchemaVersion,
                Guid.NewGuid().ToString("N"),
                "1.0.1",
                "sha256:" + new string('a', 64),
                3,
                _now - TimeSpan.FromDays(1)),
            DesktopUpdateJsonSerializerContext.Default.VerifiedUpdateCache);

        var result = UpdateStartupMaintenance.RunForTests(_root, null, _now);

        Assert.False(Directory.Exists(directory));
        Assert.Equal(1, result.DeletedInvalidOrExpiredCacheCount);
    }

    [Fact]
    public void RunForTests_DeletesVerifiedMarkerWhenRequiredFilesAreMissing()
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var directory = WriteVerifiedCache(transactionId, _now - TimeSpan.FromDays(1));
        File.Delete(Path.Combine(directory, "package.zip"));

        var result = UpdateStartupMaintenance.RunForTests(_root, null, _now);

        Assert.False(Directory.Exists(directory));
        Assert.Equal(1, result.DeletedInvalidOrExpiredCacheCount);
    }

    [Fact]
    public void RunForTests_SkipsCurrentUpdateTransaction()
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var directory = CreateTransactionDirectory(transactionId);
        File.WriteAllBytes(Path.Combine(directory, "package.zip.partial"), [1]);

        var result = UpdateStartupMaintenance.RunForTests(_root, transactionId, _now);

        Assert.True(Directory.Exists(directory));
        Assert.Equal(0, result.DeletedIncompleteDownloadCount);
    }

    [Fact]
    public void RunForTests_RequestTransactionIsNeverTreatedAsIncompleteDownload()
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var directory = CreateTransactionDirectory(transactionId);
        File.WriteAllText(Path.Combine(directory, "request.json"), "{}");

        var result = UpdateStartupMaintenance.RunForTests(_root, null, _now);

        Assert.True(Directory.Exists(directory));
        Assert.Equal(0, result.DeletedIncompleteDownloadCount);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void RunForTests_RestoresStaleAbortedHandoffToVerifiedCache()
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var directory = WriteVerifiedCache(transactionId, _now - TimeSpan.FromDays(1));
        var requestPath = Path.Combine(directory, "request.json");
        UpdateJsonFile.WriteAtomic(
            requestPath,
            CreateRequest(transactionId, directory),
            UpdateJsonTypeInfo.TransactionRequest);
        File.SetLastWriteTimeUtc(requestPath, (_now - TimeSpan.FromMinutes(2)).UtcDateTime);
        File.WriteAllText(
            Path.Combine(directory, UpdateProtocol.UpdaterExecutableName),
            "copied updater");

        var result = UpdateStartupMaintenance.RunForTests(_root, null, _now);

        Assert.True(Directory.Exists(directory));
        Assert.False(File.Exists(requestPath));
        Assert.False(File.Exists(Path.Combine(directory, UpdateProtocol.UpdaterExecutableName)));
        Assert.True(File.Exists(Path.Combine(directory, "verified-cache.json")));
        Assert.True(File.Exists(Path.Combine(directory, "package.zip")));
        Assert.True(Directory.Exists(Path.Combine(directory, "staging")));
        Assert.Equal(1, result.RestoredVerifiedCacheCount);
        Assert.Equal(0, result.DeletedUpdaterTransactionCount);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void RunForTests_CleansUpdaterArtifactCreatedBeforeRequest()
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var directory = WriteVerifiedCache(transactionId, _now - TimeSpan.FromDays(1));
        var updaterPath = Path.Combine(directory, UpdateProtocol.UpdaterExecutableName);
        File.WriteAllText(updaterPath, "partial updater");

        var result = UpdateStartupMaintenance.RunForTests(_root, null, _now);

        Assert.True(Directory.Exists(directory));
        Assert.False(File.Exists(updaterPath));
        Assert.True(File.Exists(Path.Combine(directory, "verified-cache.json")));
        Assert.Equal(1, result.RestoredVerifiedCacheCount);
        Assert.Equal(0, result.DeletedIncompleteDownloadCount);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void RunForTests_ReadyHandoffKeepsUpdaterTransactionOwnership()
    {
        var transactionId = Guid.NewGuid().ToString("N");
        var directory = WriteVerifiedCache(transactionId, _now - TimeSpan.FromDays(1));
        var requestPath = Path.Combine(directory, "request.json");
        UpdateJsonFile.WriteAtomic(
            requestPath,
            CreateRequest(transactionId, directory),
            UpdateJsonTypeInfo.TransactionRequest);
        File.SetLastWriteTimeUtc(requestPath, (_now - TimeSpan.FromMinutes(2)).UtcDateTime);
        var signalPath = Path.Combine(directory, "coordinator-signal.json");
        UpdateJsonFile.WriteAtomic(
            signalPath,
            new UpdateCoordinatorSignal(
                UpdateProtocol.SchemaVersion,
                transactionId,
                UpdateCoordinatorSignalKind.Ready,
                "ready",
                _now - TimeSpan.FromMinutes(2)),
            UpdateJsonTypeInfo.CoordinatorSignal);
        File.WriteAllText(
            Path.Combine(directory, UpdateProtocol.UpdaterExecutableName),
            "copied updater");

        var result = UpdateStartupMaintenance.RunForTests(_root, null, _now);

        Assert.True(File.Exists(requestPath));
        Assert.True(File.Exists(signalPath));
        Assert.True(File.Exists(Path.Combine(directory, UpdateProtocol.UpdaterExecutableName)));
        Assert.Equal(0, result.RestoredVerifiedCacheCount);
        Assert.Equal(0, result.DeletedUpdaterTransactionCount);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void RunForTests_IgnoresNonGuidDirectory()
    {
        var unrelated = Path.Combine(_root, "unrelated");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "keep");

        var result = UpdateStartupMaintenance.RunForTests(_root, null, _now);

        Assert.True(File.Exists(Path.Combine(unrelated, "keep.txt")));
        Assert.Equal(0, result.DeletedIncompleteDownloadCount);
        Assert.Empty(result.Failures);
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

    private string WriteVerifiedCache(string transactionId, DateTimeOffset verifiedAt)
    {
        var directory = CreateTransactionDirectory(transactionId);
        File.WriteAllBytes(Path.Combine(directory, "package.zip"), [1, 2, 3]);
        var staging = Directory.CreateDirectory(Path.Combine(directory, "staging")).FullName;
        File.WriteAllText(Path.Combine(staging, UpdateProtocol.ManifestFileName), "{}");
        UpdateJsonFile.WriteAtomic(
            Path.Combine(directory, "verified-cache.json"),
            new VerifiedUpdateCache(
                UpdateProtocol.SchemaVersion,
                transactionId,
                "1.0.1",
                "sha256:" + new string('a', 64),
                3,
                verifiedAt),
            DesktopUpdateJsonSerializerContext.Default.VerifiedUpdateCache);
        return directory;
    }

    private string CreateTransactionDirectory(string transactionId)
    {
        Directory.CreateDirectory(_root);
        return Directory.CreateDirectory(Path.Combine(_root, transactionId)).FullName;
    }

    private UpdateTransactionRequest CreateRequest(
        string transactionId,
        string transactionDirectory)
    {
        var installationDirectory = Path.Combine(_root, $"installation-{transactionId}");
        return new UpdateTransactionRequest(
            UpdateProtocol.SchemaVersion,
            transactionId,
            int.MaxValue,
            _now - TimeSpan.FromHours(1),
            "1.0.0",
            "1.0.1",
            installationDirectory,
            Path.Combine(transactionDirectory, "staging"),
            transactionDirectory,
            Path.Combine(transactionDirectory, "package.zip"),
            Path.Combine(_root, $".IGoLibrary-Ex.update-{transactionId}"),
            Path.Combine(_root, $".IGoLibrary-Ex.backup-{transactionId}"),
            UpdateProtocol.EntryExecutableName,
            UpdateProtocol.ManifestFileName,
            "sha256:" + new string('a', 64),
            3,
            Path.Combine(transactionDirectory, "health.json"),
            Path.Combine(transactionDirectory, "coordinator-signal.json"),
            Path.Combine(transactionDirectory, "worker-ready.json"),
            Path.Combine(transactionDirectory, "worker-status.json"),
            Path.Combine(transactionDirectory, "decision.json"),
            Path.Combine(transactionDirectory, "heartbeat.txt"),
            Path.Combine(transactionDirectory, "launched-process.json"),
            Path.Combine(_root, "logs"));
    }
}

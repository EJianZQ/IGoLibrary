using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.DataTransfer;
using IGoLibrary.Ex.Infrastructure.Persistence;
using IGoLibrary.Ex.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class BackupRestoreTransactionIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApplyThenComplete_CommitsDatabaseCredentialsAndIncomingBackupPassword()
    {
        var fixture = await CreateStagedRestoreAsync();

        var installed = await fixture.Restore.ApplyAsync(fixture.TransactionId);
        Assert.True(installed.Succeeded);
        Assert.Contains("等待应用初始化", installed.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.SourceSettingsJson, await ReadSettingsAsync(fixture.Factory));
        Assert.Equal("COOKIE-A", (await fixture.Credentials.LoadSessionAsync())?.Cookie);

        var completed = await fixture.Restore.CompleteAsync(fixture.TransactionId);

        Assert.True(completed.Succeeded);
        Assert.Equal("source-password", await fixture.SecretStore.LoadBackupPasswordAsync());
        Assert.False(Directory.Exists(
            Path.Combine(_directory, "data", ".backup-sync", "restore", fixture.TransactionId)));
        var startupResult = await fixture.Restore.ConsumeStartupResultAsync();
        Assert.True(startupResult?.Succeeded);
    }

    [Fact]
    public async Task CrashBeforeComplete_RecoverIncompleteRollsBackDatabaseAndAllCredentials()
    {
        var fixture = await CreateStagedRestoreAsync();
        await fixture.Restore.ApplyAsync(fixture.TransactionId);

        var recovered = await fixture.Restore.RecoverIncompleteAsync();

        Assert.False(recovered?.Succeeded);
        Assert.Contains("自动恢复", recovered?.Message, StringComparison.Ordinal);
        Assert.Equal(fixture.LocalSettingsJson, await ReadSettingsAsync(fixture.Factory));
        Assert.Equal("COOKIE-B", (await fixture.Credentials.LoadSessionAsync())?.Cookie);
        Assert.Equal("REMOTE-B", (await fixture.Credentials.LoadRemoteCheckInSessionAsync())?.Token);
        Assert.Equal("WEBDAV-B", await fixture.SecretStore.LoadWebDavPasswordAsync());
        Assert.Equal("previous-password", await fixture.SecretStore.LoadBackupPasswordAsync());
    }

    [Fact]
    public async Task Apply_RejectsLocalDataChangedAfterTheRestoreWasStaged()
    {
        var fixture = await CreateStagedRestoreAsync();
        const string changedAfterStage = "{\"ui\":{\"minimizeToTray\":null}}";
        await WriteSettingsAsync(fixture.Factory, changedAfterStage);
        fixture.Tracker.MarkChanged();

        var result = await fixture.Restore.ApplyAsync(fixture.TransactionId);

        Assert.False(result.Succeeded);
        Assert.Contains("事务创建后发生变化", result.Message, StringComparison.Ordinal);
        Assert.Equal(changedAfterStage, await ReadSettingsAsync(fixture.Factory));
        Assert.Equal("COOKIE-B", (await fixture.Credentials.LoadSessionAsync())?.Cookie);
    }

    [Theory]
    [InlineData((int)BackupRestoreTransactionPhase.SyncStatePending)]
    [InlineData((int)BackupRestoreTransactionPhase.Committed)]
    public async Task RecoverCommittedData_IdempotentlyFinalizesTheLocalSyncSafetyState(
        int phaseValue)
    {
        var phase = (BackupRestoreTransactionPhase)phaseValue;
        var fixture = await CreateStagedRestoreAsync();
        await fixture.Restore.ApplyAsync(fixture.TransactionId);
        var transactionDirectory = Path.Combine(
            fixture.Locations.DataDirectory,
            ".backup-sync",
            "restore",
            fixture.TransactionId);
        var transaction = DataBackupService.ReadTransaction(transactionDirectory) with { Phase = phase };
        await DataBackupService.WriteTransactionAsync(
            transactionDirectory,
            transaction,
            CancellationToken.None);

        var recovered = await fixture.Restore.RecoverIncompleteAsync();

        Assert.True(recovered?.Succeeded);
        Assert.Equal(fixture.SourceSettingsJson, await ReadSettingsAsync(fixture.Factory));
        Assert.Equal("COOKIE-A", (await fixture.Credentials.LoadSessionAsync())?.Cookie);
        Assert.True(fixture.Tracker.IsDirty);
        Assert.True(fixture.Tracker.IsAutomaticUploadPaused);
        Assert.Contains("本地备份", fixture.Tracker.AutomaticUploadPauseReason, StringComparison.Ordinal);
        Assert.False(Directory.Exists(transactionDirectory));
    }

    [Fact]
    public async Task CleanupFailure_RetainsTransactionUntilTheRestoreSecretCanBeDeleted()
    {
        var fixture = await CreateStagedRestoreAsync();
        await fixture.Restore.ApplyAsync(fixture.TransactionId);
        var transactionDirectory = Path.Combine(
            fixture.Locations.DataDirectory,
            ".backup-sync",
            "restore",
            fixture.TransactionId);
        fixture.SecretBackend.FailDeletes = true;

        var completed = await fixture.Restore.CompleteAsync(fixture.TransactionId);

        Assert.True(completed.Succeeded);
        Assert.True(Directory.Exists(transactionDirectory));
        Assert.NotNull(await fixture.SecretStore.LoadRestoreSecretAsync(fixture.TransactionId));

        fixture.SecretBackend.FailDeletes = false;
        var recovered = await fixture.Restore.RecoverIncompleteAsync();

        Assert.True(recovered?.Succeeded);
        Assert.False(Directory.Exists(transactionDirectory));
        Assert.Null(await fixture.SecretStore.LoadRestoreSecretAsync(fixture.TransactionId));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<Fixture> CreateStagedRestoreAsync()
    {
        var locations = new StorageLocations(
            Path.Combine(_directory, "data"),
            Path.Combine(_directory, "logs"));
        var factory = new SqliteConnectionFactory(locations);
        await new SqliteAppDataInitializer(factory).InitializeAsync();
        var tracker = new PersistentDataChangeTracker(
            locations,
            NullLogger<PersistentDataChangeTracker>.Instance);
        var credentials = new InMemoryCredentialStore(tracker);
        var secretBackend = new FailingDeleteBackupSecretBackend();
        var secretStore = new PlatformBackupSecretStore(secretBackend, tracker);
        var activity = new ActivityLogService();
        var sourceJson = "{\"ui\":{\"minimizeToTray\":true}}";
        var localJson = "{\"ui\":{\"minimizeToTray\":false}}";
        await WriteSettingsAsync(factory, sourceJson);
        await credentials.SaveSessionAsync(Session("COOKIE-A"));
        await credentials.SaveRemoteCheckInSessionAsync(Remote("REMOTE-A"));
        await secretStore.SaveWebDavPasswordAsync("WEBDAV-A");
        await secretStore.SaveBackupPasswordAsync("previous-password");
        var backup = new DataBackupService(
            factory,
            locations,
            credentials,
            secretStore,
            tracker,
            new FakeAppVersionProvider(),
            activity,
            NullLogger<DataBackupService>.Instance,
            TimeProvider.System);
        var sourceFile = Path.Combine(_directory, "source.igobackup");
        await backup.ExportAsync(sourceFile, "source-password");

        await WriteSettingsAsync(factory, localJson);
        await credentials.SaveSessionAsync(Session("COOKIE-B"));
        await credentials.SaveRemoteCheckInSessionAsync(Remote("REMOTE-B"));
        await secretStore.SaveWebDavPasswordAsync("WEBDAV-B");
        var prepared = await backup.PrepareImportAsync(sourceFile, "source-password");
        var transactionId = await backup.StageRestoreAsync(new BackupRestoreRequest(
            prepared.PreparationId,
            "source-password",
            BackupRestoreSource.LocalFile));
        var restore = new BackupRestoreStartupService(
            factory,
            locations,
            credentials,
            secretStore,
            new NoOpWebDavSyncService(),
            tracker,
            new FakeAppVersionProvider(),
            activity,
            NullLogger<BackupRestoreStartupService>.Instance,
            TimeProvider.System);
        return new Fixture(
            factory,
            locations,
            credentials,
            secretStore,
            secretBackend,
            tracker,
            restore,
            transactionId,
            sourceJson,
            localJson);
    }

    private static SessionCredentials Session(string cookie)
        => new(cookie, SessionSource.ManualCookie, DateTimeOffset.Parse("2026-07-18T08:00:00Z"), true);

    private static RemoteCheckInSessionCredentials Remote(string token)
        => new(token, DateTimeOffset.Parse("2026-07-18T08:00:00Z"), true);

    private static async Task WriteSettingsAsync(SqliteConnectionFactory factory, string json)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Settings(Key, Value) VALUES('app-settings', $value) " +
            "ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
        command.Parameters.AddWithValue("$value", json);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadSettingsAsync(SqliteConnectionFactory factory)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = 'app-settings';";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private sealed record Fixture(
        SqliteConnectionFactory Factory,
        StorageLocations Locations,
        InMemoryCredentialStore Credentials,
        PlatformBackupSecretStore SecretStore,
        FailingDeleteBackupSecretBackend SecretBackend,
        PersistentDataChangeTracker Tracker,
        BackupRestoreStartupService Restore,
        string TransactionId,
        string SourceSettingsJson,
        string LocalSettingsJson);

    private sealed class FailingDeleteBackupSecretBackend : IBackupSecretBackend
    {
        private readonly InMemoryBackupSecretBackend _inner = new();

        public bool FailDeletes { get; set; }

        public bool IsPersistent => _inner.IsPersistent;

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
            => _inner.ReadAsync(key, cancellationToken);

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken)
            => _inner.WriteAsync(key, value, cancellationToken);

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
            => FailDeletes
                ? Task.FromException(new IOException("credential store unavailable"))
                : _inner.DeleteAsync(key, cancellationToken);
    }

    private sealed class NoOpWebDavSyncService : IWebDavSyncService
    {
        public BackupSyncRuntimeStatus Status => BackupSyncRuntimeStatus.Idle;
        public event EventHandler<BackupSyncRuntimeStatus>? StatusChanged { add { } remove { } }
        public Task ReconcileLocalStateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordRestoredBaselineAsync(string semanticFingerprint, WebDavRemoteMetadata metadata, string expectedEndpointFingerprint, string remoteFileSha256, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<WebDavRemoteMetadata> TestConnectionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WebDavUploadResult> UploadAsync(bool allowOverwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WebDavDownloadResult> DownloadAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DiscardDownloadAsync(string localFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

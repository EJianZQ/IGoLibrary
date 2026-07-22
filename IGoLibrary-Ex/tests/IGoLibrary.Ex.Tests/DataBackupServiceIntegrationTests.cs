using System.Text;
using System.Text.Json;
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

public sealed class DataBackupServiceIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Export_ContainsEveryPersistentTableAndCredential_ButExcludesLogsAndBackupPassword()
    {
        var dataDirectory = Path.Combine(_directory, "data");
        var logDirectory = Path.Combine(_directory, "logs");
        var locations = new StorageLocations(dataDirectory, logDirectory);
        var factory = new SqliteConnectionFactory(locations);
        await new SqliteAppDataInitializer(factory).InitializeAsync();
        await SeedEveryTableAsync(factory);
        Directory.CreateDirectory(logDirectory);
        await File.WriteAllTextAsync(Path.Combine(logDirectory, "must-not-back-up.log"), "LOG-SECRET-LITERAL");

        var credentials = new InMemoryCredentialStore();
        await credentials.SaveSessionAsync(new SessionCredentials(
            "COOKIE-SECRET-LITERAL",
            SessionSource.ManualCookie,
            DateTimeOffset.Parse("2026-07-18T08:00:00Z"),
            true));
        await credentials.SaveRemoteCheckInSessionAsync(new RemoteCheckInSessionCredentials(
            "REMOTE-TOKEN-LITERAL",
            DateTimeOffset.Parse("2026-07-18T08:00:00Z"),
            true));
        var secretStore = new PlatformBackupSecretStore(new InMemoryBackupSecretBackend());
        await secretStore.SaveBackupPasswordAsync("BACKUP-PASSWORD-MUST-NOT-BE-EMBEDDED");
        await secretStore.SaveWebDavPasswordAsync("WEBDAV-PASSWORD-LITERAL");
        var tracker = new PersistentDataChangeTracker(
            locations,
            NullLogger<PersistentDataChangeTracker>.Instance);
        var activityLog = new ActivityLogService();
        var service = new DataBackupService(
            factory,
            locations,
            credentials,
            secretStore,
            tracker,
            new FakeAppVersionProvider(),
            activityLog,
            NullLogger<DataBackupService>.Instance,
            TimeProvider.System);
        var archive = Path.Combine(_directory, "all-data.igobackup");

        var export = await service.ExportAsync(archive, "integration-password", CancellationToken.None);

        Assert.Equal(1, export.Manifest.Summary.SettingsCount);
        Assert.Equal(1, export.Manifest.Summary.FavoriteCount);
        Assert.Equal(1, export.Manifest.Summary.SeatLabelCount);
        Assert.Equal(1, export.Manifest.Summary.ProtocolOverrideCount);
        Assert.Equal(1, export.Manifest.Summary.TaskHistoryCount);
        Assert.True(export.Manifest.Summary.HasSession);
        Assert.True(export.Manifest.Summary.HasRemoteCheckInSession);
        Assert.True(export.Manifest.Summary.HasWebDavPassword);

        var restoredDatabase = Path.Combine(_directory, "restored.db");
        var content = await new EncryptedBackupArchiveCodec().ReadAsync(
            archive,
            "integration-password",
            restoredDatabase);
        var secretsJson = Encoding.UTF8.GetString(content.Secrets);
        var manifestJson = Encoding.UTF8.GetString(content.Manifest);
        var restoredSecrets = JsonSerializer.Deserialize<BackupSecrets>(content.Secrets, AppJson.Default)!;
        Assert.Equal("COOKIE-SECRET-LITERAL", restoredSecrets.Session?.Cookie);
        Assert.Equal("REMOTE-TOKEN-LITERAL", restoredSecrets.RemoteCheckInSession?.Token);
        Assert.Equal("WEBDAV-PASSWORD-LITERAL", restoredSecrets.WebDavPassword);
        Assert.DoesNotContain("BACKUP-PASSWORD-MUST-NOT-BE-EMBEDDED", secretsJson, StringComparison.Ordinal);
        Assert.DoesNotContain(dataDirectory, manifestJson + secretsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(logDirectory, manifestJson + secretsJson, StringComparison.OrdinalIgnoreCase);
        var activityText = string.Join('\n', activityLog.Entries.Select(static entry => entry.Message));
        foreach (var sensitive in new[]
                 {
                     "COOKIE-SECRET-LITERAL",
                     "REMOTE-TOKEN-LITERAL",
                     "WEBDAV-PASSWORD-LITERAL",
                     "BACKUP-PASSWORD-MUST-NOT-BE-EMBEDDED",
                     "integration-password"
                 })
        {
            Assert.DoesNotContain(sensitive, activityText, StringComparison.Ordinal);
        }

        await using var restored = new SqliteConnection($"Data Source={restoredDatabase};Mode=ReadOnly");
        await restored.OpenAsync();
        foreach (var table in new[]
                 {
                     "Settings", "Favorites", "SeatLabels", "ProtocolOverrides", "MobileTaskLaunchHistory"
                 })
        {
            await using var command = restored.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task StageRestore_UsesTheImmutableArchiveThatWasActuallyPreviewed()
    {
        var fixture = await CreateFixtureAsync();
        var previewedArchive = Path.Combine(_directory, "previewed.igobackup");
        var replacementArchive = Path.Combine(_directory, "replacement.igobackup");

        await WriteSettingsAsync(fixture.Factory, "{\"ui\":{\"minimizeToTray\":true}}");
        fixture.Tracker.MarkChanged();
        await fixture.Service.ExportAsync(previewedArchive, "integration-password");
        await WriteSettingsAsync(fixture.Factory, "{\"ui\":{\"minimizeToTray\":false}}");
        fixture.Tracker.MarkChanged();
        await fixture.Service.ExportAsync(replacementArchive, "integration-password");
        await WriteSettingsAsync(fixture.Factory, "{\"ui\":{\"minimizeToTray\":null}}");
        fixture.Tracker.MarkChanged();

        var prepared = await fixture.Service.PrepareImportAsync(
            previewedArchive,
            "integration-password");
        File.Copy(replacementArchive, previewedArchive, overwrite: true);

        var transactionId = await fixture.Service.StageRestoreAsync(
            new BackupRestoreRequest(
                prepared.PreparationId,
                "integration-password",
                BackupRestoreSource.LocalFile));

        var incomingArchive = Path.Combine(
            fixture.Locations.DataDirectory,
            ".backup-sync",
            "restore",
            transactionId,
            "incoming.igobackup");
        var restoredDatabase = Path.Combine(_directory, "immutable-preview.db");
        await new EncryptedBackupArchiveCodec().ReadAsync(
            incomingArchive,
            "integration-password",
            restoredDatabase);
        Assert.Equal(
            "{\"ui\":{\"minimizeToTray\":true}}",
            await ReadSettingsAsync(restoredDatabase));
    }

    [Fact]
    public async Task StageRestore_RejectsLocalChangesMadeAfterPreview()
    {
        var fixture = await CreateFixtureAsync();
        var archive = Path.Combine(_directory, "source.igobackup");
        await WriteSettingsAsync(fixture.Factory, "{\"ui\":{\"minimizeToTray\":true}}");
        fixture.Tracker.MarkChanged();
        await fixture.Service.ExportAsync(archive, "integration-password");
        await WriteSettingsAsync(fixture.Factory, "{\"ui\":{\"minimizeToTray\":false}}");
        fixture.Tracker.MarkChanged();
        var prepared = await fixture.Service.PrepareImportAsync(archive, "integration-password");

        await WriteSettingsAsync(fixture.Factory, "{\"ui\":{\"minimizeToTray\":null}}");
        fixture.Tracker.MarkChanged();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.StageRestoreAsync(new BackupRestoreRequest(
                prepared.PreparationId,
                "integration-password",
                BackupRestoreSource.LocalFile)));

        Assert.Contains("预览后已发生变化", error.Message, StringComparison.Ordinal);
        await fixture.Service.DiscardPreparedAsync(prepared.PreparationId);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static async Task SeedEveryTableAsync(SqliteConnectionFactory factory)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Settings(Key, Value) VALUES('app-settings', '{}');
            INSERT INTO Favorites(LibraryId, SeatKey, SeatName) VALUES(1, 'A-01', 'A01');
            INSERT INTO SeatLabels(LibraryId, SeatKey, SeatName, LabelText) VALUES(1, 'A-01', 'A01', '窗边');
            INSERT INTO ProtocolOverrides(Key, Value) VALUES('protocol-overrides', '{"query":"value"}');
            INSERT INTO MobileTaskLaunchHistory(RecordId, TaskKind, Fingerprint, RecordedAtUtc, PayloadJson)
            VALUES('record-1', 'grab', 'fingerprint-1', '2026-07-18T08:00:00Z', '{}');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<BackupFixture> CreateFixtureAsync()
    {
        var locations = new StorageLocations(
            Path.Combine(_directory, "fixture-data"),
            Path.Combine(_directory, "fixture-logs"));
        var factory = new SqliteConnectionFactory(locations);
        await new SqliteAppDataInitializer(factory).InitializeAsync();
        var tracker = new PersistentDataChangeTracker(
            locations,
            NullLogger<PersistentDataChangeTracker>.Instance);
        var credentials = new InMemoryCredentialStore(tracker);
        var secretStore = new PlatformBackupSecretStore(new InMemoryBackupSecretBackend(), tracker);
        var service = new DataBackupService(
            factory,
            locations,
            credentials,
            secretStore,
            tracker,
            new FakeAppVersionProvider(),
            new ActivityLogService(),
            NullLogger<DataBackupService>.Instance,
            TimeProvider.System);
        return new BackupFixture(factory, locations, tracker, service);
    }

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

    private static async Task<string> ReadSettingsAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = 'app-settings';";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private sealed record BackupFixture(
        SqliteConnectionFactory Factory,
        StorageLocations Locations,
        PersistentDataChangeTracker Tracker,
        DataBackupService Service);
}

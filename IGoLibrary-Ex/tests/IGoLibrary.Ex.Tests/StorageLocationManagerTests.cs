using System.Text.Json;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Tests;

public sealed class StorageLocationManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-StorageTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InitializeAsync_WithoutLocator_UsesLegacyDefaults()
    {
        var defaults = CreateLocations("default");
        var manager = CreateManager(defaults);

        var actual = await manager.InitializeAsync();

        Assert.Equal(defaults, actual);
        Assert.True(Directory.Exists(defaults.DataDirectory));
        Assert.True(Directory.Exists(defaults.LogDirectory));
    }

    [Fact]
    public async Task InitializeAsync_AfterNoMigrationChange_LeavesOldFilesAndUsesNewDirectories()
    {
        var source = CreateLocations("source");
        var target = CreateLocations("target");
        await CreateDatabaseAsync(source, "source-value");
        var manager = CreateManager(source);
        await manager.InitializeAsync();
        await manager.StageChangeAsync(new StorageLocationChangeRequest(target, false, false, false));

        var restarted = CreateManager(source);
        var actual = await restarted.InitializeAsync();

        Assert.Equal(target, actual);
        Assert.True(File.Exists(DatabasePath(source)));
        Assert.False(File.Exists(DatabasePath(target)));
        Assert.True((await restarted.ConsumeStartupResultAsync())?.Succeeded);
    }

    [Fact]
    public async Task InitializeAsync_MigratesDatabaseAndLogs_ThenDeletesSources()
    {
        var source = CreateLocations("source");
        var target = CreateLocations("target");
        await CreateDatabaseAsync(source, "source-value");
        Directory.CreateDirectory(source.LogDirectory);
        var sourceLog = Path.Combine(source.LogDirectory, "app-20260710-090000-000.log");
        await File.WriteAllTextAsync(sourceLog, "source-log");
        var manager = CreateManager(source);
        await manager.InitializeAsync();
        await manager.StageChangeAsync(new StorageLocationChangeRequest(target, true, true, false));

        var restarted = CreateManager(source);
        var actual = await restarted.InitializeAsync();

        Assert.Equal(target, actual);
        Assert.Equal("source-value", await ReadMarkerAsync(target));
        Assert.Equal("source-log", await File.ReadAllTextAsync(Path.Combine(target.LogDirectory, "app-20260710-090000-000.log")));
        Assert.False(File.Exists(DatabasePath(source)));
        Assert.False(File.Exists(sourceLog));
    }

    [Fact]
    public async Task InitializeAsync_WhenTargetDatabaseExistsWithoutConsent_RollsBackToSource()
    {
        var source = CreateLocations("source");
        var target = CreateLocations("target");
        await CreateDatabaseAsync(source, "source-value");
        await CreateDatabaseAsync(target, "target-value");
        var manager = CreateManager(source);
        await manager.InitializeAsync();
        await manager.StageChangeAsync(new StorageLocationChangeRequest(target, true, false, false));

        var restarted = CreateManager(source);
        var actual = await restarted.InitializeAsync();

        Assert.Equal(source, actual);
        Assert.Equal("source-value", await ReadMarkerAsync(source));
        Assert.Equal("target-value", await ReadMarkerAsync(target));
        var result = await restarted.ConsumeStartupResultAsync();
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task InitializeAsync_WithOverwriteConsent_ReplacesTargetDatabase()
    {
        var source = CreateLocations("source");
        var target = CreateLocations("target");
        await CreateDatabaseAsync(source, "source-value");
        await CreateDatabaseAsync(target, "target-value");
        var manager = CreateManager(source);
        await manager.InitializeAsync();
        await manager.StageChangeAsync(new StorageLocationChangeRequest(target, true, false, true));

        var restarted = CreateManager(source);
        var actual = await restarted.InitializeAsync();

        Assert.Equal(target, actual);
        Assert.Equal("source-value", await ReadMarkerAsync(target));
        Assert.False(File.Exists(DatabasePath(source)));
    }

    [Fact]
    public async Task InitializeAsync_WhenSourceDatabaseIsCorrupt_PreservesTargetAndRollsBack()
    {
        var source = CreateLocations("source");
        var target = CreateLocations("target");
        Directory.CreateDirectory(source.DataDirectory);
        await File.WriteAllTextAsync(DatabasePath(source), "not-a-database");
        await CreateDatabaseAsync(target, "target-value");
        var manager = CreateManager(source);
        await manager.InitializeAsync();
        await manager.StageChangeAsync(new StorageLocationChangeRequest(target, true, false, true));

        var restarted = CreateManager(source);
        var actual = await restarted.InitializeAsync();

        Assert.Equal(source, actual);
        Assert.Equal("target-value", await ReadMarkerAsync(target));
        Assert.True(File.Exists(DatabasePath(source)));
    }

    [Fact]
    public async Task InitializeAsync_WhenLogNameExists_PreservesBothFiles()
    {
        var source = CreateLocations("source");
        var target = CreateLocations("target");
        Directory.CreateDirectory(source.LogDirectory);
        Directory.CreateDirectory(target.LogDirectory);
        await File.WriteAllTextAsync(Path.Combine(source.LogDirectory, "app-20260710-090000-000.log"), "source");
        await File.WriteAllTextAsync(Path.Combine(target.LogDirectory, "app-20260710-090000-000.log"), "target");
        var manager = CreateManager(source);
        await manager.InitializeAsync();
        await manager.StageChangeAsync(new StorageLocationChangeRequest(target, false, true, false));

        var restarted = CreateManager(source);
        await restarted.InitializeAsync();

        var logs = Directory.GetFiles(target.LogDirectory, "app-*.log");
        Assert.Equal(2, logs.Length);
        Assert.Contains(logs, path => File.ReadAllText(path) == "source");
        Assert.Contains(logs, path => File.ReadAllText(path) == "target");
    }

    [Fact]
    public async Task InitializeAsync_WithoutLogMigration_StillDiscardsLegacyDailyLogs()
    {
        var source = CreateLocations("source");
        var target = CreateLocations("target");
        Directory.CreateDirectory(source.LogDirectory);
        Directory.CreateDirectory(target.LogDirectory);
        var sourceLegacy = Path.Combine(source.LogDirectory, "app-20260710.log");
        var targetLegacy = Path.Combine(target.LogDirectory, "app-20260709.log");
        var unrelated = Path.Combine(source.LogDirectory, "app-backup.log");
        await File.WriteAllTextAsync(sourceLegacy, "source-legacy");
        await File.WriteAllTextAsync(targetLegacy, "target-legacy");
        await File.WriteAllTextAsync(unrelated, "unrelated");
        var manager = CreateManager(source);
        await manager.InitializeAsync();
        await manager.StageChangeAsync(new StorageLocationChangeRequest(target, false, false, false));

        var restarted = CreateManager(source);
        var actual = await restarted.InitializeAsync();

        Assert.Equal(target, actual);
        Assert.False(File.Exists(sourceLegacy));
        Assert.False(File.Exists(targetLegacy));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public async Task InitializeAsync_WithCorruptLocator_BacksItUpAndFallsBackToDefaults()
    {
        var defaults = CreateLocations("default");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(LocatorPath, "{not-json");

        var manager = CreateManager(defaults);
        var actual = await manager.InitializeAsync();

        Assert.Equal(defaults, actual);
        Assert.Single(Directory.GetFiles(_root, "storage-locations.json.corrupt-*"));
        var result = await manager.ConsumeStartupResultAsync();
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task InitializeAsync_WithVersionOneLocator_UpgradesWithoutLosingActiveLocation()
    {
        var defaults = CreateLocations("default");
        var active = CreateLocations("custom-active");
        await CreateDatabaseAsync(active, "custom-value");
        Directory.CreateDirectory(_root);
        var legacy = new LegacyStorageLocatorDocument
        {
            Active = active,
            PendingCleanup = []
        };
        await File.WriteAllTextAsync(
            LocatorPath,
            JsonSerializer.Serialize(legacy, AppJson.Default));

        var manager = CreateManager(defaults);
        var actual = await manager.InitializeAsync();

        Assert.Equal(active, actual);
        Assert.Equal("custom-value", await ReadMarkerAsync(active));
        Assert.Equal(2, new StorageLocatorStore(LocatorPath, defaults).Load().SchemaVersion);
    }

    [Fact]
    public async Task InitializeAsync_WhenPendingCleanupPointsIntoNewTarget_PreservesDatabaseAcrossRestarts()
    {
        var previous = CreateLocations("previous");
        var target = CreateLocations("target");
        await CreateDatabaseAsync(previous, "previous-value");
        await CreateDatabaseAsync(target, "target-value");
        var store = new StorageLocatorStore(LocatorPath, previous);
        store.Save(new StorageLocatorDocument
        {
            Active = previous,
            Pending = new PendingStorageLocationChange(
                previous,
                target,
                MigrateData: false,
                MigrateLogs: false,
                OverwriteTargetDatabase: false,
                DateTimeOffset.UtcNow),
            PendingCleanup =
            [
                new PendingStorageCleanup(
                    target.DataDirectory,
                    "igolibrary-ex.db",
                    StorageCleanupKind.DatabaseArtifact)
            ]
        });

        var restarted = CreateManager(previous);
        Assert.Equal(target, await restarted.InitializeAsync());
        Assert.Equal("target-value", await ReadMarkerAsync(target));

        var restartedAgain = CreateManager(previous);
        Assert.Equal(target, await restartedAgain.InitializeAsync());
        Assert.Equal("target-value", await ReadMarkerAsync(target));
        Assert.Empty(new StorageLocatorStore(LocatorPath, previous).Load().PendingCleanup);
    }

    [Fact]
    public async Task StageChangeAsync_WhenTargetIsPhysicalAliasOfCurrent_RejectsChangeWithoutDeletingDatabase()
    {
        var current = CreateLocations("current");
        await CreateDatabaseAsync(current, "current-value");
        Directory.CreateDirectory(current.LogDirectory);
        var alias = Path.Combine(_root, "data-alias");
        try
        {
            Directory.CreateSymbolicLink(alias, current.DataDirectory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var manager = CreateManager(current);
        await manager.InitializeAsync();
        var aliasedTarget = new StorageLocations(alias, current.LogDirectory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.StageChangeAsync(new StorageLocationChangeRequest(
                aliasedTarget,
                MigrateData: true,
                MigrateLogs: false,
                OverwriteTargetDatabase: true)));

        Assert.Equal("current-value", await ReadMarkerAsync(current));
    }

    [Fact]
    public async Task InitializeAsync_WithoutMigrationToInvalidDatabase_RollsBackToSource()
    {
        var source = CreateLocations("source");
        var target = CreateLocations("target");
        await CreateDatabaseAsync(source, "source-value");
        Directory.CreateDirectory(target.DataDirectory);
        await File.WriteAllTextAsync(DatabasePath(target), "not-a-database");
        var manager = CreateManager(source);
        await manager.InitializeAsync();
        await manager.StageChangeAsync(new StorageLocationChangeRequest(target, false, false, false));

        var restarted = CreateManager(source);
        var actual = await restarted.InitializeAsync();

        Assert.Equal(source, actual);
        Assert.Equal("source-value", await ReadMarkerAsync(source));
        Assert.Equal("not-a-database", await File.ReadAllTextAsync(DatabasePath(target)));
        var result = await restarted.ConsumeStartupResultAsync();
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains("现有数据库无效", result.Message);
    }

    [Fact]
    public async Task InspectTargetDatabaseAsync_DistinguishesMissingValidAndInvalidDatabases()
    {
        var defaults = CreateLocations("default");
        var valid = CreateLocations("valid");
        var invalid = CreateLocations("invalid");
        await CreateDatabaseAsync(valid, "valid-value");
        Directory.CreateDirectory(invalid.DataDirectory);
        await File.WriteAllTextAsync(DatabasePath(invalid), "not-a-database");
        var manager = CreateManager(defaults);

        var missingResult = await manager.InspectTargetDatabaseAsync(defaults.DataDirectory);
        var validResult = await manager.InspectTargetDatabaseAsync(valid.DataDirectory);
        var invalidResult = await manager.InspectTargetDatabaseAsync(invalid.DataDirectory);

        Assert.False(missingResult.Exists);
        Assert.True(missingResult.IsValid);
        Assert.True(validResult.Exists);
        Assert.True(validResult.IsValid);
        Assert.True(invalidResult.Exists);
        Assert.False(invalidResult.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(invalidResult.FailureMessage));
    }

    [Fact]
    public async Task InitializeAsync_WhenSavedLocationIsUnavailable_UsesRecoveryWithoutForgettingSavedLocation()
    {
        var recovery = CreateLocations("recovery");
        Directory.CreateDirectory(_root);
        var blockingFile = Path.Combine(_root, "not-a-directory");
        await File.WriteAllTextAsync(blockingFile, "blocker");
        var unavailable = new StorageLocations(
            Path.Combine(blockingFile, "data"),
            Path.Combine(blockingFile, "logs"));
        var store = new StorageLocatorStore(LocatorPath, recovery);
        store.Save(new StorageLocatorDocument { Active = unavailable });
        var manager = new StorageLocationManager(LocatorPath, recovery, recovery);

        var actual = await manager.InitializeAsync();

        Assert.Equal(recovery, actual);
        Assert.Equal(unavailable, new StorageLocatorStore(LocatorPath, recovery).Load().Active);
        var result = await manager.ConsumeStartupResultAsync();
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains("临时使用平台默认目录", result.Message);
        Assert.Equal(unavailable, new StorageLocatorStore(LocatorPath, recovery).Load().Active);
    }

    [Fact]
    public async Task ValidateAsync_RejectsFileSystemRoot()
    {
        var defaults = CreateLocations("default");
        var manager = CreateManager(defaults);
        var root = Path.GetPathRoot(defaults.DataDirectory)!;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.ValidateAsync(new StorageLocations(root, defaults.LogDirectory)));
    }

    [Fact]
    public void StorageLocationDefaults_HonorsLegacyEnvironmentOverride()
    {
        var previous = Environment.GetEnvironmentVariable(StorageLocationDefaults.DataDirectoryEnvironmentVariable);
        var overridden = Path.Combine(_root, "environment-override");
        try
        {
            Environment.SetEnvironmentVariable(StorageLocationDefaults.DataDirectoryEnvironmentVariable, overridden);

            var defaults = StorageLocationDefaults.GetDefaults();

            Assert.Equal(Path.GetFullPath(overridden), defaults.DataDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(overridden), "logs"), defaults.LogDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(StorageLocationDefaults.DataDirectoryEnvironmentVariable, previous);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string LocatorPath => Path.Combine(_root, "storage-locations.json");

    private StorageLocationManager CreateManager(StorageLocations defaults)
        => new(LocatorPath, defaults);

    private StorageLocations CreateLocations(string name)
    {
        var data = Path.Combine(_root, name, "data");
        return new StorageLocations(data, Path.Combine(_root, name, "logs"));
    }

    private static string DatabasePath(StorageLocations locations)
        => Path.Combine(locations.DataDirectory, "igolibrary-ex.db");

    private static async Task CreateDatabaseAsync(StorageLocations locations, string marker)
    {
        var factory = new SqliteConnectionFactory(locations);
        await new SqliteAppDataInitializer(factory).InitializeAsync();
        await using var connection = factory.Create();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO Settings(Key, Value) VALUES('test-marker', $value);";
        command.Parameters.AddWithValue("$value", marker);
        await command.ExecuteNonQueryAsync();
        SqliteConnection.ClearAllPools();
    }

    private static async Task<string> ReadMarkerAsync(StorageLocations locations)
    {
        var factory = new SqliteConnectionFactory(locations);
        await using var connection = factory.Create();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = 'test-marker';";
        var value = Assert.IsType<string>(await command.ExecuteScalarAsync());
        SqliteConnection.ClearAllPools();
        return value;
    }
}

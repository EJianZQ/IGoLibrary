using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Tests;

public sealed class BackupDatabaseValidationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StrictValidation_AcceptsInitializedApplicationDatabase()
    {
        var path = await CreateDatabaseAsync();

        StorageDatabaseValidator.ValidateBackup(path);
    }

    [Fact]
    public async Task StrictValidation_RejectsFutureSchemaVersion()
    {
        var path = await CreateDatabaseAsync();
        await ExecuteAsync(path, $"PRAGMA user_version = {AppDatabaseSchema.CurrentVersion + 1};");

        var error = Assert.Throws<InvalidDataException>(() => StorageDatabaseValidator.ValidateBackup(path));

        Assert.Contains("高于当前支持", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StrictValidation_RejectsWrongApplicationIdAndMissingTables()
    {
        var wrongId = await CreateDatabaseAsync("wrong-id.db");
        await ExecuteAsync(wrongId, "PRAGMA application_id = 123;");
        Assert.Throws<InvalidDataException>(() => StorageDatabaseValidator.ValidateBackup(wrongId));

        var missingTable = await CreateDatabaseAsync("missing.db");
        await ExecuteAsync(missingTable, "DROP TABLE SeatLabels;");
        Assert.Throws<InvalidDataException>(() => StorageDatabaseValidator.ValidateBackup(missingTable));
    }

    [Fact]
    public async Task Initializer_RejectsFutureSchemaWithoutDowngradingItsVersion()
    {
        var locations = new StorageLocations(
            Path.Combine(_directory, "future-initializer"),
            Path.Combine(_directory, "logs"));
        var factory = new SqliteConnectionFactory(locations);
        var initializer = new SqliteAppDataInitializer(factory);
        await initializer.InitializeAsync();
        var futureVersion = AppDatabaseSchema.CurrentVersion + 1;
        await ExecuteAsync(factory.DatabasePath, $"PRAGMA user_version = {futureVersion};");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => initializer.InitializeAsync());

        Assert.Contains("高于当前支持", error.Message, StringComparison.Ordinal);
        Assert.Equal(futureVersion, await ReadPragmaAsync(factory.DatabasePath, "user_version"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<string> CreateDatabaseAsync(string fileName = "data.db")
    {
        var data = Path.Combine(_directory, Path.GetFileNameWithoutExtension(fileName));
        var locations = new StorageLocations(data, Path.Combine(_directory, "logs"));
        var factory = new SqliteConnectionFactory(locations);
        await new SqliteAppDataInitializer(factory).InitializeAsync();
        if (Path.GetFileName(factory.DatabasePath) == fileName)
        {
            return factory.DatabasePath;
        }

        var target = Path.Combine(_directory, fileName);
        File.Copy(factory.DatabasePath, target);
        return target;
    }

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReadPragmaAsync(string path, string name)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}

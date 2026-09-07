using System.Text.Json;
using System.Text.Json.Nodes;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Infrastructure.DataTransfer;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Tests;

public sealed class GlobalLeakBlacklistPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "IGoLibrary.Ex.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Blacklist_RoundTrips_AndSurvivesLegacyMigration(bool migrateLegacy)
    {
        var factory = await CreateDatabaseAsync();
        var repository = new SqliteSettingsRepository(factory, new DefaultAppSettingsDefaults());
        await repository.SaveAsync(AppSettings.Default with { Tasks = AppSettings.Default.Tasks with
        {
            GlobalLeak = new GlobalLeakTaskSettings { BlacklistedSeats =
            [new(2, "a", "001"), new(1, "A", "002"), new(1, "a", "003"), new(1, "a", "重复")] }
        }});
        if (migrateLegacy)
        {
            var json = JsonNode.Parse(await ReadJsonAsync(factory))!;
            json["themeMode"] = 0;
            await WriteJsonAsync(factory, json.ToJsonString());
        }
        var loaded = await repository.LoadAsync();
        Assert.Equal([(1, "A"), (1, "a"), (2, "a")], loaded.Tasks.GlobalLeak.BlacklistedSeats
            .Select(static seat => (seat.LibraryId, seat.SeatKey)));
        await repository.SaveAsync(loaded);
        var reopened = new SqliteSettingsRepository(factory, new DefaultAppSettingsDefaults());
        Assert.Equal(loaded.Tasks.GlobalLeak.BlacklistedSeats, (await reopened.LoadAsync()).Tasks.GlobalLeak.BlacklistedSeats);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"tasks\":{\"globalLeak\":{\"blacklistedSeats\":null}}}")]
    public async Task OldSettings_LoadWithEmptyBlacklist(string json)
    {
        var factory = await CreateDatabaseAsync();
        await WriteJsonAsync(factory, json);
        var loaded = await new SqliteSettingsRepository(factory, new DefaultAppSettingsDefaults()).LoadAsync();
        Assert.Empty(loaded.Tasks.GlobalLeak.BlacklistedSeats);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("\"broken\"")]
    [InlineData("[{\"libraryId\":1,\"seatKey\":{}}]")]
    public async Task MalformedBlacklist_ThrowsInsteadOfDisablingProtection(string invalid)
    {
        var factory = await CreateDatabaseAsync();
        await WriteJsonAsync(factory, "{\"tasks\":{\"globalLeak\":{\"blacklistedSeats\":" + invalid + "}}}");
        await Assert.ThrowsAsync<JsonException>(() => new SqliteSettingsRepository(factory, new DefaultAppSettingsDefaults()).LoadAsync());
    }

    [Fact]
    public async Task InvalidEntries_AreNormalized_WithoutRewritingSeatKeys()
    {
        var factory = await CreateDatabaseAsync();
        await WriteJsonAsync(factory, """{"tasks":{"globalLeak":{"blacklistedSeats":[null,{"libraryId":0,"seatKey":"a"},{"libraryId":1,"seatKey":" "},{"libraryId":1,"seatKey":" a ","seatName":null}]}}}""");
        var loaded = await new SqliteSettingsRepository(factory, new DefaultAppSettingsDefaults()).LoadAsync();
        var seat = Assert.Single(loaded.Tasks.GlobalLeak.BlacklistedSeats);
        Assert.Equal(" a ", seat.SeatKey);
        Assert.Equal(" a ", seat.SeatName);
    }

    [Fact]
    public async Task SettingsWrite_TracksChange_AndBackupInventoryIncludesBlacklist()
    {
        var factory = await CreateDatabaseAsync();
        var tracker = new FakePersistentDataChangeTracker();
        var repository = new SqliteSettingsRepository(factory, new DefaultAppSettingsDefaults(), tracker);
        await repository.SaveAsync(AppSettings.Default);
        var locations = new StorageLocations(_directory, Path.Combine(_directory, "logs"));
        var before = await BackupInventoryReader.ReadAsync(factory.DatabasePath, new(null, null, null), CancellationToken.None);
        var settings = new SettingsService(repository, tracker);
        using var gate = new GlobalLeakConfigurationGate();
        var blacklist = new GlobalLeakSeatBlacklistService(settings, new FakeGlobalLeakCoordinator(), gate,
            new ActivityLogService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalLeakSeatBlacklistService>.Instance);
        var version = tracker.Version;
        await blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<IGoLibrary.Ex.Domain.Models.SeatReference>> { [1] = [new("a", "1")] });
        Assert.True(tracker.Version > version);
        var after = await BackupInventoryReader.ReadAsync(factory.DatabasePath, new(null, null, null), CancellationToken.None);
        Assert.NotEqual(before.Categories["任务配置"].Fingerprint, after.Categories["任务配置"].Fingerprint);
        Assert.Contains("blacklistedSeats", await ReadJsonAsync(factory));
    }

    private async Task<SqliteConnectionFactory> CreateDatabaseAsync()
    {
        var factory = new SqliteConnectionFactory(new StorageLocations(_directory, Path.Combine(_directory, "logs")));
        await new SqliteAppDataInitializer(factory).InitializeAsync();
        return factory;
    }
    private static async Task WriteJsonAsync(SqliteConnectionFactory factory, string json)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO Settings(Key, Value) VALUES('app-settings', $json)";
        command.Parameters.AddWithValue("$json", json);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<string> ReadJsonAsync(SqliteConnectionFactory factory)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key='app-settings'";
        return (string)(await command.ExecuteScalarAsync())!;
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

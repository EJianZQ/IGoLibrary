using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Tests;

public sealed class SqliteSeatLabelRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-SeatLabelTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Initializer_UpgradesLegacyDatabaseWithoutChangingExistingData()
    {
        var factory = CreateFactory();
        await using (var connection = factory.Create())
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                CREATE TABLE Favorites (
                    LibraryId INTEGER NOT NULL,
                    SeatKey TEXT NOT NULL,
                    SeatName TEXT NOT NULL,
                    PRIMARY KEY (LibraryId, SeatKey)
                );
                CREATE TABLE ProtocolOverrides (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                INSERT INTO Favorites(LibraryId, SeatKey, SeatName) VALUES(7, 'seat-1', '1');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new SqliteAppDataInitializer(factory).InitializeAsync();
        var repository = new SqliteSeatLabelRepository(factory);
        await repository.SetLabelsAsync(7, [new SeatLabel("seat-1", "1", "靠窗")]);

        await using var verification = factory.Create();
        await verification.OpenAsync();
        var favoriteCommand = verification.CreateCommand();
        favoriteCommand.CommandText = "SELECT SeatName FROM Favorites WHERE LibraryId = 7 AND SeatKey = 'seat-1';";
        Assert.Equal("1", await favoriteCommand.ExecuteScalarAsync());
        Assert.Equal("靠窗", Assert.Single(await repository.GetLabelsAsync(7)).Text);
    }

    [Fact]
    public async Task Repository_UpsertsDeletesAndIsolatesLibraries()
    {
        var factory = CreateFactory();
        await new SqliteAppDataInitializer(factory).InitializeAsync();
        var repository = new SqliteSeatLabelRepository(factory);

        await repository.SetLabelsAsync(7,
        [
            new SeatLabel("seat-1", "1", "靠窗 '安静'"),
            new SeatLabel("seat-2", "2", "常用")
        ]);
        await repository.SetLabelsAsync(8, [new SeatLabel("seat-1", "1", "另一个场馆")]);
        await repository.SetLabelsAsync(7, [new SeatLabel("seat-1", "01", "已更新")]);
        await repository.DeleteLabelsAsync(7, ["seat-2", "seat-2", "missing"]);
        await repository.DeleteLabelsAsync(7, ["missing"]);

        var librarySeven = Assert.Single(await repository.GetLabelsAsync(7));
        Assert.Equal(new SeatLabel("seat-1", "01", "已更新"), librarySeven);
        Assert.Equal("另一个场馆", Assert.Single(await repository.GetLabelsAsync(8)).Text);
    }

    [Fact]
    public async Task SetLabelsAsync_RollsBackWholeBatchWhenAnyWriteFails()
    {
        var factory = CreateFactory();
        await new SqliteAppDataInitializer(factory).InitializeAsync();
        var repository = new SqliteSeatLabelRepository(factory);

        await Assert.ThrowsAnyAsync<Exception>(() => repository.SetLabelsAsync(
            7,
            [
                new SeatLabel("seat-1", "1", "有效"),
                new SeatLabel("seat-2", null!, "无效")
            ]));

        Assert.Empty(await repository.GetLabelsAsync(7));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private SqliteConnectionFactory CreateFactory()
    {
        return new SqliteConnectionFactory(new StorageLocations(
            Path.Combine(_root, "data"),
            Path.Combine(_root, "logs")));
    }
}

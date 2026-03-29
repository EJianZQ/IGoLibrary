using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

public sealed class SqliteAppDataInitializer(SqliteConnectionFactory connectionFactory) : IAppDataInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppDataPaths.RootDirectory);

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Favorites (
                LibraryId INTEGER NOT NULL,
                SeatKey TEXT NOT NULL,
                SeatName TEXT NOT NULL,
                PRIMARY KEY (LibraryId, SeatKey)
            );

            CREATE TABLE IF NOT EXISTS ProtocolOverrides (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

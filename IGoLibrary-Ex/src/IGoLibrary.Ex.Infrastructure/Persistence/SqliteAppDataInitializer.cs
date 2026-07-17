using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

public sealed class SqliteAppDataInitializer(SqliteConnectionFactory connectionFactory) : IAppDataInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(connectionFactory.Locations.DataDirectory);
        Directory.CreateDirectory(connectionFactory.Locations.LogDirectory);

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

            CREATE TABLE IF NOT EXISTS SeatLabels (
                LibraryId INTEGER NOT NULL,
                SeatKey TEXT NOT NULL,
                SeatName TEXT NOT NULL,
                LabelText TEXT NOT NULL,
                PRIMARY KEY (LibraryId, SeatKey)
            );

            CREATE TABLE IF NOT EXISTS ProtocolOverrides (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS MobileTaskLaunchHistory (
                SequenceId INTEGER PRIMARY KEY AUTOINCREMENT,
                RecordId TEXT NOT NULL UNIQUE,
                TaskKind TEXT NOT NULL,
                Fingerprint TEXT NOT NULL,
                RecordedAtUtc TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                UNIQUE(TaskKind, Fingerprint)
            );

            CREATE INDEX IF NOT EXISTS IX_MobileTaskLaunchHistory_TaskKind_SequenceId
                ON MobileTaskLaunchHistory(TaskKind, SequenceId DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

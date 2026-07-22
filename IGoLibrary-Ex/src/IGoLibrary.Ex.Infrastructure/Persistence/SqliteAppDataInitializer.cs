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

        var applicationId = await ReadPragmaAsync(connection, "application_id", cancellationToken);
        var schemaVersion = await ReadPragmaAsync(connection, "user_version", cancellationToken);
        if (applicationId is not (0 or AppDatabaseSchema.ApplicationId))
        {
            throw new InvalidDataException("数据库不属于 IGoLibrary-Ex，已拒绝修改其结构");
        }

        if (schemaVersion > AppDatabaseSchema.CurrentVersion)
        {
            throw new InvalidDataException(
                $"数据库版本 {schemaVersion} 高于当前支持的版本 {AppDatabaseSchema.CurrentVersion}");
        }

        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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

        if (applicationId != AppDatabaseSchema.ApplicationId ||
            schemaVersion != AppDatabaseSchema.CurrentVersion)
        {
            command.CommandText =
                $"""
                PRAGMA application_id = {AppDatabaseSchema.ApplicationId};
                PRAGMA user_version = {AppDatabaseSchema.CurrentVersion};
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    private static async Task<int> ReadPragmaAsync(
        SqliteConnection connection,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}

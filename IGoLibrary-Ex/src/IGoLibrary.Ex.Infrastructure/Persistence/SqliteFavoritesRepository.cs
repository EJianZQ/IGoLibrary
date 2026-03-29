using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

public sealed class SqliteFavoritesRepository(SqliteConnectionFactory connectionFactory) : IFavoritesRepository
{
    public async Task<IReadOnlyList<TrackedSeat>> GetFavoritesAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        var results = new List<TrackedSeat>();

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT SeatKey, SeatName FROM Favorites WHERE LibraryId = $libraryId ORDER BY SeatName;";
        command.Parameters.AddWithValue("$libraryId", libraryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TrackedSeat(
                reader.GetString(0),
                reader.GetString(1)));
        }

        return results;
    }

    public async Task SaveFavoritesAsync(int libraryId, IReadOnlyList<TrackedSeat> seats, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM Favorites WHERE LibraryId = $libraryId;";
        deleteCommand.Parameters.AddWithValue("$libraryId", libraryId);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var seat in seats.DistinctBy(x => x.SeatKey))
        {
            var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO Favorites(LibraryId, SeatKey, SeatName)
                VALUES($libraryId, $seatKey, $seatName);
                """;
            insertCommand.Parameters.AddWithValue("$libraryId", libraryId);
            insertCommand.Parameters.AddWithValue("$seatKey", seat.SeatKey);
            insertCommand.Parameters.AddWithValue("$seatName", seat.SeatName);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

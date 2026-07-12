using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

public sealed class SqliteSeatLabelRepository(SqliteConnectionFactory connectionFactory) : ISeatLabelRepository
{
    public async Task<IReadOnlyList<SeatLabel>> GetLabelsAsync(
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SeatLabel>();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT SeatKey, SeatName, LabelText FROM SeatLabels WHERE LibraryId = $libraryId ORDER BY SeatName;";
        command.Parameters.AddWithValue("$libraryId", libraryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SeatLabel(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return results;
    }

    public async Task SetLabelsAsync(
        int libraryId,
        IReadOnlyList<SeatLabel> labels,
        CancellationToken cancellationToken = default)
    {
        if (labels.Count == 0)
        {
            return;
        }

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var label in labels.DistinctBy(static label => label.SeatKey, StringComparer.Ordinal))
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO SeatLabels(LibraryId, SeatKey, SeatName, LabelText)
                VALUES($libraryId, $seatKey, $seatName, $labelText)
                ON CONFLICT(LibraryId, SeatKey) DO UPDATE SET
                    SeatName = excluded.SeatName,
                    LabelText = excluded.LabelText;
                """;
            command.Parameters.AddWithValue("$libraryId", libraryId);
            command.Parameters.AddWithValue("$seatKey", label.SeatKey);
            command.Parameters.AddWithValue("$seatName", label.SeatName);
            command.Parameters.AddWithValue("$labelText", label.Text);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteLabelsAsync(
        int libraryId,
        IReadOnlyList<string> seatKeys,
        CancellationToken cancellationToken = default)
    {
        if (seatKeys.Count == 0)
        {
            return;
        }

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var seatKey in seatKeys.Distinct(StringComparer.Ordinal))
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "DELETE FROM SeatLabels WHERE LibraryId = $libraryId AND SeatKey = $seatKey;";
            command.Parameters.AddWithValue("$libraryId", libraryId);
            command.Parameters.AddWithValue("$seatKey", seatKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

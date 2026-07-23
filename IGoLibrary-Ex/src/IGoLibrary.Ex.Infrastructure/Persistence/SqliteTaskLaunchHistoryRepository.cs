using System.Globalization;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

public sealed class SqliteTaskLaunchHistoryRepository(
    SqliteConnectionFactory connectionFactory,
    ILogger<SqliteTaskLaunchHistoryRepository> logger,
    IPersistentDataChangeTracker? changeTracker = null) : ITaskLaunchHistoryRepository
{
    private const int MaximumRecordsPerKind = 5;
    private const string GrabKind = "grab";
    private const string GlobalLeakKind = "globalLeak";

    public Task<IReadOnlyList<GrabTaskLaunchRecord>> GetRecentGrabAsync(
        CancellationToken cancellationToken = default)
    {
        return LoadGrabAsync(recordId: null, cancellationToken);
    }

    public Task<IReadOnlyList<GlobalLeakTaskLaunchRecord>> GetRecentGlobalLeakAsync(
        CancellationToken cancellationToken = default)
    {
        return LoadGlobalLeakAsync(recordId: null, cancellationToken);
    }

    public async Task<GrabTaskLaunchRecord?> GetGrabAsync(
        string recordId,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidRecordId(recordId))
        {
            return null;
        }

        return (await LoadGrabAsync(recordId, cancellationToken)).SingleOrDefault();
    }

    public async Task<GlobalLeakTaskLaunchRecord?> GetGlobalLeakAsync(
        string recordId,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidRecordId(recordId))
        {
            return null;
        }

        return (await LoadGlobalLeakAsync(recordId, cancellationToken)).SingleOrDefault();
    }

    public Task<TaskLaunchHistorySaveResult> SaveGrabAsync(
        GrabTaskLaunchRecord record,
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return SaveAsync(
            GrabKind,
            record.RecordId,
            fingerprint,
            record.RecordedAtUtc,
            TaskLaunchHistoryPayloadCodec.Serialize(record),
            cancellationToken);
    }

    public Task<TaskLaunchHistorySaveResult> SaveGlobalLeakAsync(
        GlobalLeakTaskLaunchRecord record,
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return SaveAsync(
            GlobalLeakKind,
            record.RecordId,
            fingerprint,
            record.RecordedAtUtc,
            TaskLaunchHistoryPayloadCodec.Serialize(record),
            cancellationToken);
    }

    private async Task<TaskLaunchHistorySaveResult> SaveAsync(
        string taskKind,
        string candidateRecordId,
        string fingerprint,
        DateTimeOffset recordedAtUtc,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        if (!IsValidRecordId(candidateRecordId))
        {
            throw new ArgumentException("记录 ID 格式无效", nameof(candidateRecordId));
        }

        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            throw new ArgumentException("记录指纹不能为空", nameof(fingerprint));
        }

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var existingRecordId = await FindExistingRecordIdAsync(
            connection,
            transaction,
            taskKind,
            fingerprint,
            cancellationToken);
        var recordId = existingRecordId ?? candidateRecordId;

        if (existingRecordId is not null)
        {
            await DeleteExistingAsync(connection, transaction, taskKind, fingerprint, cancellationToken);
        }

        await InsertAsync(
            connection,
            transaction,
            taskKind,
            recordId,
            fingerprint,
            recordedAtUtc,
            payloadJson,
            cancellationToken);
        var prunedCount = await PruneAsync(connection, transaction, taskKind, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        changeTracker?.MarkChanged();

        logger.LogInformation(
            "已保存手机端任务启动历史。任务类型={TaskKind}，记录 ID={RecordId}，是否刷新={Refreshed}，清理数量={PrunedCount}。",
            taskKind,
            recordId,
            existingRecordId is not null,
            prunedCount);
        return new TaskLaunchHistorySaveResult(recordId, existingRecordId is not null, prunedCount);
    }

    private async Task<IReadOnlyList<GrabTaskLaunchRecord>> LoadGrabAsync(
        string? recordId,
        CancellationToken cancellationToken)
    {
        var rows = await LoadRowsAsync(GrabKind, recordId, cancellationToken);
        var records = new List<GrabTaskLaunchRecord>(rows.Count);
        foreach (var row in rows)
        {
            if (TaskLaunchHistoryPayloadCodec.TryDeserializeGrab(
                    row.PayloadJson,
                    row.RecordId,
                    row.RecordedAtUtc,
                    out var record,
                    out var error))
            {
                records.Add(record!);
                continue;
            }

            LogInvalidRow(row, error);
        }

        return records;
    }

    private async Task<IReadOnlyList<GlobalLeakTaskLaunchRecord>> LoadGlobalLeakAsync(
        string? recordId,
        CancellationToken cancellationToken)
    {
        var rows = await LoadRowsAsync(GlobalLeakKind, recordId, cancellationToken);
        var records = new List<GlobalLeakTaskLaunchRecord>(rows.Count);
        foreach (var row in rows)
        {
            if (TaskLaunchHistoryPayloadCodec.TryDeserializeGlobalLeak(
                    row.PayloadJson,
                    row.RecordId,
                    row.RecordedAtUtc,
                    out var record,
                    out var error))
            {
                records.Add(record!);
                continue;
            }

            LogInvalidRow(row, error);
        }

        return records;
    }

    private async Task<IReadOnlyList<HistoryRow>> LoadRowsAsync(
        string taskKind,
        string? recordId,
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = recordId is null
            ? """
              SELECT RecordId, RecordedAtUtc, PayloadJson
              FROM MobileTaskLaunchHistory
              WHERE TaskKind = $taskKind
              ORDER BY SequenceId DESC
              LIMIT $limit;
              """
            : """
              SELECT RecordId, RecordedAtUtc, PayloadJson
              FROM MobileTaskLaunchHistory
              WHERE TaskKind = $taskKind AND RecordId = $recordId
              LIMIT 1;
              """;
        command.Parameters.AddWithValue("$taskKind", taskKind);
        if (recordId is null)
        {
            command.Parameters.AddWithValue("$limit", MaximumRecordsPerKind);
        }
        else
        {
            command.Parameters.AddWithValue("$recordId", recordId);
        }

        var rows = new List<HistoryRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var storedRecordId = reader.GetString(0);
            var recordedAtText = reader.GetString(1);
            if (!IsValidRecordId(storedRecordId) ||
                !DateTimeOffset.TryParse(
                    recordedAtText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var recordedAtUtc))
            {
                logger.LogWarning(
                    "已跳过无效的手机端任务启动历史元数据。任务类型={TaskKind}，记录 ID={RecordId}。",
                    taskKind,
                    storedRecordId);
                continue;
            }

            rows.Add(new HistoryRow(taskKind, storedRecordId, recordedAtUtc.ToUniversalTime(), reader.GetString(2)));
        }

        logger.LogInformation(
            "已加载手机端任务启动历史。任务类型={TaskKind}，请求的记录 ID={RequestedRecordId}，数量={Count}。",
            taskKind,
            recordId,
            rows.Count);
        return rows;
    }

    private void LogInvalidRow(HistoryRow row, string error)
    {
        logger.LogWarning(
            "已跳过无效的手机端任务启动历史载荷。任务类型={TaskKind}，记录 ID={RecordId}，错误={Error}。",
            row.TaskKind,
            row.RecordId,
            error);
    }

    private static async Task<string?> FindExistingRecordIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string taskKind,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT RecordId FROM MobileTaskLaunchHistory WHERE TaskKind = $taskKind AND Fingerprint = $fingerprint LIMIT 1;";
        command.Parameters.AddWithValue("$taskKind", taskKind);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task DeleteExistingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string taskKind,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM MobileTaskLaunchHistory WHERE TaskKind = $taskKind AND Fingerprint = $fingerprint;";
        command.Parameters.AddWithValue("$taskKind", taskKind);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string taskKind,
        string recordId,
        string fingerprint,
        DateTimeOffset recordedAtUtc,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO MobileTaskLaunchHistory(RecordId, TaskKind, Fingerprint, RecordedAtUtc, PayloadJson)
            VALUES($recordId, $taskKind, $fingerprint, $recordedAtUtc, $payloadJson);
            """;
        command.Parameters.AddWithValue("$recordId", recordId);
        command.Parameters.AddWithValue("$taskKind", taskKind);
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue("$recordedAtUtc", recordedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$payloadJson", payloadJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> PruneAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string taskKind,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM MobileTaskLaunchHistory
            WHERE TaskKind = $taskKind
              AND SequenceId NOT IN (
                  SELECT SequenceId
                  FROM MobileTaskLaunchHistory
                  WHERE TaskKind = $taskKind
                  ORDER BY SequenceId DESC
                  LIMIT $limit
              );
            """;
        command.Parameters.AddWithValue("$taskKind", taskKind);
        command.Parameters.AddWithValue("$limit", MaximumRecordsPerKind);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool IsValidRecordId(string? recordId)
    {
        return recordId is { Length: 32 } && Guid.TryParseExact(recordId, "N", out _);
    }

    private sealed record HistoryRow(
        string TaskKind,
        string RecordId,
        DateTimeOffset RecordedAtUtc,
        string PayloadJson);
}

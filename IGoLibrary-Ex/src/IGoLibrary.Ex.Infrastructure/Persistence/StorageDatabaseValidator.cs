using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

internal static class StorageDatabaseValidator
{
    public static StorageTargetDatabaseInspection Inspect(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return new StorageTargetDatabaseInspection(false, true, null);
        }

        try
        {
            Validate(databasePath);
            return new StorageTargetDatabaseInspection(true, true, null);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new StorageTargetDatabaseInspection(true, false, ex.Message);
        }
    }

    public static void Validate(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var quickCheck = connection.CreateCommand();
        quickCheck.CommandText = "PRAGMA quick_check;";
        if (!string.Equals(quickCheck.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("数据库完整性检查失败。");
        }

        using var schemaCheck = connection.CreateCommand();
        schemaCheck.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Settings', 'Favorites', 'ProtocolOverrides');";
        if (Convert.ToInt32(schemaCheck.ExecuteScalar()) != 3)
        {
            throw new InvalidDataException("数据库缺少必要的数据表。");
        }
    }
}

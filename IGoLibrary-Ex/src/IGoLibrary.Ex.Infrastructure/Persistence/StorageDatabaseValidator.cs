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
        => Validate(databasePath, allowLegacyApplicationId: true);

    public static void ValidateBackup(string databasePath)
        => Validate(databasePath, allowLegacyApplicationId: false);

    private static void Validate(string databasePath, bool allowLegacyApplicationId)
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
            throw new InvalidDataException("数据库完整性检查失败");
        }

        using var applicationIdCheck = connection.CreateCommand();
        applicationIdCheck.CommandText = "PRAGMA application_id;";
        var applicationId = Convert.ToInt32(applicationIdCheck.ExecuteScalar());
        if (applicationId != AppDatabaseSchema.ApplicationId &&
            (!allowLegacyApplicationId || applicationId != 0))
        {
            throw new InvalidDataException("数据库不属于 IGoLibrary-Ex");
        }

        using var versionCheck = connection.CreateCommand();
        versionCheck.CommandText = "PRAGMA user_version;";
        var schemaVersion = Convert.ToInt32(versionCheck.ExecuteScalar());
        if (schemaVersion > AppDatabaseSchema.CurrentVersion)
        {
            throw new InvalidDataException(
                $"数据库版本 {schemaVersion} 高于当前支持的版本 {AppDatabaseSchema.CurrentVersion}");
        }

        using var schemaCheck = connection.CreateCommand();
        var tableParameters = AppDatabaseSchema.RequiredTables
            .Select((_, index) => $"$table{index}")
            .ToArray();
        schemaCheck.CommandText =
            $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ({string.Join(',', tableParameters)});";
        for (var index = 0; index < AppDatabaseSchema.RequiredTables.Length; index++)
        {
            schemaCheck.Parameters.AddWithValue(tableParameters[index], AppDatabaseSchema.RequiredTables[index]);
        }

        if (Convert.ToInt32(schemaCheck.ExecuteScalar()) != AppDatabaseSchema.RequiredTables.Length)
        {
            throw new InvalidDataException("数据库缺少必要的数据表");
        }
    }
}

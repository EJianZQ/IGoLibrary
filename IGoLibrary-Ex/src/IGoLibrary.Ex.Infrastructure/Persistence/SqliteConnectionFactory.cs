using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString = $"Data Source={AppDataPaths.DatabasePath}";

    public SqliteConnection Create()
    {
        Directory.CreateDirectory(AppDataPaths.RootDirectory);
        return new SqliteConnection(_connectionString);
    }
}

using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory()
        : this(StorageLocationDefaults.GetDefaults())
    {
    }

    public SqliteConnectionFactory(StorageLocations locations)
    {
        Locations = locations;
        DatabasePath = Path.Combine(locations.DataDirectory, StorageLocationDefaults.DatabaseFileName);
        _connectionString = $"Data Source={DatabasePath}";
    }

    public StorageLocations Locations { get; }

    public string DatabasePath { get; }

    public SqliteConnection Create()
    {
        Directory.CreateDirectory(Locations.DataDirectory);
        return new SqliteConnection(_connectionString);
    }
}

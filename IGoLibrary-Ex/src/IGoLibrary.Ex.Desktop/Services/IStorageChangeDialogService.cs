namespace IGoLibrary.Ex.Desktop.Services;

public interface IStorageChangeDialogService
{
    Task<StorageMigrationDecision> ConfirmMigrationAsync(
        StorageLocations current,
        StorageLocations target,
        bool dataDirectoryChanged,
        bool logDirectoryChanged,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmOverwriteDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmUseExistingDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmStopTasksAsync(
        IReadOnlyList<string> taskNames,
        CancellationToken cancellationToken = default);
}

public enum StorageMigrationDecision
{
    Cancel,
    Migrate,
    DoNotMigrate
}

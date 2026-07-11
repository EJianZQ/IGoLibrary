namespace IGoLibrary.Ex.Application.Configuration;

public sealed record StorageLocations(
    string DataDirectory,
    string LogDirectory);

public sealed record StorageLocationChangeRequest(
    StorageLocations Target,
    bool MigrateData,
    bool MigrateLogs,
    bool OverwriteTargetDatabase);

public sealed record StorageLocationStartupResult(
    bool Succeeded,
    string Message);

public sealed record StorageTargetDatabaseInspection(
    bool Exists,
    bool IsValid,
    string? FailureMessage);

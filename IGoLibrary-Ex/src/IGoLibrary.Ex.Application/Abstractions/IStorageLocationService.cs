namespace IGoLibrary.Ex.Application.Abstractions;

public interface IStorageLocationService
{
    StorageLocations Current { get; }

    StorageLocations Defaults { get; }

    Task ValidateAsync(StorageLocations locations, CancellationToken cancellationToken = default);

    Task<StorageTargetDatabaseInspection> InspectTargetDatabaseAsync(
        string dataDirectory,
        CancellationToken cancellationToken = default);

    Task StageChangeAsync(
        StorageLocationChangeRequest request,
        CancellationToken cancellationToken = default);

    Task CancelPendingChangeAsync(CancellationToken cancellationToken = default);

    Task<StorageLocationStartupResult?> ConsumeStartupResultAsync(
        CancellationToken cancellationToken = default);
}

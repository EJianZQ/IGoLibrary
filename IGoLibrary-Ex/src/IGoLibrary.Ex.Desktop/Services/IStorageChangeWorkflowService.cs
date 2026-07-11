namespace IGoLibrary.Ex.Desktop.Services;

public interface IStorageChangeWorkflowService
{
    Task<bool> ApplyAsync(StorageLocations target, CancellationToken cancellationToken = default);
}

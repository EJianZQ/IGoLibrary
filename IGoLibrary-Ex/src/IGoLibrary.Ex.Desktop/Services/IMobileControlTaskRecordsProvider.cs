namespace IGoLibrary.Ex.Desktop.Services;

public interface IMobileControlTaskRecordsProvider
{
    Task<MobileControlTaskRecordsSnapshot> CreateSnapshotAsync(
        CancellationToken cancellationToken = default);
}

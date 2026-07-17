namespace IGoLibrary.Ex.Desktop.Services;

public interface IMobileControlTaskStartService
{
    Task<MobileControlActionResult> StartTaskAsync(
        string taskKind,
        string? recordId,
        CancellationToken cancellationToken = default);
}

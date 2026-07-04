namespace IGoLibrary.Ex.Desktop.Services;

public interface IStartupEntryService
{
    bool IsSupported { get; }

    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    Task EnableAsync(CancellationToken cancellationToken = default);

    Task DisableAsync(CancellationToken cancellationToken = default);
}

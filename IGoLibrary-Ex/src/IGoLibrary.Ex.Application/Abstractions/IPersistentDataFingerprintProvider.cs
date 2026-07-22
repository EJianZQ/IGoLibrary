namespace IGoLibrary.Ex.Application.Abstractions;

public interface IPersistentDataFingerprintProvider
{
    Task<string> ComputeAsync(CancellationToken cancellationToken = default);
}

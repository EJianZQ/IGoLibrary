namespace IGoLibrary.Ex.Application.Abstractions;

public interface IBackupSecretStore
{
    bool IsPersistent { get; }

    Task<string?> LoadBackupPasswordAsync(CancellationToken cancellationToken = default);

    Task SaveBackupPasswordAsync(string password, CancellationToken cancellationToken = default);

    Task ClearBackupPasswordAsync(CancellationToken cancellationToken = default);

    Task<string?> LoadPreviousBackupPasswordAsync(CancellationToken cancellationToken = default);

    Task SavePreviousBackupPasswordAsync(string password, CancellationToken cancellationToken = default);

    Task ClearPreviousBackupPasswordAsync(CancellationToken cancellationToken = default);

    Task<string?> LoadWebDavPasswordAsync(CancellationToken cancellationToken = default);

    Task SaveWebDavPasswordAsync(string password, CancellationToken cancellationToken = default);

    Task ClearWebDavPasswordAsync(CancellationToken cancellationToken = default);

    Task<string?> LoadRestoreSecretAsync(string transactionId, CancellationToken cancellationToken = default);

    Task SaveRestoreSecretAsync(
        string transactionId,
        string value,
        CancellationToken cancellationToken = default);

    Task ClearRestoreSecretAsync(string transactionId, CancellationToken cancellationToken = default);
}

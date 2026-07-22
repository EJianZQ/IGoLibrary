using IGoLibrary.Ex.Application.Backup;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IWebDavSyncService
{
    BackupSyncRuntimeStatus Status { get; }

    event EventHandler<BackupSyncRuntimeStatus>? StatusChanged;

    Task ReconcileLocalStateAsync(CancellationToken cancellationToken = default);

    Task RecordRestoredBaselineAsync(
        string semanticFingerprint,
        WebDavRemoteMetadata metadata,
        string expectedEndpointFingerprint,
        string remoteFileSha256,
        CancellationToken cancellationToken = default);

    Task<WebDavRemoteMetadata> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<WebDavUploadResult> UploadAsync(
        bool allowOverwrite,
        CancellationToken cancellationToken = default);

    Task<WebDavDownloadResult> DownloadAsync(CancellationToken cancellationToken = default);

    Task DiscardDownloadAsync(
        string localFilePath,
        CancellationToken cancellationToken = default);
}

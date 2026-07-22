using IGoLibrary.Ex.Application.Backup;

namespace IGoLibrary.Ex.Desktop.Services;

public enum BackupPasswordChangeDecision
{
    Cancel,
    SaveOnly,
    SaveAndUpload
}

public interface IBackupDialogService
{
    Task<string?> RequestPasswordAsync(
        string title,
        string message,
        bool requireConfirmation,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmRestoreAsync(
        PreparedBackup backup,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmInsecureHttpAsync(CancellationToken cancellationToken = default);

    Task<bool> ConfirmSkipTlsVerificationAsync(CancellationToken cancellationToken = default);

    Task<bool> ConfirmRemoteOverwriteAsync(
        WebDavRemoteMetadata? metadata,
        CancellationToken cancellationToken = default);

    Task<BackupPasswordChangeDecision> ConfirmPasswordChangeAsync(
        bool webDavConfigured,
        CancellationToken cancellationToken = default);
}

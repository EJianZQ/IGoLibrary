namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflaredDownloadDialogService
{
    Task<bool> ConfirmDownloadAsync(
        CloudflaredAssetDescriptor asset,
        CancellationToken cancellationToken = default);

    Task<CloudflaredInstallDialogResult> ShowInstallAsync(
        CancellationToken cancellationToken = default);
}

internal sealed record CloudflaredInstallDialogResult(
    CloudflaredInstallDialogOutcome Outcome,
    string Message);

internal enum CloudflaredInstallDialogOutcome
{
    Installed,
    Canceled,
    Failed
}

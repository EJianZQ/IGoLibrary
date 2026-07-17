using System.Runtime.InteropServices;

namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflaredPathProvider
{
    string BundledDirectory { get; }

    string ManagedInstallRoot { get; }

    string DownloadWorkspaceRoot { get; }

    string GetManagedInstallDirectory(CloudflaredAssetDescriptor asset);
}

internal sealed class CloudflaredPathProvider : ICloudflaredPathProvider
{
    public CloudflaredPathProvider()
        : this(
            Path.Combine(AppContext.BaseDirectory, "tools", "cloudflared"),
            Path.Combine(GetPlatformAppDataRoot(), "tools", "cloudflared"),
            Path.Combine(Path.GetTempPath(), "IGoLibrary-Ex", "cloudflared-download"))
    {
    }

    internal CloudflaredPathProvider(
        string bundledDirectory,
        string managedInstallRoot,
        string downloadWorkspaceRoot)
    {
        BundledDirectory = Path.GetFullPath(bundledDirectory);
        ManagedInstallRoot = Path.GetFullPath(managedInstallRoot);
        DownloadWorkspaceRoot = Path.GetFullPath(downloadWorkspaceRoot);
    }

    public string BundledDirectory { get; }

    public string ManagedInstallRoot { get; }

    public string DownloadWorkspaceRoot { get; }

    public string GetManagedInstallDirectory(CloudflaredAssetDescriptor asset)
        => Path.Combine(ManagedInstallRoot, asset.Version, asset.RuntimeIdentifier);

    private static string GetPlatformAppDataRoot()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            baseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                "Library",
                "Application Support");
        }

        return Path.Combine(baseDirectory, "IGoLibrary-Ex");
    }
}

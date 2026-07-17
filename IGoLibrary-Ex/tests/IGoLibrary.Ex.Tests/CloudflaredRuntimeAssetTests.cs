using IGoLibrary.Ex.Desktop.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class CloudflaredRuntimeAssetTests
{
    [Theory]
    [InlineData("win-x64", "cloudflared.exe", "binary", 54168384, 54168384,
        "b11ee950a12b15604e6b0a0f30a226516adc7aec75de2e3c642b28e50ddef9ea",
        "b11ee950a12b15604e6b0a0f30a226516adc7aec75de2e3c642b28e50ddef9ea")]
    [InlineData("osx-x64", "cloudflared", "tgz", 20841929, 41181376,
        "dd1fb6a914a21dc52c64bad96987bbbc72d6c65553a2cfee1dd5bc886742ddfb",
        "c0c65579c6f11b1381cf5ffd1614f5094bf140e18938eae4ad16931da9f69499")]
    [InlineData("osx-arm64", "cloudflared", "tgz", 18957597, 38388400,
        "276f4ae3119c88d1708b0f884a35a1c87d9ae459b0dab6313f2daddbddab2bec",
        "cd33944f6ce65e240942d986932bc96bde8641ecefcd52c1ae5dc21f0bcffb04")]
    public void Catalog_MapsPinnedRuntimeAsset(
        string rid,
        string executableName,
        string archiveType,
        long downloadSize,
        long executableSize,
        string downloadSha256,
        string executableSha256)
    {
        var catalog = new CloudflaredAssetCatalog(
            ReadEmbeddedManifest(),
            rid,
            NullLogger<CloudflaredAssetCatalog>.Instance);

        Assert.Equal("2026.7.0", catalog.Current.Version);
        Assert.Equal(rid, catalog.Current.RuntimeIdentifier);
        Assert.Equal(executableName, catalog.Current.ExecutableName);
        Assert.Equal(archiveType, catalog.Current.ArchiveType);
        Assert.Equal(downloadSize, catalog.Current.DownloadSize);
        Assert.Equal(executableSize, catalog.Current.ExecutableSize);
        Assert.Equal(downloadSha256, catalog.Current.DownloadSha256);
        Assert.Equal(executableSha256, catalog.Current.ExecutableSha256);
        Assert.Equal("github.com", catalog.Current.DownloadUri.Host);
        Assert.Contains("/cloudflare/cloudflared/releases/", catalog.Current.DownloadUri.AbsolutePath);
    }

    [Fact]
    public void Catalog_RejectsUnsupportedRuntime()
    {
        Assert.Throws<PlatformNotSupportedException>(() => new CloudflaredAssetCatalog(
            ReadEmbeddedManifest(),
            "linux-x64",
            NullLogger<CloudflaredAssetCatalog>.Instance));
    }

    [Fact]
    public void Catalog_EmbedsLegalFiles()
    {
        var catalog = new CloudflaredAssetCatalog(
            ReadEmbeddedManifest(),
            "win-x64",
            NullLogger<CloudflaredAssetCatalog>.Instance);

        Assert.NotEmpty(catalog.LicenseBytes);
        Assert.NotEmpty(catalog.NoticesBytes);
        Assert.Contains("Apache License", System.Text.Encoding.UTF8.GetString(catalog.LicenseBytes));
        Assert.Contains("cloudflare/cloudflared", System.Text.Encoding.UTF8.GetString(catalog.NoticesBytes));
    }

    private static string ReadEmbeddedManifest()
    {
        using var stream = typeof(CloudflaredAssetCatalog).Assembly.GetManifestResourceStream(
            CloudflaredAssetCatalog.ManifestResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}

using System.Text.Json;
using IGoLibrary.Ex.Desktop.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class CloudflaredPackagingTests
{
    [Fact]
    public void AssetManifest_PinsExpectedVersionRidsAndSha256Values()
    {
        var root = GetRepositoryRoot();
        var manifestPath = Path.Combine(root.FullName, "build", "cloudflared-assets.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var rootElement = document.RootElement;

        Assert.Equal("2026.7.0", rootElement.GetProperty("version").GetString());
        var assets = rootElement.GetProperty("assets");
        Assert.Equal(["osx-arm64", "osx-x64", "win-x64"], assets.EnumerateObject().Select(x => x.Name).Order().ToArray());
        foreach (var asset in assets.EnumerateObject())
        {
            Assert.Matches("^[0-9a-f]{64}$", asset.Value.GetProperty("sha256").GetString());
            Assert.Matches("^[0-9a-f]{64}$", asset.Value.GetProperty("executableSha256").GetString());
            Assert.False(string.IsNullOrWhiteSpace(asset.Value.GetProperty("fileName").GetString()));
            Assert.True(asset.Value.GetProperty("size").GetInt64() > 0);
            Assert.True(asset.Value.GetProperty("executableSize").GetInt64() > 0);
        }

        Assert.Equal(54168384, assets.GetProperty("win-x64").GetProperty("size").GetInt64());
        Assert.Equal(20841929, assets.GetProperty("osx-x64").GetProperty("size").GetInt64());
        Assert.Equal(18957597, assets.GetProperty("osx-arm64").GetProperty("size").GetInt64());
    }

    [Fact]
    public void VisualStudioDebugBuild_PreparesCloudflaredFromValidatedOfflineCache()
    {
        var root = GetRepositoryRoot().FullName;
        var project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "IGoLibrary.Ex.Desktop",
            "IGoLibrary.Ex.Desktop.csproj"));
        var prepareScript = File.ReadAllText(Path.Combine(root, "build", "prepare-cloudflared.ps1"));

        Assert.Contains("PrepareCloudflaredForVisualStudioDebug", project, StringComparison.Ordinal);
        Assert.Contains("'$(BuildingInsideVisualStudio)' == 'true'", project, StringComparison.Ordinal);
        Assert.Contains("-Runtime win-x64", project, StringComparison.Ordinal);
        Assert.Contains("-Offline", project, StringComparison.Ordinal);
        Assert.Contains("[switch]$Offline", prepareScript, StringComparison.Ordinal);
        Assert.Contains("if ($Offline)", prepareScript, StringComparison.Ordinal);
        Assert.Contains("$asset.size", prepareScript, StringComparison.Ordinal);
        Assert.Contains("$asset.executableSize", prepareScript, StringComparison.Ordinal);
        Assert.Contains("$asset.executableSha256", prepareScript, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopBuild_EmbedsRuntimeManifestAndLegalResources()
    {
        var root = GetRepositoryRoot().FullName;
        var project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "IGoLibrary.Ex.Desktop",
            "IGoLibrary.Ex.Desktop.csproj"));

        Assert.Contains("cloudflared-assets.json", project, StringComparison.Ordinal);
        Assert.Contains("cloudflared-LICENSE.txt", project, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES.txt", project, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedRuntimeAssets_ExactlyMatchBuildSources()
    {
        var root = GetRepositoryRoot().FullName;
        var catalog = new CloudflaredAssetCatalog(
            File.ReadAllText(Path.Combine(root, "build", "cloudflared-assets.json")),
            "win-x64",
            NullLogger<CloudflaredAssetCatalog>.Instance);
        using var embeddedManifest = typeof(CloudflaredAssetCatalog).Assembly.GetManifestResourceStream(
            CloudflaredAssetCatalog.ManifestResourceName);
        Assert.NotNull(embeddedManifest);
        using var memory = new MemoryStream();
        embeddedManifest!.CopyTo(memory);

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(root, "build", "cloudflared-assets.json")),
            memory.ToArray());
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(root, "build", "third-party", "cloudflared-LICENSE.txt")),
            catalog.LicenseBytes);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(root, "build", "third-party", "THIRD-PARTY-NOTICES.txt")),
            catalog.NoticesBytes);
    }

    private static DirectoryInfo GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IGoLibrary-Ex.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}

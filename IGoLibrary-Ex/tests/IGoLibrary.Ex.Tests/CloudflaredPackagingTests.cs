using System.Text.Json;

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
            Assert.False(string.IsNullOrWhiteSpace(asset.Value.GetProperty("fileName").GetString()));
        }
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

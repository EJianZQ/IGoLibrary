using System.Text.Json;
using System.Text.RegularExpressions;

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
    public void PublishScripts_DeclareLightweightAndBundledPackagesWithIsolatedTools()
    {
        var build = Path.Combine(GetRepositoryRoot().FullName, "build");
        var windows = File.ReadAllText(Path.Combine(build, "publish-windows.ps1"));
        var mac = File.ReadAllText(Path.Combine(build, "publish-macos.ps1"));
        var macAll = File.ReadAllText(Path.Combine(build, "publish-macos-all.ps1"));
        var macBash = File.ReadAllText(Path.Combine(build, "publish-macos.sh"));
        var windowsVerifier = File.ReadAllText(Path.Combine(build, "verify-windows-package.ps1"));

        Assert.Contains("prepare-cloudflared.ps1", windows, StringComparison.Ordinal);
        Assert.Contains("prepare-cloudflared.ps1", mac, StringComparison.Ordinal);
        Assert.Contains("[string]$BundledPackageName", windows, StringComparison.Ordinal);
        Assert.Contains("[string]$BundledPackageName", mac, StringComparison.Ordinal);
        Assert.Contains("windows-x64-with-cloudflared.zip", windows, StringComparison.Ordinal);
        Assert.Contains("macOS-Apple-Silicon-arm64-with-cloudflared.zip", macAll, StringComparison.Ordinal);
        Assert.Contains("macOS-Intel-x64-with-cloudflared.zip", macAll, StringComparison.Ordinal);
        Assert.Contains("Copy-DirectoryWithoutTools", windows, StringComparison.Ordinal);
        Assert.Contains("Install-ValidatedReleaseArtifacts", windows, StringComparison.Ordinal);
        Assert.Contains("New-MacAppBundle -IncludeTools $false", mac, StringComparison.Ordinal);
        Assert.Contains("New-MacAppBundle -IncludeTools $true", mac, StringComparison.Ordinal);
        Assert.Contains("Test-MacAppZip", mac, StringComparison.Ordinal);
        Assert.Contains("Install-ValidatedPackagePair", mac, StringComparison.Ordinal);
        Assert.Contains("Remove-SafeArtifactDirectory -Path $PublishOutput", mac, StringComparison.Ordinal);
        Assert.Contains("[string]$CompanionPackagePath", windowsVerifier, StringComparison.Ordinal);
        Assert.Contains("manifest 不得声明 tools", windowsVerifier, StringComparison.Ordinal);
        Assert.Contains("tools/cloudflared/THIRD-PARTY-NOTICES.txt", windowsVerifier, StringComparison.Ordinal);
        Assert.Contains("command -v pwsh", macBash, StringComparison.Ordinal);
        Assert.Contains("publish-macos.ps1", macBash, StringComparison.Ordinal);
        Assert.Contains("-NoLogo", macBash, StringComparison.Ordinal);
        Assert.Contains("-NoProfile", macBash, StringComparison.Ordinal);
        Assert.Contains("-NonInteractive", macBash, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(windows, @"&\s+dotnet\s+@").Count);
        Assert.Single(Regex.Matches(mac, @"&\s+dotnet\s+@"));
        Assert.Contains("$leafName -eq \"cloudflared\"", mac, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(build, "third-party", "cloudflared-LICENSE.txt")));
        Assert.True(File.Exists(Path.Combine(build, "third-party", "THIRD-PARTY-NOTICES.txt")));
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

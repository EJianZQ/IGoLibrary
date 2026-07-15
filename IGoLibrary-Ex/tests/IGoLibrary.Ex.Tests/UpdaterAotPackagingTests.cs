namespace IGoLibrary.Ex.Tests;

public sealed class UpdaterAotPackagingTests
{
    [Fact]
    public void WindowsPublishEnforcesNativeAotAndUpdaterSizeBudgets()
    {
        var root = FindProjectRoot();
        var publishText = File.ReadAllText(Path.Combine(root, "build", "publish-windows.ps1"));
        var verifierText = File.ReadAllText(Path.Combine(root, "build", "verify-windows-package.ps1"));
        var dialogText = File.ReadAllText(Path.Combine(
            root,
            "src",
            "IGoLibrary.Ex.Updater",
            "NativeUpdaterDialog.cs"));
        var solutionText = File.ReadAllText(Path.Combine(root, "IGoLibrary-Ex.sln"));

        Assert.Contains("-p:PublishAot=true", publishText, StringComparison.Ordinal);
        Assert.Contains("-p:OptimizationPreference=Size", publishText, StringComparison.Ordinal);
        Assert.Contains("Assert-NativeAotToolchain", publishText, StringComparison.Ordinal);
        Assert.Contains("Assert-UpdaterHeadlessSmoke", publishText, StringComparison.Ordinal);
        Assert.Contains("Assert-UpdaterTaskDialogSmoke", publishText, StringComparison.Ordinal);
        Assert.Contains("Assert-PublishedUpdaterTransactions", publishText, StringComparison.Ordinal);
        Assert.Contains("IGoLibrary.Ex.Updater.AcceptanceTests.csproj", publishText, StringComparison.Ordinal);
        Assert.Contains("IGOLIBRARY_MANAGED_UPDATER_BASELINE_PATH", publishText, StringComparison.Ordinal);
        Assert.Contains("ManagedUpdaterBaselinePath", publishText, StringComparison.Ordinal);
        Assert.Contains("我去图书馆 - 正在更新", publishText, StringComparison.Ordinal);
        Assert.Contains("WaitForExit(10000)", publishText, StringComparison.Ordinal);
        Assert.Contains("IGoLibrary.Ex.Updater.pdb", publishText, StringComparison.Ordinal);
        Assert.Contains("-Filter '*.pdb'", publishText, StringComparison.Ordinal);
        Assert.Contains("CompressionLevel]::SmallestSize", publishText, StringComparison.Ordinal);
        Assert.Contains("$maximumUpdaterBytes = 20MB", publishText, StringComparison.Ordinal);
        Assert.Contains("$maximumUpdaterCompressedBytes = 10MB", publishText, StringComparison.Ordinal);
        Assert.Contains("$maximumUpdaterBytes = 20MB", verifierText, StringComparison.Ordinal);
        Assert.Contains("$maximumUpdaterCompressedBytes = 10MB", verifierText, StringComparison.Ordinal);
        Assert.Contains("UpdaterProductVersion", verifierText, StringComparison.Ordinal);
        Assert.Contains("Test-IsForbiddenUpdaterSidecar", verifierText, StringComparison.Ordinal);
        Assert.Contains("updater sidecar", verifierText, StringComparison.Ordinal);
        Assert.Contains(
            "Flags = TaskDialogFlagSizeToContent | TaskDialogFlagAllowDialogCancellation",
            dialogText,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            System.Text.RegularExpressions.Regex.Matches(
                dialogText,
                "Flags = TaskDialogFlagSizeToContent \\| TaskDialogFlagAllowDialogCancellation").Count);
        Assert.Contains(
            "Flags = TaskDialogFlagShowMarqueeProgressBar | TaskDialogFlagSizeToContent",
            dialogText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IGoLibrary.Ex.Updater.AcceptanceTests",
            solutionText,
            StringComparison.Ordinal);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IGoLibrary-Ex.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IGoLibrary-Ex.sln.");
    }
}

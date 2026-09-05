using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Updates;
using IGoLibrary.Ex.Desktop;
using Markdown.Avalonia.Full;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class UpdateReleaseWindowTests
{
    [Theory]
    [InlineData("1.0.1", "发现新版本 - 当前版本号 v1.0.1")]
    [InlineData("v1.0.8", "发现新版本 - 当前版本号 v1.0.8")]
    [InlineData(" V1.1.1 ", "发现新版本 - 当前版本号 v1.1.1")]
    public void BuildWindowTitle_IncludesCurrentVersion_WithSingleLowercaseVPrefix(
        string currentVersionText,
        string expected)
    {
        Assert.Equal(expected, UpdateReleaseWindow.BuildWindowTitle(currentVersionText));
    }

    [AvaloniaFact]
    public void CreateReleaseBodyViewer_ReturnsMarkdownScrollViewer()
    {
        var markdownViewer = UpdateReleaseWindow.CreateReleaseBodyViewer(
            "### 新功能\n\n- 支持 **Markdown** 渲染");

        Assert.IsType<MarkdownScrollViewer>(markdownViewer);
        Assert.True(markdownViewer.SelectionEnabled);
    }

    [AvaloniaFact]
    public void CreateReleaseBodyViewer_AddsRightInset_ForOverlayScrollBar()
    {
        var markdownViewer = UpdateReleaseWindow.CreateReleaseBodyViewer(
            "### 新功能\n\n- 支持 **Markdown** 渲染");

        var style = Assert.Single(markdownViewer.Styles.OfType<Style>());
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Equal(Layoutable.MarginProperty, setter.Property);
        Assert.Equal(new Thickness(0, 0, 20, 0), setter.Value);
    }

    [AvaloniaFact]
    public void CreateReleaseBodyViewer_TrimsMarkdownBody_BeforeRendering()
    {
        var markdownViewer = UpdateReleaseWindow.CreateReleaseBodyViewer(
            "  ### 新功能\n\n- 支持 **Markdown** 渲染  ");

        Assert.Equal("### 新功能\n\n- 支持 **Markdown** 渲染", markdownViewer.Markdown);
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateReleaseBodyViewer_UsesFallbackMarkdownBody_WhenReleaseBodyIsBlank(string body)
    {
        var markdownViewer = UpdateReleaseWindow.CreateReleaseBodyViewer(body);

        Assert.Equal("此版本没有填写更新说明", markdownViewer.Markdown);
    }

    [Fact]
    public void ShouldShowAutomaticInstall_RequiresWindowsAndQualifiedAsset()
    {
        var release = new ReleaseUpdateInfo(
            new ReleaseVersion(1, 0, 1),
            "v1.0.1",
            "IGoLibrary-Ex v1.0.1",
            "notes",
            new Uri("https://github.com/EJianZQ/IGoLibrary/releases/tag/v1.0.1"),
            DateTimeOffset.UtcNow,
            new ReleaseAssetInfo(
                "IGoLibrary-Ex-v1.0.1-windows-x64.zip",
                new Uri("https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/file.zip"),
                1,
                "sha256:" + new string('0', 64),
                "application/zip"));

        var currentVersion = new ReleaseVersion(1, 0, 0);
        Assert.True(UpdateReleaseWindow.ShouldShowAutomaticInstall(
            release,
            currentVersion,
            isWindows: true));
        Assert.False(UpdateReleaseWindow.ShouldShowAutomaticInstall(
            release,
            currentVersion,
            isWindows: false));
        Assert.False(UpdateReleaseWindow.ShouldShowAutomaticInstall(
            release with { WindowsX64Package = null },
            currentVersion,
            isWindows: true));
    }

    [Fact]
    public void AutomaticUpdatePresentation_CurrentVersionBeforeMinimum_UsesManualDownloadWarning()
    {
        var release = CreateRelease() with
        {
            AutomaticUpdatePolicy = AutomaticUpdatePolicy.RequireMinimumVersion(
                new ReleaseVersion(1, 0, 5))
        };
        var currentVersion = new ReleaseVersion(1, 0, 4);

        Assert.False(UpdateReleaseWindow.ShouldShowAutomaticInstall(
            release,
            currentVersion,
            isWindows: true));
        var warning = Assert.IsType<string>(UpdateReleaseWindow.BuildAutomaticUpdateWarning(
            release,
            currentVersion,
            isWindows: true));
        Assert.Contains("v1.0.4", warning);
        Assert.Contains("v1.0.5", warning);
        Assert.Contains("手动下载", warning);
    }

    [Fact]
    public void AutomaticUpdatePresentation_InvalidPolicy_UsesSafeGenericWarning()
    {
        var release = CreateRelease() with
        {
            AutomaticUpdatePolicy = AutomaticUpdatePolicy.Invalid
        };

        var warning = Assert.IsType<string>(UpdateReleaseWindow.BuildAutomaticUpdateWarning(
            release,
            new ReleaseVersion(1, 0, 4),
            isWindows: true));

        Assert.Contains("兼容性标记无效", warning);
        Assert.DoesNotContain("InvalidMarkerSyntax", warning);
    }

    [Fact]
    public void AutomaticUpdatePresentation_NonWindows_KeepsExistingManualFlowWithoutPolicyWarning()
    {
        var release = CreateRelease() with
        {
            AutomaticUpdatePolicy = AutomaticUpdatePolicy.Invalid
        };

        Assert.Null(UpdateReleaseWindow.BuildAutomaticUpdateWarning(
            release,
            new ReleaseVersion(1, 0, 4),
            isWindows: false));
    }

    [AvaloniaFact]
    public void IncompatibleWindow_ShowsWarningAndMakesManualDownloadThePrimaryAction()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var release = CreateRelease() with
        {
            AutomaticUpdatePolicy = AutomaticUpdatePolicy.RequireMinimumVersion(
                new ReleaseVersion(1, 0, 5))
        };
        var window = new UpdateReleaseWindow(release, new ReleaseVersion(1, 0, 4));
        try
        {
            var releasePageButton = FindNamedControl<Button>(
                window,
                "ReleasePageButton");
            var warning = FindNamedControl<TextBlock>(
                window,
                "AutomaticUpdateWarningText");

            Assert.Equal("前往 GitHub 手动下载", releasePageButton.Content);
            Assert.Contains("accent", releasePageButton.Classes);
            Assert.Contains("v1.0.4", warning.Text);
            Assert.Contains("v1.0.5", warning.Text);
            Assert.DoesNotContain(
                window.GetLogicalDescendants().OfType<Button>(),
                button => button.Name == "AutomaticInstallButton");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SupportedWindow_KeepsAutomaticInstallAsThePrimaryAction()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var release = CreateRelease() with
        {
            AutomaticUpdatePolicy = AutomaticUpdatePolicy.RequireMinimumVersion(
                new ReleaseVersion(1, 0, 5))
        };
        var window = new UpdateReleaseWindow(release, new ReleaseVersion(1, 0, 5));
        try
        {
            var releasePageButton = FindNamedControl<Button>(
                window,
                "ReleasePageButton");
            var installButton = FindNamedControl<Button>(
                window,
                "AutomaticInstallButton");

            Assert.Equal("前往 GitHub", releasePageButton.Content);
            Assert.DoesNotContain("accent", releasePageButton.Classes);
            Assert.Contains("accent", installButton.Classes);
            Assert.DoesNotContain(
                window.GetLogicalDescendants().OfType<Border>(),
                border => border.Name == "AutomaticUpdateWarning");
        }
        finally
        {
            window.Close();
        }
    }

    private static ReleaseUpdateInfo CreateRelease()
    {
        return new ReleaseUpdateInfo(
            new ReleaseVersion(1, 0, 6),
            "v1.0.6",
            "IGoLibrary-Ex v1.0.6",
            "notes",
            new Uri("https://github.com/EJianZQ/IGoLibrary/releases/tag/v1.0.6"),
            DateTimeOffset.UtcNow,
            new ReleaseAssetInfo(
                "IGoLibrary-Ex-v1.0.6-windows-x64.zip",
                new Uri("https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.6/file.zip"),
                1,
                "sha256:" + new string('0', 64),
                "application/zip"));
    }

    private static T FindNamedControl<T>(Control root, string name)
        where T : Control
    {
        return Assert.Single(
            root.GetLogicalDescendants().OfType<T>(),
            control => control.Name == name);
    }
}

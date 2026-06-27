using Avalonia;
using Avalonia.Layout;
using Avalonia.Styling;
using IGoLibrary.Ex.Desktop;
using Markdown.Avalonia.Full;

namespace IGoLibrary.Ex.Tests;

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

    [Fact]
    public void CreateReleaseBodyViewer_ReturnsMarkdownScrollViewer()
    {
        var markdownViewer = UpdateReleaseWindow.CreateReleaseBodyViewer(
            "### 新功能\n\n- 支持 **Markdown** 渲染");

        Assert.IsType<MarkdownScrollViewer>(markdownViewer);
        Assert.True(markdownViewer.SelectionEnabled);
    }

    [Fact]
    public void CreateReleaseBodyViewer_AddsRightInset_ForOverlayScrollBar()
    {
        var markdownViewer = UpdateReleaseWindow.CreateReleaseBodyViewer(
            "### 新功能\n\n- 支持 **Markdown** 渲染");

        var style = Assert.Single(markdownViewer.Styles.OfType<Style>());
        var setter = Assert.Single(style.Setters.OfType<Setter>());
        Assert.Equal(Layoutable.MarginProperty, setter.Property);
        Assert.Equal(new Thickness(0, 0, 20, 0), setter.Value);
    }

    [Fact]
    public void CreateReleaseBodyViewer_TrimsMarkdownBody_BeforeRendering()
    {
        var markdownViewer = UpdateReleaseWindow.CreateReleaseBodyViewer(
            "  ### 新功能\n\n- 支持 **Markdown** 渲染  ");

        Assert.Equal("### 新功能\n\n- 支持 **Markdown** 渲染", markdownViewer.Markdown);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateReleaseBodyViewer_UsesFallbackMarkdownBody_WhenReleaseBodyIsBlank(string body)
    {
        var markdownViewer = UpdateReleaseWindow.CreateReleaseBodyViewer(body);

        Assert.Equal("此版本没有填写更新说明。", markdownViewer.Markdown);
    }
}

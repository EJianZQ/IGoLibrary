using IGoLibrary.Ex.Desktop;

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
}

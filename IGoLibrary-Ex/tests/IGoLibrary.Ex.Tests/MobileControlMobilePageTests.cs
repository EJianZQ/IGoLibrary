using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Tests;

public sealed class MobileControlMobilePageTests
{
    [Fact]
    public void Build_InjectsTokenAndContainsActionDom()
    {
        var html = MobileControlMobilePage.Build("secret-token");

        Assert.Contains("const token = \"secret-token\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__MOBILE_CONTROL_TOKEN_JSON__", html, StringComparison.Ordinal);
        Assert.Contains("取消任务", html, StringComparison.Ordinal);
        Assert.Contains("取消当前预约", html, StringComparison.Ordinal);
        Assert.Contains("当前无任务", html, StringComparison.Ordinal);
        Assert.Contains("const display = value =>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cookieProgress\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"reservationProgress\"", html, StringComparison.Ordinal);
    }
}

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
        Assert.Contains("刷新 Cookie", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"cookie-refresh-panel\" id=\"cookieRefreshPanel\">", html, StringComparison.Ordinal);
        Assert.Contains("<summary>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"cookieRefreshPanel\" open", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cookieRefreshForm\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"authQrCode\"", html, StringComparison.Ordinal);
        Assert.Contains("微信授权二维码", html, StringComparison.Ordinal);
        Assert.Contains("/api/session/auth-qrcode?token=' + encodeURIComponent(token)", html, StringComparison.Ordinal);
        Assert.Contains("/api/session/cookie/refresh", html, StringComparison.Ordinal);
        Assert.Contains("当前无任务", html, StringComparison.Ordinal);
        Assert.Contains("const display = value =>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cookieProgress\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"reservationProgress\"", html, StringComparison.Ordinal);
    }
}

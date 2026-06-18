using System.Net;
using System.Text.Json;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Notifications;

namespace IGoLibrary.Ex.Tests;

public sealed class BarkAlertSenderTests
{
    [Fact]
    public async Task SendAsync_PostsJsonToBarkPushEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new SequenceHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"code":200,"message":"success","timestamp":1710000000}""")
            };
        });
        var sender = CreateSender(handler);

        await sender.SendAsync(
            new BarkAlertChannelSettings(true, "https://api.day.app/", "abc/key", "alarm", "Library"),
            "抢座成功",
            "自科阅览区一 · 2号座 已成功预约");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://api.day.app/abc%2Fkey", capturedRequest.RequestUri?.ToString());
        using var document = JsonDocument.Parse(capturedBody!);
        Assert.Equal("抢座成功", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("自科阅览区一 · 2号座 已成功预约", document.RootElement.GetProperty("body").GetString());
        Assert.Equal("alarm", document.RootElement.GetProperty("sound").GetString());
        Assert.Equal("Library", document.RootElement.GetProperty("group").GetString());
    }

    [Fact]
    public async Task SendAsync_ThrowsReadableException_WhenBarkReturnsFailureCode()
    {
        var handler = new SequenceHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"code":400,"message":"device key is invalid"}""")
        }));
        var sender = CreateSender(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new BarkAlertChannelSettings(true, "https://api.day.app", "device-key", string.Empty, "Library"),
            "测试",
            "测试消息"));

        Assert.Contains("code=400", exception.Message);
        Assert.Contains("device key is invalid", exception.Message);
    }

    [Fact]
    public async Task SendAsync_RetriesTransientHttpFailure_UsingSavedMaxRetries()
    {
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("temporary")
            }),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"code":200,"message":"success"}""")
            }));
        var sender = CreateSender(handler, AppSettings.Default with { Network = new NetworkRequestSettings(1, 1) });

        await sender.SendAsync(
            new BarkAlertChannelSettings(true, "https://api.day.app", "device-key", string.Empty, "Library"),
            "测试",
            "测试消息");

        Assert.Equal(2, handler.CallCount);
    }

    [Theory]
    [InlineData("", "device-key", "请填写 Bark 服务器地址")]
    [InlineData(null, "device-key", "请填写 Bark 服务器地址")]
    [InlineData("api.day.app", "device-key", "Bark 服务器地址必须是 http 或 https 绝对地址")]
    [InlineData("ftp://api.day.app", "device-key", "Bark 服务器地址必须是 http 或 https 绝对地址")]
    [InlineData("https://api.day.app", "", "请填写 Bark Device Key")]
    [InlineData("https://api.day.app", null, "请填写 Bark Device Key")]
    public void Normalize_ValidatesRequiredSettings(string? serverUrl, string? deviceKey, string expectedMessage)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BarkAlertSender.Normalize(
            new BarkAlertChannelSettings(true, serverUrl!, deviceKey!, string.Empty, "Library")));

        Assert.Equal(expectedMessage, exception.Message);
    }

    private static BarkAlertSender CreateSender(
        HttpMessageHandler handler,
        AppSettings? settings = null)
    {
        return new BarkAlertSender(
            new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            new FakeSettingsService(settings ?? AppSettings.Default with
            {
                Network = AppSettings.Default.Network with { MaxRetries = 0 }
            }));
    }
}

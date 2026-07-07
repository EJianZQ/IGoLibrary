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
                Content = new StringContent("""{"code":200,"message":"success","timestamp":1}""")
            };
        });
        var sender = CreateSender(handler);

        await sender.SendAsync(
            new BarkAlertChannelSettings(true, "https://api.day.app/", "key-1", "IGoLibrary-Ex", "alarm", "timeSensitive"),
            "抢座成功",
            "测试内容");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://api.day.app/push", capturedRequest.RequestUri?.ToString());
        using var document = JsonDocument.Parse(capturedBody!);
        Assert.Equal("key-1", document.RootElement.GetProperty("device_key").GetString());
        Assert.Equal("抢座成功", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("测试内容", document.RootElement.GetProperty("body").GetString());
        Assert.Equal("IGoLibrary-Ex", document.RootElement.GetProperty("group").GetString());
        Assert.Equal("alarm", document.RootElement.GetProperty("sound").GetString());
        Assert.Equal("timeSensitive", document.RootElement.GetProperty("level").GetString());
    }

    [Fact]
    public async Task SendAsync_OmitsEmptyOptionalFields()
    {
        string? capturedBody = null;
        var handler = new SequenceHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"code":200,"message":"success","timestamp":1}""")
            };
        });
        var sender = CreateSender(handler);

        await sender.SendAsync(
            new BarkAlertChannelSettings(true, "https://api.day.app", "key-1", "", "", ""),
            "抢座成功",
            "测试内容");

        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("group", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("sound", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("level", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_ThrowsReadableException_WhenBarkReturnsFailureCode()
    {
        var handler = new SequenceHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"code":400,"message":"device key is empty","timestamp":1}""")
        }));
        var sender = CreateSender(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new BarkAlertChannelSettings(true, "https://api.day.app", "key-1", "", "", ""),
            "抢座成功",
            "测试内容"));

        Assert.Contains("code=400", exception.Message);
        Assert.Contains("device key is empty", exception.Message);
    }

    [Fact]
    public async Task SendAsync_ThrowsReadableException_WhenBarkReturnsInvalidJson()
    {
        var handler = new SequenceHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json")
        }));
        var sender = CreateSender(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new BarkAlertChannelSettings(true, "https://api.day.app", "key-1", "", "", ""),
            "抢座成功",
            "测试内容"));

        Assert.Contains("Bark API 返回不是有效 JSON", exception.Message);
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
                Content = new StringContent("""{"code":200,"message":"success","timestamp":2}""")
            }));
        var sender = CreateSender(handler, AppSettings.Default with { Network = new NetworkRequestSettings(1, 1) });

        await sender.SendAsync(
            new BarkAlertChannelSettings(true, "https://api.day.app", "key-1", "", "", ""),
            "抢座成功",
            "测试内容");

        Assert.Equal(2, handler.CallCount);
    }

    [Theory]
    [InlineData("", "key-1", "", "请填写 Bark 服务端地址")]
    [InlineData(null, "key-1", "", "请填写 Bark 服务端地址")]
    [InlineData("api.day.app", "key-1", "", "Bark 服务端地址必须是 http 或 https 绝对地址")]
    [InlineData("ftp://api.day.app", "key-1", "", "Bark 服务端地址必须是 http 或 https 绝对地址")]
    [InlineData("https://api.day.app", "", "", "请填写 Bark Device Key")]
    [InlineData("https://api.day.app", null, "", "请填写 Bark Device Key")]
    [InlineData("https://api.day.app", "key-1", "urgent", "Bark 通知级别必须是 active、timeSensitive、passive 或 critical")]
    public void Normalize_ValidatesRequiredSettings(string? apiBaseUrl, string? deviceKey, string? level, string expectedMessage)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BarkAlertSender.Normalize(
            new BarkAlertChannelSettings(true, apiBaseUrl!, deviceKey!, "", "", level!)));

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

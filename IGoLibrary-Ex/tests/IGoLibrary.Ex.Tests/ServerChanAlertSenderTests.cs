using System.Net;
using System.Text.Json;
using IGoLibrary.Ex.Infrastructure.Notifications;

namespace IGoLibrary.Ex.Tests;

public sealed class ServerChanAlertSenderTests
{
    [Fact]
    public async Task SendAsync_PostsJsonToTurboEndpoint_WithOptionalFields()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new SequenceHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return SuccessResponse();
        });
        var sender = CreateSender(handler);

        await sender.SendAsync(
            new ServerChanAlertChannelSettings(true, "SCT_xxx", true, "9 | 66", " user-1 "),
            "抢座成功",
            "测试内容");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://sctapi.ftqq.com/SCT_xxx.send", capturedRequest.RequestUri?.ToString());
        using var document = JsonDocument.Parse(capturedBody!);
        Assert.Equal("抢座成功", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("测试内容", document.RootElement.GetProperty("desp").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("noip").GetInt32());
        Assert.Equal("9|66", document.RootElement.GetProperty("channel").GetString());
        Assert.Equal("user-1", document.RootElement.GetProperty("openid").GetString());
        Assert.False(document.RootElement.TryGetProperty("short", out _));
    }

    [Fact]
    public async Task SendAsync_UsesServerChan3EndpointForSctpSendKey_AndOmitsEmptyOptionalFields()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new SequenceHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return SuccessResponse();
        });
        var sender = CreateSender(handler);

        await sender.SendAsync(
            new ServerChanAlertChannelSettings(true, "sctp123tabcdef", false, "", ""),
            "抢座成功",
            "测试内容");

        Assert.NotNull(capturedRequest);
        Assert.Equal("https://123.push.ft07.com/send/sctp123tabcdef.send", capturedRequest.RequestUri?.ToString());
        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("noip", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("channel", capturedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("openid", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_TruncatesTitleToServerChanLimit()
    {
        string? capturedBody = null;
        var handler = new SequenceHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return SuccessResponse();
        });
        var sender = CreateSender(handler);

        await sender.SendAsync(
            new ServerChanAlertChannelSettings(true, "SCT_xxx", false, "", ""),
            new string('a', 40),
            "测试内容");

        using var document = JsonDocument.Parse(capturedBody!);
        Assert.Equal(32, document.RootElement.GetProperty("title").GetString()?.Length);
    }

    [Fact]
    public async Task SendAsync_ThrowsReadableException_WhenServerChanReturnsFailureCode()
    {
        var handler = new SequenceHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"code":1024,"message":"bad sendkey"}""")
        }));
        var sender = CreateSender(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new ServerChanAlertChannelSettings(true, "SCT_xxx", false, "", ""),
            "抢座成功",
            "测试内容"));

        Assert.Contains("code=1024", exception.Message);
        Assert.Contains("bad sendkey", exception.Message);
    }

    [Fact]
    public async Task SendAsync_ReadsFailureMessageFromDataError()
    {
        var handler = new SequenceHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":1001,"data":{"error":"channel invalid"}}""")
        }));
        var sender = CreateSender(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new ServerChanAlertChannelSettings(true, "SCT_xxx", false, "", ""),
            "抢座成功",
            "测试内容"));

        Assert.Contains("channel invalid", exception.Message);
    }

    [Fact]
    public async Task SendAsync_ThrowsReadableException_WhenServerChanReturnsInvalidJson()
    {
        var handler = new SequenceHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json")
        }));
        var sender = CreateSender(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new ServerChanAlertChannelSettings(true, "SCT_xxx", false, "", ""),
            "抢座成功",
            "测试内容"));

        Assert.Contains("Server酱 API 返回不是有效 JSON", exception.Message);
    }

    [Fact]
    public async Task SendAsync_RetriesTransientHttpFailure_UsingSavedMaxRetries()
    {
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("temporary")
            }),
            (_, _) => Task.FromResult(SuccessResponse()));
        var timeProvider = new FakeTimeProvider();
        var sender = CreateSender(
            handler,
            AppSettings.Default with { Network = new NetworkRequestSettings(1, 1) },
            timeProvider);

        var sendTask = sender.SendAsync(
            new ServerChanAlertChannelSettings(true, "SCT_xxx", false, "", ""),
            "抢座成功",
            "测试内容");
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        await sendTask;

        Assert.Equal(2, handler.CallCount);
    }

    [Theory]
    [InlineData("", "", "请填写 Server酱 SendKey")]
    [InlineData(null, "", "请填写 Server酱 SendKey")]
    [InlineData("sctpabc", "", "Server酱³ SendKey 格式不正确")]
    [InlineData("sctp123", "", "Server酱³ SendKey 格式不正确")]
    [InlineData("SCT_xxx", "9|66|88", "Server酱 channel 最多指定两个通道")]
    [InlineData("SCT_xxx", "9|abc", "Server酱 channel 必须是数字，多个通道用 | 分隔")]
    [InlineData("SCT_xxx", "9||66", "Server酱 channel 最多指定两个通道")]
    public void Normalize_ValidatesRequiredSettings(string? sendKey, string? channel, string expectedMessage)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ServerChanAlertSender.Normalize(
            new ServerChanAlertChannelSettings(true, sendKey!, false, channel!, "")));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public async Task SendAsync_ThrowsReadableException_WhenDespExceedsLimit()
    {
        var sender = CreateSender(new SequenceHttpMessageHandler());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new ServerChanAlertChannelSettings(true, "SCT_xxx", false, "", ""),
            "抢座成功",
            new string('a', 32 * 1024 + 1)));

        Assert.Equal("Server酱推送内容不能超过 32KB", exception.Message);
    }

    private static HttpResponseMessage SuccessResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":0,"message":"success","data":{"pushid":"1","readkey":"r"}}""")
        };
    }

    private static ServerChanAlertSender CreateSender(
        HttpMessageHandler handler,
        AppSettings? settings = null,
        TimeProvider? timeProvider = null)
    {
        return new ServerChanAlertSender(
            new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            new FakeSettingsService(settings ?? AppSettings.Default with
            {
                Network = AppSettings.Default.Network with { MaxRetries = 0 }
            }),
            timeProvider);
    }
}

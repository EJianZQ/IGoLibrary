using System.Net;
using System.Text.Json;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Notifications;

namespace IGoLibrary.Ex.Tests;

public sealed class WxPusherAlertSenderTests
{
    [Fact]
    public async Task SendAsync_PostsJsonToWxPusherMessageEndpoint()
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
                Content = new StringContent(
                    """
                    {"code":1000,"msg":"处理成功","data":[{"uid":"UID_1","code":1000,"status":"创建发送任务成功"}],"success":true}
                    """)
            };
        });
        var sender = CreateSender(handler);

        await sender.SendAsync(
            new WxPusherAlertChannelSettings(
                true,
                "https://wxpusher.zjiecode.com/",
                "AT_xxx",
                "UID_1, UID_2\nUID_3",
                "123;456"),
            "抢座成功",
            "测试内容");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://wxpusher.zjiecode.com/api/send/message", capturedRequest.RequestUri?.ToString());
        using var document = JsonDocument.Parse(capturedBody!);
        Assert.Equal("AT_xxx", document.RootElement.GetProperty("appToken").GetString());
        Assert.Equal("测试内容", document.RootElement.GetProperty("content").GetString());
        Assert.Equal("抢座成功", document.RootElement.GetProperty("summary").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("contentType").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("verifyPayType").GetInt32());
        Assert.Equal(["UID_1", "UID_2", "UID_3"], document.RootElement.GetProperty("uids").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal([123, 456], document.RootElement.GetProperty("topicIds").EnumerateArray().Select(x => x.GetInt32()).ToArray());
    }

    [Fact]
    public async Task SendAsync_TruncatesSummaryToWxPusherLimit()
    {
        string? capturedBody = null;
        var handler = new SequenceHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"code":1000,"msg":"处理成功","data":[],"success":true}""")
            };
        });
        var sender = CreateSender(handler);

        await sender.SendAsync(
            new WxPusherAlertChannelSettings(true, "https://wxpusher.zjiecode.com", "AT_xxx", "UID_1", ""),
            new string('a', 120),
            "测试内容");

        using var document = JsonDocument.Parse(capturedBody!);
        Assert.Equal(100, document.RootElement.GetProperty("summary").GetString()?.Length);
    }

    [Fact]
    public async Task SendAsync_ThrowsReadableException_WhenWxPusherReturnsTopLevelFailureCode()
    {
        var handler = new SequenceHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"code":1001,"msg":"appToken错误","success":false}""")
        }));
        var sender = CreateSender(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new WxPusherAlertChannelSettings(true, "https://wxpusher.zjiecode.com", "AT_xxx", "UID_1", ""),
            "抢座成功",
            "测试内容"));

        Assert.Contains("code=1001", exception.Message);
        Assert.Contains("appToken错误", exception.Message);
    }

    [Fact]
    public async Task SendAsync_ThrowsReadableException_WhenAnyRecipientFails()
    {
        var handler = new SequenceHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {"code":1000,"msg":"处理成功","data":[{"uid":"UID_1","code":1001,"status":"用户不存在"}],"success":true}
                """)
        }));
        var sender = CreateSender(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new WxPusherAlertChannelSettings(true, "https://wxpusher.zjiecode.com", "AT_xxx", "UID_1", ""),
            "抢座成功",
            "测试内容"));

        Assert.Contains("部分失败", exception.Message);
        Assert.Contains("UID_1", exception.Message);
        Assert.Contains("用户不存在", exception.Message);
    }

    [Fact]
    public async Task SendAsync_ThrowsReadableException_WhenWxPusherReturnsInvalidJson()
    {
        var handler = new SequenceHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json")
        }));
        var sender = CreateSender(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new WxPusherAlertChannelSettings(true, "https://wxpusher.zjiecode.com", "AT_xxx", "UID_1", ""),
            "抢座成功",
            "测试内容"));

        Assert.Contains("WxPusher API 返回不是有效 JSON", exception.Message);
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
                Content = new StringContent("""{"code":1000,"msg":"处理成功","data":[],"success":true}""")
            }));
        var timeProvider = new FakeTimeProvider();
        var sender = CreateSender(
            handler,
            AppSettings.Default with { Network = new NetworkRequestSettings(1, 1) },
            timeProvider);

        var sendTask = sender.SendAsync(
            new WxPusherAlertChannelSettings(true, "https://wxpusher.zjiecode.com", "AT_xxx", "UID_1", ""),
            "抢座成功",
            "测试内容");
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        await sendTask;

        Assert.Equal(2, handler.CallCount);
    }

    [Theory]
    [InlineData("", "AT_xxx", "UID_1", "", "请填写 WxPusher API 基础地址")]
    [InlineData(null, "AT_xxx", "UID_1", "", "请填写 WxPusher API 基础地址")]
    [InlineData("wxpusher.zjiecode.com", "AT_xxx", "UID_1", "", "WxPusher API 基础地址必须是 http 或 https 绝对地址")]
    [InlineData("ftp://wxpusher.zjiecode.com", "AT_xxx", "UID_1", "", "WxPusher API 基础地址必须是 http 或 https 绝对地址")]
    [InlineData("https://wxpusher.zjiecode.com", "", "UID_1", "", "请填写 WxPusher AppToken")]
    [InlineData("https://wxpusher.zjiecode.com", null, "UID_1", "", "请填写 WxPusher AppToken")]
    [InlineData("https://wxpusher.zjiecode.com", "AT_xxx", "", "", "请至少填写一个 WxPusher UID 或 Topic ID")]
    [InlineData("https://wxpusher.zjiecode.com", "AT_xxx", null, null, "请至少填写一个 WxPusher UID 或 Topic ID")]
    [InlineData("https://wxpusher.zjiecode.com", "AT_xxx", "", "abc", "WxPusher Topic ID 必须是正整数")]
    [InlineData("https://wxpusher.zjiecode.com", "AT_xxx", "", "0", "WxPusher Topic ID 必须是正整数")]
    public void Normalize_ValidatesRequiredSettings(
        string? apiBaseUrl,
        string? appToken,
        string? uids,
        string? topicIds,
        string expectedMessage)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => WxPusherAlertSender.Normalize(
            new WxPusherAlertChannelSettings(true, apiBaseUrl!, appToken!, uids!, topicIds!)));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public void Normalize_ValidatesRecipientLimits()
    {
        var tooManyUids = string.Join(",", Enumerable.Range(1, 2001).Select(static index => $"UID_{index}"));
        var tooManyTopicIds = string.Join(",", Enumerable.Range(1, 6));

        var uidException = Assert.Throws<InvalidOperationException>(() => WxPusherAlertSender.Normalize(
            new WxPusherAlertChannelSettings(true, "https://wxpusher.zjiecode.com", "AT_xxx", tooManyUids, "")));
        var topicException = Assert.Throws<InvalidOperationException>(() => WxPusherAlertSender.Normalize(
            new WxPusherAlertChannelSettings(true, "https://wxpusher.zjiecode.com", "AT_xxx", "", tooManyTopicIds)));

        Assert.Contains("UID 数量不能超过 2000 个", uidException.Message);
        Assert.Contains("Topic ID 数量不能超过 5 个", topicException.Message);
    }

    [Fact]
    public async Task SendAsync_ThrowsReadableException_WhenContentExceedsLimit()
    {
        var sender = CreateSender(new SequenceHttpMessageHandler());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new WxPusherAlertChannelSettings(true, "https://wxpusher.zjiecode.com", "AT_xxx", "UID_1", ""),
            "抢座成功",
            new string('a', 40001)));

        Assert.Contains("WxPusher 推送内容不能超过 40000 个字符", exception.Message);
    }

    private static WxPusherAlertSender CreateSender(
        HttpMessageHandler handler,
        AppSettings? settings = null,
        TimeProvider? timeProvider = null)
    {
        return new WxPusherAlertSender(
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

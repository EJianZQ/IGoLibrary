using System.Net;
using System.Text.Json;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Notifications;

namespace IGoLibrary.Ex.Tests;

public sealed class TelegramAlertSenderTests
{
    [Fact]
    public async Task SendAsync_PostsJsonToTelegramSendMessageEndpoint()
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
                Content = new StringContent("""{"ok":true,"result":{"message_id":1}}""")
            };
        });
        var sender = CreateSender(handler);

        await sender.SendAsync(
            new TelegramAlertSettings(true, "https://api.telegram.org/", "123:ABC", "456"),
            "测试消息");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://api.telegram.org/bot123:ABC/sendMessage", capturedRequest.RequestUri?.ToString());
        using var document = JsonDocument.Parse(capturedBody!);
        Assert.Equal("456", document.RootElement.GetProperty("chat_id").GetString());
        Assert.Equal("测试消息", document.RootElement.GetProperty("text").GetString());
        Assert.DoesNotContain("parse_mode", capturedBody);
    }

    [Fact]
    public async Task SendAsync_ThrowsReadableException_WhenTelegramReturnsOkFalse()
    {
        var handler = new SequenceHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"ok":false,"error_code":400,"description":"Bad Request: chat not found"}""")
        }));
        var sender = CreateSender(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new TelegramAlertSettings(true, "https://api.telegram.org", "123:ABC", "456"),
            "测试消息"));

        Assert.Contains("error_code=400", exception.Message);
        Assert.Contains("Bad Request: chat not found", exception.Message);
    }

    [Fact]
    public async Task SendAsync_RetriesTransientHttpFailure_UsingSavedRetryCount()
    {
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("temporary")
            }),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true,"result":{"message_id":2}}""")
            }));
        var sender = CreateSender(handler, AppSettings.Default with { ApiTimeoutSeconds = 1, RetryCount = 1 });

        await sender.SendAsync(
            new TelegramAlertSettings(true, "https://api.telegram.org", "123:ABC", "456"),
            "测试消息");

        Assert.Equal(2, handler.CallCount);
    }

    [Theory]
    [InlineData("", "123:ABC", "456", "请填写 Telegram API 基础地址")]
    [InlineData(null, "123:ABC", "456", "请填写 Telegram API 基础地址")]
    [InlineData("api.telegram.org", "123:ABC", "456", "Telegram API 基础地址必须是 http 或 https 绝对地址")]
    [InlineData("ftp://api.telegram.org", "123:ABC", "456", "Telegram API 基础地址必须是 http 或 https 绝对地址")]
    [InlineData("https://api.telegram.org", "", "456", "请填写 Telegram Bot Token")]
    [InlineData("https://api.telegram.org", null, "456", "请填写 Telegram Bot Token")]
    [InlineData("https://api.telegram.org", "123:ABC", "", "请填写 Telegram Chat ID")]
    [InlineData("https://api.telegram.org", "123:ABC", null, "请填写 Telegram Chat ID")]
    public void Normalize_ValidatesRequiredSettings(string? apiBaseUrl, string? botToken, string? chatId, string expectedMessage)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => TelegramAlertSender.Normalize(
            new TelegramAlertSettings(true, apiBaseUrl!, botToken!, chatId!)));

        Assert.Equal(expectedMessage, exception.Message);
    }

    private static TelegramAlertSender CreateSender(
        HttpMessageHandler handler,
        AppSettings? settings = null)
    {
        return new TelegramAlertSender(
            new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            new FakeSettingsService(settings ?? AppSettings.Default with { RetryCount = 0 }));
    }
}

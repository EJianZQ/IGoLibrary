using System.Text;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Notifications;
using MailKit.Security;
using MimeKit;

namespace IGoLibrary.Ex.Tests;

public sealed class SmtpEmailAlertSenderTests
{
    [Fact]
    public async Task SendAsync_UsesSslOnConnectForTlsPort465_AndAuthenticates()
    {
        var client = new FakeSmtpTransportClient();
        var sender = new SmtpEmailAlertSender(new FakeSmtpTransportClientFactory(client));

        await sender.SendAsync(
            CreateSettings(port: 465, securityMode: EmailSecurityMode.Tls, username: "tester", password: "secret"),
            "测试主题",
            "测试正文");

        var connection = Assert.Single(client.ConnectRequests);
        Assert.Equal("smtp.example.com", connection.Host);
        Assert.Equal(465, connection.Port);
        Assert.Equal(SecureSocketOptions.SslOnConnect, connection.Options);

        var auth = Assert.Single(client.AuthenticationRequests);
        Assert.Equal("tester", auth.Username);
        Assert.Equal("secret", auth.Password);
        Assert.Equal(1, client.DisconnectCalls);
        Assert.True(client.Disposed);
    }

    [Fact]
    public async Task SendAsync_UsesStartTlsForTlsPort587()
    {
        var client = new FakeSmtpTransportClient();
        var sender = new SmtpEmailAlertSender(new FakeSmtpTransportClientFactory(client));

        await sender.SendAsync(
            CreateSettings(port: 587, securityMode: EmailSecurityMode.Tls, username: "tester", password: "secret"),
            "测试主题",
            "测试正文");

        var connection = Assert.Single(client.ConnectRequests);
        Assert.Equal(SecureSocketOptions.StartTls, connection.Options);
    }

    [Fact]
    public async Task SendAsync_SkipsAuthenticationWhenCredentialsAreEmpty()
    {
        var client = new FakeSmtpTransportClient();
        var sender = new SmtpEmailAlertSender(new FakeSmtpTransportClientFactory(client));

        await sender.SendAsync(
            CreateSettings(port: 25, securityMode: EmailSecurityMode.None),
            "测试主题",
            "测试正文");

        var connection = Assert.Single(client.ConnectRequests);
        Assert.Equal(SecureSocketOptions.None, connection.Options);
        Assert.Empty(client.AuthenticationRequests);
    }

    [Fact]
    public async Task SendAsync_BuildsUtf8EncodedSubjectAndBody()
    {
        var client = new FakeSmtpTransportClient();
        var sender = new SmtpEmailAlertSender(new FakeSmtpTransportClientFactory(client));

        await sender.SendAsync(
            CreateSettings(port: 587, securityMode: EmailSecurityMode.Tls),
            "中文主题提醒",
            "这是一段中文正文。");

        var message = Assert.Single(client.SentMessages);
        var textPart = Assert.IsType<TextPart>(message.Body);
        var bodyText = textPart.GetText(out var encoding);
        Assert.Equal("utf-8", encoding.WebName);
        Assert.Equal("这是一段中文正文。", bodyText);

        using var stream = new MemoryStream();
        message.WriteTo(stream);
        var rawMessage = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("charset=utf-8", rawMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("=?utf-8?", rawMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static CookieExpiryEmailAlertSettings CreateSettings(
        int port,
        EmailSecurityMode securityMode,
        string username = "",
        string password = "")
    {
        return new CookieExpiryEmailAlertSettings(
            Enabled: true,
            SmtpHost: "smtp.example.com",
            Port: port,
            SecurityMode: securityMode,
            Username: username,
            Password: password,
            FromAddress: "sender@example.com",
            ToAddress: "receiver@example.com");
    }
}

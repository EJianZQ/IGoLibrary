using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class CookieExpiryAlertServiceTests
{
    [Fact]
    public async Task SendTestEmailAsync_ThrowsWhenOnlyUsernameIsProvided()
    {
        var service = CreateService();
        var settings = new CookieExpiryEmailAlertSettings(
            Enabled: true,
            SmtpHost: "smtp.example.com",
            Port: 587,
            SecurityMode: EmailSecurityMode.Tls,
            Username: "tester",
            Password: string.Empty,
            FromAddress: "sender@example.com",
            ToAddress: "receiver@example.com");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendTestEmailAsync(settings));

        Assert.Equal("SMTP 用户名和邮箱授权码/密码需要同时填写，或同时留空。", exception.Message);
    }

    [Fact]
    public async Task NotifyCookieExpiredAsync_SendsEmailUsingPersistedSettings()
    {
        var emailSender = new FakeEmailAlertSender();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            CookieExpiryAlerts = new CookieExpiryAlertSettings(
                new CookieExpiryEmailAlertSettings(
                    Enabled: true,
                    SmtpHost: "smtp.example.com",
                    Port: 587,
                    SecurityMode: EmailSecurityMode.Tls,
                    Username: "tester",
                    Password: "secret",
                    FromAddress: "sender@example.com",
                    ToAddress: "receiver@example.com"),
                new CookieExpiryLocalAlertSettings(false, false))
        });

        var service = CreateService(settingsService, emailSender);

        await service.NotifyCookieExpiredAsync("抢座轮询", "Cookie 无效");

        var request = Assert.Single(emailSender.Requests);
        Assert.Equal("IGoLibrary-Ex Cookie 失效提醒", request.Subject);
        Assert.Contains("触发模块：抢座轮询", request.Body);
        Assert.Contains("详细信息：Cookie 无效", request.Body);
    }

    [Fact]
    public async Task NotifyCookieExpiredAsync_LogsWarningWhenEmailSendFails()
    {
        var emailSender = new FakeEmailAlertSender
        {
            SendException = new InvalidOperationException("smtp boom")
        };
        var activityLog = new ActivityLogService();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            CookieExpiryAlerts = new CookieExpiryAlertSettings(
                new CookieExpiryEmailAlertSettings(
                    Enabled: true,
                    SmtpHost: "smtp.example.com",
                    Port: 587,
                    SecurityMode: EmailSecurityMode.Tls,
                    Username: "tester",
                    Password: "secret",
                    FromAddress: "sender@example.com",
                    ToAddress: "receiver@example.com"),
                new CookieExpiryLocalAlertSettings(false, false))
        });

        var service = CreateService(settingsService, emailSender, activityLog);

        await service.NotifyCookieExpiredAsync("占座轮询", "cookie expired");

        Assert.Contains(
            activityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning
                     && entry.Category == "Alert"
                     && entry.Message.Contains("发送 Cookie 过期提醒邮件失败：smtp boom", StringComparison.Ordinal));
    }

    private static CookieExpiryAlertService CreateService(
        FakeSettingsService? settingsService = null,
        FakeEmailAlertSender? emailSender = null,
        ActivityLogService? activityLogService = null)
    {
        return new CookieExpiryAlertService(
            settingsService ?? new FakeSettingsService(AppSettings.Default),
            emailSender ?? new FakeEmailAlertSender(),
            new ToastNotificationService(
                settingsService ?? new FakeSettingsService(AppSettings.Default),
                new AppWindowService()),
            new AlertSoundService(),
            activityLogService ?? new ActivityLogService());
    }
}

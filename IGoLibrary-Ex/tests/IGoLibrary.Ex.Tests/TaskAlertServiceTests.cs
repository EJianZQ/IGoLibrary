using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class TaskAlertServiceTests
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

        Assert.Equal("SMTP 用户名和邮箱授权码/密码需要同时填写，或同时留空", exception.Message);
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
                     && entry.Message.Contains("发送Cookie 过期提醒邮件失败：smtp boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotifyGrabSucceededAsync_SendsEmailUsingPersistedSettings()
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

        await service.NotifyGrabSucceededAsync("自科阅览区一", "2号座");

        var request = Assert.Single(emailSender.Requests);
        Assert.Equal("IGoLibrary-Ex 抢座成功提醒", request.Subject);
        Assert.Contains("目标场馆：自科阅览区一", request.Body);
        Assert.Contains("目标座位：2号座", request.Body);
    }

    [Fact]
    public async Task NotifyTaskFailedAsync_SendsEmailUsingPersistedSettings()
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

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        var request = Assert.Single(emailSender.Requests);
        Assert.Equal("IGoLibrary-Ex 抢座任务失败提醒", request.Subject);
        Assert.Contains("任务模块：抢座", request.Body);
        Assert.Contains("详细信息：预约请求超时", request.Body);
    }

    [Fact]
    public async Task NotifyTaskFailedAsync_FallsBackToInAppToast_WhenLocalAlertIsDisabled()
    {
        var notificationService = new FakeNotificationService();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            CookieExpiryAlerts = new CookieExpiryAlertSettings(
                CookieExpiryEmailAlertSettings.Default with { Enabled = false },
                new CookieExpiryLocalAlertSettings(false, false))
        });

        var service = CreateService(settingsService: settingsService, notificationService: notificationService);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        var warning = Assert.Single(notificationService.Warnings);
        Assert.Equal("抢座失败", warning.Title);
        Assert.Contains("预约请求超时", warning.Message);
    }

    [Fact]
    public async Task NotifyGrabSucceededAsync_DoesNotSuppressDifferentLibraries_WithSameSeatName()
    {
        var notificationService = new FakeNotificationService();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            CookieExpiryAlerts = new CookieExpiryAlertSettings(
                CookieExpiryEmailAlertSettings.Default with { Enabled = false },
                new CookieExpiryLocalAlertSettings(false, false))
        });

        var service = CreateService(settingsService: settingsService, notificationService: notificationService);

        await service.NotifyGrabSucceededAsync("一馆", "1号座");
        await service.NotifyGrabSucceededAsync("二馆", "1号座");

        Assert.Equal(2, notificationService.Successes.Count);
    }

    private static TaskAlertService CreateService(
        FakeSettingsService? settingsService = null,
        FakeEmailAlertSender? emailSender = null,
        ActivityLogService? activityLogService = null,
        INotificationService? notificationService = null)
    {
        settingsService ??= new FakeSettingsService(AppSettings.Default);
        var toastService = new ToastNotificationService(settingsService, new AppWindowService());

        return new TaskAlertService(
            settingsService,
            emailSender ?? new FakeEmailAlertSender(),
            toastService,
            notificationService ?? new FakeNotificationService(),
            new AlertSoundService(),
            activityLogService ?? new ActivityLogService());
    }
}

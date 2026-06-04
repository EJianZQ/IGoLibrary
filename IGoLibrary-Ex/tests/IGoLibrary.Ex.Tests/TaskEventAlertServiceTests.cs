using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class TaskEventAlertServiceTests
{
    [Fact]
    public async Task SendTestEmailAsync_ThrowsWhenOnlyUsernameIsProvided()
    {
        var service = CreateService();
        var settings = new EmailAlertChannelSettings(
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
    public async Task NotifySessionInvalidAsync_SendsEmailUsingPersistedSettings()
    {
        var emailSender = new FakeEmailAlertSender();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            TaskEventAlerts = new TaskEventAlertSettings(
                new EmailAlertChannelSettings(
                    Enabled: true,
                    SmtpHost: "smtp.example.com",
                    Port: 587,
                    SecurityMode: EmailSecurityMode.Tls,
                    Username: "tester",
                    Password: "secret",
                    FromAddress: "sender@example.com",
                    ToAddress: "receiver@example.com"),
                new LocalAlertChannelSettings(false, false))
        });

        var service = CreateService(settingsService, emailSender);

        await service.NotifySessionInvalidAsync("抢座轮询", "Cookie 无效");

        var request = Assert.Single(emailSender.Requests);
        Assert.Equal("IGoLibrary-Ex Cookie 失效提醒", request.Subject);
        Assert.Contains("触发模块：抢座轮询", request.Body);
        Assert.Contains("详细信息：Cookie 无效", request.Body);
    }

    [Fact]
    public async Task NotifySessionInvalidAsync_LogsWarningWhenEmailSendFails()
    {
        var emailSender = new FakeEmailAlertSender
        {
            SendException = new InvalidOperationException("smtp boom")
        };
        var activityLog = new ActivityLogService();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            TaskEventAlerts = new TaskEventAlertSettings(
                new EmailAlertChannelSettings(
                    Enabled: true,
                    SmtpHost: "smtp.example.com",
                    Port: 587,
                    SecurityMode: EmailSecurityMode.Tls,
                    Username: "tester",
                    Password: "secret",
                    FromAddress: "sender@example.com",
                    ToAddress: "receiver@example.com"),
                new LocalAlertChannelSettings(false, false))
        });

        var service = CreateService(settingsService, emailSender, activityLog);

        await service.NotifySessionInvalidAsync("占座轮询", "cookie expired");

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
            TaskEventAlerts = new TaskEventAlertSettings(
                new EmailAlertChannelSettings(
                    Enabled: true,
                    SmtpHost: "smtp.example.com",
                    Port: 587,
                    SecurityMode: EmailSecurityMode.Tls,
                    Username: "tester",
                    Password: "secret",
                    FromAddress: "sender@example.com",
                    ToAddress: "receiver@example.com"),
                new LocalAlertChannelSettings(false, false))
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
            TaskEventAlerts = new TaskEventAlertSettings(
                new EmailAlertChannelSettings(
                    Enabled: true,
                    SmtpHost: "smtp.example.com",
                    Port: 587,
                    SecurityMode: EmailSecurityMode.Tls,
                    Username: "tester",
                    Password: "secret",
                    FromAddress: "sender@example.com",
                    ToAddress: "receiver@example.com"),
                new LocalAlertChannelSettings(false, false))
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
            TaskEventAlerts = new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalAlertChannelSettings(false, false))
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
            TaskEventAlerts = new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalAlertChannelSettings(false, false))
        });

        var service = CreateService(settingsService: settingsService, notificationService: notificationService);

        await service.NotifyGrabSucceededAsync("一馆", "1号座");
        await service.NotifyGrabSucceededAsync("二馆", "1号座");

        Assert.Equal(2, notificationService.Successes.Count);
    }

    [Fact]
    public async Task NotifySessionInvalidAsync_SendsTelegramUsingPersistedSettings()
    {
        var telegramSender = new FakeTelegramAlertSender();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            TaskEventAlerts = new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalAlertChannelSettings(false, false),
                new TelegramAlertChannelSettings(true, "https://api.telegram.org", "token-1", "chat-1"))
        });

        var service = CreateService(settingsService: settingsService, telegramSender: telegramSender);

        await service.NotifySessionInvalidAsync("抢座轮询", "Cookie 无效");

        var request = Assert.Single(telegramSender.Requests);
        Assert.Equal("chat-1", request.Settings.ChatId);
        Assert.Contains("IGoLibrary-Ex Cookie 已失效", request.Message);
        Assert.Contains("触发模块：抢座轮询", request.Message);
        Assert.Contains("详细信息：Cookie 无效", request.Message);
    }

    [Fact]
    public async Task NotifyGrabSucceededAsync_SendsTelegramUsingPersistedSettings()
    {
        var telegramSender = new FakeTelegramAlertSender();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            TaskEventAlerts = new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalAlertChannelSettings(false, false),
                new TelegramAlertChannelSettings(true, "https://api.telegram.org", "token-1", "chat-1"))
        });

        var service = CreateService(settingsService: settingsService, telegramSender: telegramSender);

        await service.NotifyGrabSucceededAsync("自科阅览区一", "2号座");

        var request = Assert.Single(telegramSender.Requests);
        Assert.Contains("IGoLibrary-Ex 抢座成功", request.Message);
        Assert.Contains("目标场馆：自科阅览区一", request.Message);
        Assert.Contains("目标座位：2号座", request.Message);
    }

    [Fact]
    public async Task NotifyTaskFailedAsync_SendsTelegramUsingPersistedSettings()
    {
        var telegramSender = new FakeTelegramAlertSender();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            TaskEventAlerts = new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalAlertChannelSettings(false, false),
                new TelegramAlertChannelSettings(true, "https://api.telegram.org", "token-1", "chat-1"))
        });

        var service = CreateService(settingsService: settingsService, telegramSender: telegramSender);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        var request = Assert.Single(telegramSender.Requests);
        Assert.Contains("IGoLibrary-Ex 抢座任务失败", request.Message);
        Assert.Contains("任务模块：抢座", request.Message);
        Assert.Contains("详细信息：预约请求超时", request.Message);
    }

    [Fact]
    public async Task NotifyTaskFailedAsync_LogsWarningWhenTelegramSendFails_AndContinuesEmail()
    {
        var emailSender = new FakeEmailAlertSender();
        var telegramSender = new FakeTelegramAlertSender
        {
            SendException = new InvalidOperationException("telegram boom")
        };
        var activityLog = new ActivityLogService();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            TaskEventAlerts = new TaskEventAlertSettings(
                new EmailAlertChannelSettings(
                    Enabled: true,
                    SmtpHost: "smtp.example.com",
                    Port: 587,
                    SecurityMode: EmailSecurityMode.Tls,
                    Username: "tester",
                    Password: "secret",
                    FromAddress: "sender@example.com",
                    ToAddress: "receiver@example.com"),
                new LocalAlertChannelSettings(false, false),
                new TelegramAlertChannelSettings(true, "https://api.telegram.org", "token-1", "chat-1"))
        });

        var service = CreateService(settingsService, emailSender, activityLog, telegramSender: telegramSender);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        Assert.Single(emailSender.Requests);
        Assert.Contains(
            activityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning
                     && entry.Category == "Alert"
                     && entry.Message.Contains("发送抢座任务失败提醒Telegram提醒失败：telegram boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotifyTaskFailedAsync_SuppressesDuplicateTelegramWithinWindow()
    {
        var telegramSender = new FakeTelegramAlertSender();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            TaskEventAlerts = new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalAlertChannelSettings(false, false),
                new TelegramAlertChannelSettings(true, "https://api.telegram.org", "token-1", "chat-1"))
        });

        var service = CreateService(settingsService: settingsService, telegramSender: telegramSender);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");
        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        Assert.Single(telegramSender.Requests);
    }

    [Fact]
    public async Task NotifyGrabSucceededAsync_ShowsInAppFallbackBeforeSlowTelegramCompletes()
    {
        var notificationService = new FakeNotificationService();
        var telegramCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var telegramSender = new FakeTelegramAlertSender
        {
            SendCompletion = telegramCompletion
        };
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            TaskEventAlerts = new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalAlertChannelSettings(false, false),
                new TelegramAlertChannelSettings(true, "https://api.telegram.org", "token-1", "chat-1"))
        });
        var service = CreateService(
            settingsService: settingsService,
            notificationService: notificationService,
            telegramSender: telegramSender);

        var notifyTask = service.NotifyGrabSucceededAsync("自科阅览区一", "2号座");
        await WaitForAsync(() => notificationService.Successes.Count == 1);

        Assert.False(notifyTask.IsCompleted);
        telegramCompletion.SetResult();
        await notifyTask;
    }

    private static TaskEventAlertService CreateService(
        FakeSettingsService? settingsService = null,
        FakeEmailAlertSender? emailSender = null,
        ActivityLogService? activityLogService = null,
        INotificationService? notificationService = null,
        FakeTelegramAlertSender? telegramSender = null)
    {
        settingsService ??= new FakeSettingsService(AppSettings.Default);
        var toastService = new ToastNotificationService(settingsService, new AppWindowService());

        return new TaskEventAlertService(
            settingsService,
            emailSender ?? new FakeEmailAlertSender(),
            telegramSender ?? new FakeTelegramAlertSender(),
            toastService,
            notificationService ?? new FakeNotificationService(),
            new AlertSoundService(),
            activityLogService ?? new ActivityLogService());
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Condition was not met within the expected time.");
    }
}

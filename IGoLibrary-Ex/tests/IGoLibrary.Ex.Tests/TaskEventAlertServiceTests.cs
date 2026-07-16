using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class TaskEventAlertServiceTests
{
    public static TheoryData<
        TaskAlertTestEvent,
        string,
        string,
        string[],
        string[],
        ExpectedFallbackKind,
        string?,
        string?> AlertDispatchScenarios => new()
    {
        {
            TaskAlertTestEvent.CookieExpiring,
            "IGoLibrary-Ex Cookie 即将到期提醒",
            "Cookie 即将到期",
            ["IGoLibrary-Ex 检测到 Cookie 即将在 10 分钟内到期", "到期时间：", "剩余时间：9 分", "请尽快重新授权"],
            ["IGoLibrary-Ex Cookie 即将到期", "到期时间：", "剩余时间：9 分", "请尽快重新授权"],
            ExpectedFallbackKind.None,
            null,
            null
        },
        {
            TaskAlertTestEvent.SessionInvalid,
            "IGoLibrary-Ex Cookie 失效提醒",
            "Cookie 已失效",
            ["IGoLibrary-Ex 检测到 Cookie 已失效", "触发模块：抢座轮询", "详细信息：Cookie 无效", "请尽快重新授权，以恢复抢座/占座轮询"],
            ["IGoLibrary-Ex Cookie 已失效", "触发模块：抢座轮询", "详细信息：Cookie 无效", "请尽快重新授权，以恢复抢座/占座轮询"],
            ExpectedFallbackKind.None,
            null,
            null
        },
        {
            TaskAlertTestEvent.GrabSucceeded,
            "IGoLibrary-Ex 抢座成功提醒",
            "抢座成功",
            ["IGoLibrary-Ex 已成功预约到目标座位", "目标场馆：自科阅览区一", "目标座位：2号座", "你可以返回应用查看最新预约状态"],
            ["IGoLibrary-Ex 抢座成功", "目标场馆：自科阅览区一", "目标座位：2号座", "你可以返回应用查看最新预约状态"],
            ExpectedFallbackKind.Success,
            "抢座成功",
            "自科阅览区一 · 2号座 已成功预约"
        },
        {
            TaskAlertTestEvent.OccupyReReserveSucceeded,
            "IGoLibrary-Ex 占座成功提醒",
            "占座成功",
            ["IGoLibrary-Ex 已完成占座重新预约", "目标座位：2号座", "你可以返回应用查看最新预约状态"],
            ["IGoLibrary-Ex 占座成功", "目标座位：2号座", "你可以返回应用查看最新预约状态"],
            ExpectedFallbackKind.Success,
            "占座成功",
            "2号座 已重新预约"
        },
        {
            TaskAlertTestEvent.TomorrowReservationSucceeded,
            "IGoLibrary-Ex 明日预约成功提醒",
            "明日预约成功",
            ["IGoLibrary-Ex 已成功完成明日预约", "预约日期：明日", "目标场馆：自科阅览区一", "目标座位：2号座", "你可以返回应用查看明日预约任务状态"],
            ["IGoLibrary-Ex 明日预约成功", "预约日期：明日", "目标场馆：自科阅览区一", "目标座位：2号座", "你可以返回应用查看明日预约任务状态"],
            ExpectedFallbackKind.Success,
            "明日预约成功",
            "明日 · 自科阅览区一 · 2号座 已成功预约"
        },
        {
            TaskAlertTestEvent.GlobalLeakSucceeded,
            "IGoLibrary-Ex 全域捡漏成功提醒",
            "全域捡漏成功",
            ["IGoLibrary-Ex 已通过全域捡漏预约到空座", "目标场馆：自科阅览区一", "目标座位：2号座", "你可以返回应用查看最新预约状态"],
            ["IGoLibrary-Ex 全域捡漏成功", "目标场馆：自科阅览区一", "目标座位：2号座", "你可以返回应用查看最新预约状态"],
            ExpectedFallbackKind.Success,
            "全域捡漏成功",
            "自科阅览区一 · 2号座 已成功预约"
        },
        {
            TaskAlertTestEvent.TaskFailed,
            "IGoLibrary-Ex 抢座任务失败提醒",
            "抢座失败",
            ["IGoLibrary-Ex 检测到 抢座任务执行失败", "任务模块：抢座", "详细信息：预约请求超时", "请返回应用检查任务状态、授权信息与场馆配置"],
            ["IGoLibrary-Ex 抢座任务失败", "任务模块：抢座", "详细信息：预约请求超时", "请返回应用检查任务状态、授权信息与场馆配置"],
            ExpectedFallbackKind.Warning,
            "抢座失败",
            "抢座任务执行失败 详细信息：预约请求超时"
        }
    };

    [Fact]
    public async Task SendTestEmailAsync_ThrowsWhenOnlyUsernameIsProvided()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var service = new DesktopNotificationTestService(
            new FakeEmailAlertSender(),
            new FakeTelegramAlertSender(),
            new FakeBarkAlertSender(),
            new FakeWxPusherAlertSender(),
            new FakeServerChanAlertSender(),
            new ToastNotificationService(new AppWindowService()),
            new AlertSoundService());
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

    [Theory]
    [MemberData(nameof(AlertDispatchScenarios))]
    public async Task NotifyAsync_DispatchesEveryEventToAllEnabledChannels(
        TaskAlertTestEvent eventKind,
        string expectedEmailSubject,
        string expectedRemoteTitle,
        string[] expectedEmailFragments,
        string[] expectedRemoteFragments,
        ExpectedFallbackKind expectedFallbackKind,
        string? expectedFallbackTitle,
        string? expectedFallbackMessage)
    {
        var emailSender = new FakeEmailAlertSender();
        var telegramSender = new FakeTelegramAlertSender();
        var barkSender = new FakeBarkAlertSender();
        var wxPusherSender = new FakeWxPusherAlertSender();
        var serverChanSender = new FakeServerChanAlertSender();
        var notificationService = new FakeNotificationService();
        var alertSettings = CreateAllChannelsEnabledSettings(TaskEventAlertEventSettings.Default);
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(alertSettings));
        var service = CreateService(
            settingsService,
            emailSender,
            notificationService: notificationService,
            telegramSender: telegramSender,
            barkSender: barkSender,
            wxPusherSender: wxPusherSender,
            serverChanSender: serverChanSender);

        await NotifyEventAsync(service, eventKind);

        var emailRequest = Assert.Single(emailSender.Requests);
        Assert.Equal(expectedEmailSubject, emailRequest.Subject);
        Assert.Equal(alertSettings.Email, emailRequest.Settings);
        AssertPayloadContainsAll(emailRequest.Body, expectedEmailFragments);

        var telegramRequest = Assert.Single(telegramSender.Requests);
        Assert.Equal(alertSettings.Telegram, telegramRequest.Settings);
        AssertPayloadContainsAll(telegramRequest.Message, expectedRemoteFragments);

        var barkRequest = Assert.Single(barkSender.Requests);
        Assert.Equal(expectedRemoteTitle, barkRequest.Title);
        Assert.Equal(alertSettings.Bark, barkRequest.Settings);
        AssertPayloadContainsAll(barkRequest.Body, expectedRemoteFragments);

        var wxPusherRequest = Assert.Single(wxPusherSender.Requests);
        Assert.Equal(expectedRemoteTitle, wxPusherRequest.Title);
        Assert.Equal(alertSettings.WxPusher, wxPusherRequest.Settings);
        AssertPayloadContainsAll(wxPusherRequest.Body, expectedRemoteFragments);

        var serverChanRequest = Assert.Single(serverChanSender.Requests);
        Assert.Equal(expectedRemoteTitle, serverChanRequest.Title);
        Assert.Equal(alertSettings.ServerChan, serverChanRequest.Settings);
        AssertPayloadContainsAll(serverChanRequest.Body, expectedRemoteFragments);

        var fallbackCount = notificationService.Successes.Count
                            + notificationService.Warnings.Count
                            + notificationService.Infos.Count;
        if (expectedFallbackKind == ExpectedFallbackKind.None)
        {
            Assert.Equal(0, fallbackCount);
        }
        else
        {
            Assert.Equal(1, fallbackCount);
            var fallback = expectedFallbackKind switch
            {
                ExpectedFallbackKind.Success => Assert.Single(notificationService.Successes),
                ExpectedFallbackKind.Warning => Assert.Single(notificationService.Warnings),
                ExpectedFallbackKind.Info => Assert.Single(notificationService.Infos),
                _ => throw new InvalidOperationException("未知应用内提醒类型。")
            };
            Assert.Equal(expectedFallbackTitle, fallback.Title);
            Assert.Equal(expectedFallbackMessage, fallback.Message);
        }
    }

    private static void AssertPayloadContainsAll(string payload, IEnumerable<string> expectedFragments)
    {
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, payload, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task NotifySessionInvalidAsync_LogsWarningWhenEmailSendFails()
    {
        var emailSender = new FakeEmailAlertSender
        {
            SendException = new InvalidOperationException("smtp boom")
        };
        var activityLog = new ActivityLogService();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                new EmailAlertChannelSettings(
                    Enabled: true,
                    SmtpHost: "smtp.example.com",
                    Port: 587,
                    SecurityMode: EmailSecurityMode.Tls,
                    Username: "tester",
                    Password: "secret",
                    FromAddress: "sender@example.com",
                    ToAddress: "receiver@example.com"),
                new LocalDesktopAlertSettings(false, false))));

        var service = CreateService(settingsService, emailSender, activityLog);

        await service.NotifySessionInvalidAsync("占座轮询", "cookie expired");

        Assert.Contains(
            activityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning
                     && entry.Category == "Alert"
                     && entry.Message.Contains("发送Cookie 过期提醒邮件失败：smtp boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotifyTaskFailedAsync_FallsBackToInAppToast_WhenLocalAlertIsDisabled()
    {
        var notificationService = new FakeNotificationService();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalDesktopAlertSettings(false, false))));

        var service = CreateService(settingsService: settingsService, notificationService: notificationService);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        var warning = Assert.Single(notificationService.Warnings);
        Assert.Equal("抢座失败", warning.Title);
        Assert.Contains("预约请求超时", warning.Message);
    }

    [Fact]
    public async Task NotifyAsync_DisabledEventsDoNotSendAnyChannel()
    {
        foreach (var eventKind in new[]
                 {
                     TaskAlertTestEvent.CookieExpiring,
                     TaskAlertTestEvent.SessionInvalid,
                     TaskAlertTestEvent.GrabSucceeded,
                     TaskAlertTestEvent.OccupyReReserveSucceeded,
                     TaskAlertTestEvent.TomorrowReservationSucceeded,
                     TaskAlertTestEvent.GlobalLeakSucceeded,
                     TaskAlertTestEvent.TaskFailed
                 })
        {
            await NotifyAsync_DoesNotSendAnyChannel_WhenEventIsDisabled(eventKind);
        }
    }

    private async Task NotifyAsync_DoesNotSendAnyChannel_WhenEventIsDisabled(TaskAlertTestEvent eventKind)
    {
        var emailSender = new FakeEmailAlertSender();
        var telegramSender = new FakeTelegramAlertSender();
        var barkSender = new FakeBarkAlertSender();
        var wxPusherSender = new FakeWxPusherAlertSender();
        var serverChanSender = new FakeServerChanAlertSender();
        var notificationService = new FakeNotificationService();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            CreateAllChannelsEnabledSettings(DisableEvent(eventKind))));
        var service = CreateService(
            settingsService,
            emailSender,
            notificationService: notificationService,
            telegramSender: telegramSender,
            barkSender: barkSender,
            wxPusherSender: wxPusherSender,
            serverChanSender: serverChanSender);

        await NotifyEventAsync(service, eventKind);

        Assert.Empty(emailSender.Requests);
        Assert.Empty(telegramSender.Requests);
        Assert.Empty(barkSender.Requests);
        Assert.Empty(wxPusherSender.Requests);
        Assert.Empty(serverChanSender.Requests);
        Assert.Empty(notificationService.Successes);
        Assert.Empty(notificationService.Warnings);
        Assert.Empty(notificationService.Infos);
    }

    [Fact]
    public async Task TryNotifyCookieExpiringAsync_ReturnsFalseWhenEventIsDisabled()
    {
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            CreateAllChannelsEnabledSettings(
                TaskEventAlertEventSettings.Default with { CookieExpiring = false })));
        var service = CreateService(settingsService: settingsService);

        var accepted = await service.TryNotifyCookieExpiringAsync(
            DateTimeOffset.Now.AddMinutes(5),
            TimeSpan.FromMinutes(5));

        Assert.False(accepted);
    }

    [Fact]
    public async Task TryNotifyCookieExpiringAsync_DoesNotUseUnconfiguredInAppFallback()
    {
        var notificationService = new FakeNotificationService();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalDesktopAlertSettings(false, false))));
        var service = CreateService(
            settingsService: settingsService,
            notificationService: notificationService);

        var accepted = await service.TryNotifyCookieExpiringAsync(
            DateTimeOffset.Now.AddMinutes(5),
            TimeSpan.FromMinutes(5));

        Assert.True(accepted);
        Assert.Empty(notificationService.Successes);
        Assert.Empty(notificationService.Warnings);
        Assert.Empty(notificationService.Infos);
    }

    [Fact]
    public async Task TryNotifyCookieExpiringAsync_LeavesPerCookieDeduplicationToMonitor()
    {
        var telegramSender = new FakeTelegramAlertSender();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalDesktopAlertSettings(false, false),
                new TelegramAlertChannelSettings(true, "https://api.telegram.org", "token-1", "chat-1"))));
        var service = CreateService(
            settingsService: settingsService,
            telegramSender: telegramSender);
        var expirationTime = DateTimeOffset.Now.AddMinutes(5);

        await service.TryNotifyCookieExpiringAsync(expirationTime, TimeSpan.FromMinutes(5));
        await service.TryNotifyCookieExpiringAsync(expirationTime, TimeSpan.FromMinutes(5));

        Assert.Equal(2, telegramSender.Requests.Count);
    }

    [Theory]
    [InlineData(RemoteCancellationChannel.Email)]
    [InlineData(RemoteCancellationChannel.Telegram)]
    [InlineData(RemoteCancellationChannel.Bark)]
    [InlineData(RemoteCancellationChannel.WxPusher)]
    [InlineData(RemoteCancellationChannel.ServerChan)]
    public async Task TryNotifyCookieExpiringAsync_PropagatesCallerCancellationFromRemoteChannel(
        RemoteCancellationChannel channel)
    {
        var sender = new BlockingTaskEventAlertSender();
        var activityLog = new ActivityLogService();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            CreateSingleRemoteChannelEnabledSettings(channel)));
        var service = CreateService(
            settingsService,
            sender,
            activityLog,
            telegramSender: sender,
            barkSender: sender,
            wxPusherSender: sender,
            serverChanSender: sender);
        using var cancellationSource = new CancellationTokenSource();

        var notifyTask = service.TryNotifyCookieExpiringAsync(
            DateTimeOffset.Now.AddMinutes(5),
            TimeSpan.FromMinutes(5),
            cancellationSource.Token);
        await sender.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await notifyTask);
        Assert.DoesNotContain(
            activityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning && entry.Category == "Alert");
    }

    [Fact]
    public async Task NotifyAsync_DisabledEventDoesNotSuppressEnabledEvent()
    {
        var notificationService = new FakeNotificationService();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalDesktopAlertSettings(false, false),
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default with
                {
                    GrabSucceeded = false
                })));
        var service = CreateService(settingsService: settingsService, notificationService: notificationService);

        await service.NotifyGrabSucceededAsync("自科阅览区一", "2号座");
        await service.NotifyGlobalLeakSucceededAsync("自科阅览区一", "2号座");

        var success = Assert.Single(notificationService.Successes);
        Assert.Equal("全域捡漏成功", success.Title);
    }

    [Fact]
    public async Task NotifyGrabSucceededAsync_DoesNotSuppressDifferentLibraries_WithSameSeatName()
    {
        var notificationService = new FakeNotificationService();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalDesktopAlertSettings(false, false))));

        var service = CreateService(settingsService: settingsService, notificationService: notificationService);

        await service.NotifyGrabSucceededAsync("一馆", "1号座");
        await service.NotifyGrabSucceededAsync("二馆", "1号座");

        Assert.Equal(2, notificationService.Successes.Count);
    }




    [Fact]
    public async Task NotifyTaskFailedAsync_IsolatesAndLogsEveryRemoteChannelFailure()
    {
        await NotifyTaskFailedAsync_LogsWarningWhenTelegramSendFails_AndContinuesEmail();
        await NotifyTaskFailedAsync_LogsWarningWhenBarkSendFails_AndContinuesEmail();
        await NotifyTaskFailedAsync_LogsWarningWhenWxPusherSendFails_AndContinuesEmail();
        await NotifyTaskFailedAsync_LogsWarningWhenServerChanSendFails_AndContinuesEmail();
    }

    private async Task NotifyTaskFailedAsync_LogsWarningWhenTelegramSendFails_AndContinuesEmail()
    {
        var emailSender = new FakeEmailAlertSender();
        var telegramSender = new FakeTelegramAlertSender
        {
            SendException = new InvalidOperationException("telegram boom")
        };
        var activityLog = new ActivityLogService();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                new EmailAlertChannelSettings(
                    Enabled: true,
                    SmtpHost: "smtp.example.com",
                    Port: 587,
                    SecurityMode: EmailSecurityMode.Tls,
                    Username: "tester",
                    Password: "secret",
                    FromAddress: "sender@example.com",
                    ToAddress: "receiver@example.com"),
                new LocalDesktopAlertSettings(false, false),
                new TelegramAlertChannelSettings(true, "https://api.telegram.org", "token-1", "chat-1"))));

        var service = CreateService(settingsService, emailSender, activityLog, telegramSender: telegramSender);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        Assert.Single(emailSender.Requests);
        Assert.Contains(
            activityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning
                     && entry.Category == "Alert"
                     && entry.Message.Contains("发送抢座任务失败提醒Telegram提醒失败：telegram boom", StringComparison.Ordinal));
    }

    private async Task NotifyTaskFailedAsync_LogsWarningWhenBarkSendFails_AndContinuesEmail()
    {
        var emailSender = new FakeEmailAlertSender();
        var barkSender = new FakeBarkAlertSender
        {
            SendException = new InvalidOperationException("bark boom")
        };
        var activityLog = new ActivityLogService();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                new EmailAlertChannelSettings(
                    Enabled: true,
                    SmtpHost: "smtp.example.com",
                    Port: 587,
                    SecurityMode: EmailSecurityMode.Tls,
                    Username: "tester",
                    Password: "secret",
                    FromAddress: "sender@example.com",
                    ToAddress: "receiver@example.com"),
                new LocalDesktopAlertSettings(false, false),
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default,
                new BarkAlertChannelSettings(true, "https://api.day.app", "bark-key", "IGoLibrary-Ex", "alarm", "critical"))));

        var service = CreateService(settingsService, emailSender, activityLog, barkSender: barkSender);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        Assert.Single(emailSender.Requests);
        Assert.Contains(
            activityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning
                     && entry.Category == "Alert"
                     && entry.Message.Contains("发送抢座任务失败提醒Bark提醒失败：bark boom", StringComparison.Ordinal));
    }

    private async Task NotifyTaskFailedAsync_LogsWarningWhenWxPusherSendFails_AndContinuesEmail()
    {
        var emailSender = new FakeEmailAlertSender();
        var wxPusherSender = new FakeWxPusherAlertSender
        {
            SendException = new InvalidOperationException("wxpusher boom")
        };
        var activityLog = new ActivityLogService();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                new EmailAlertChannelSettings(
                    Enabled: true,
                    SmtpHost: "smtp.example.com",
                    Port: 587,
                    SecurityMode: EmailSecurityMode.Tls,
                    Username: "tester",
                    Password: "secret",
                    FromAddress: "sender@example.com",
                    ToAddress: "receiver@example.com"),
                new LocalDesktopAlertSettings(false, false),
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default,
                BarkAlertChannelSettings.Default,
                new WxPusherAlertChannelSettings(true, "https://wxpusher.zjiecode.com", "AT_xxx", "UID_xxx", ""))));

        var service = CreateService(
            settingsService,
            emailSender,
            activityLog,
            wxPusherSender: wxPusherSender);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        Assert.Single(emailSender.Requests);
        Assert.Contains(
            activityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning
                     && entry.Category == "Alert"
                     && entry.Message.Contains("发送抢座任务失败提醒WxPusher提醒失败：wxpusher boom", StringComparison.Ordinal));
    }

    private async Task NotifyTaskFailedAsync_LogsWarningWhenServerChanSendFails_AndContinuesEmail()
    {
        var emailSender = new FakeEmailAlertSender();
        var serverChanSender = new FakeServerChanAlertSender
        {
            SendException = new InvalidOperationException("serverchan boom")
        };
        var activityLog = new ActivityLogService();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                new EmailAlertChannelSettings(
                    Enabled: true,
                    SmtpHost: "smtp.example.com",
                    Port: 587,
                    SecurityMode: EmailSecurityMode.Tls,
                    Username: "tester",
                    Password: "secret",
                    FromAddress: "sender@example.com",
                    ToAddress: "receiver@example.com"),
                new LocalDesktopAlertSettings(false, false),
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default,
                BarkAlertChannelSettings.Default,
                WxPusherAlertChannelSettings.Default,
                new ServerChanAlertChannelSettings(true, "SCT_xxx", false, "", ""))));

        var service = CreateService(
            settingsService,
            emailSender,
            activityLog,
            serverChanSender: serverChanSender);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        Assert.Single(emailSender.Requests);
        Assert.Contains(
            activityLog.Entries,
            entry => entry.Kind == LogEntryKind.Warning
                     && entry.Category == "Alert"
                     && entry.Message.Contains("发送抢座任务失败提醒Server酱提醒失败：serverchan boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotifyTaskFailedAsync_SuppressesDuplicatesBeforeEveryRemoteChannelDispatch()
    {
        await NotifyTaskFailedAsync_SuppressesDuplicateTelegramWithinWindow();
        await NotifyTaskFailedAsync_SuppressesDuplicateBarkWithinWindow();
        await NotifyTaskFailedAsync_SuppressesDuplicateWxPusherWithinWindow();
        await NotifyTaskFailedAsync_SuppressesDuplicateServerChanWithinWindow();
    }

    private async Task NotifyTaskFailedAsync_SuppressesDuplicateTelegramWithinWindow()
    {
        var telegramSender = new FakeTelegramAlertSender();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalDesktopAlertSettings(false, false),
                new TelegramAlertChannelSettings(true, "https://api.telegram.org", "token-1", "chat-1"))));

        var service = CreateService(settingsService: settingsService, telegramSender: telegramSender);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");
        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        Assert.Single(telegramSender.Requests);
    }

    private async Task NotifyTaskFailedAsync_SuppressesDuplicateBarkWithinWindow()
    {
        var barkSender = new FakeBarkAlertSender();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalDesktopAlertSettings(false, false),
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default,
                new BarkAlertChannelSettings(true, "https://api.day.app", "bark-key", "IGoLibrary-Ex", "alarm", "critical"))));

        var service = CreateService(settingsService: settingsService, barkSender: barkSender);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");
        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        Assert.Single(barkSender.Requests);
    }

    private async Task NotifyTaskFailedAsync_SuppressesDuplicateWxPusherWithinWindow()
    {
        var wxPusherSender = new FakeWxPusherAlertSender();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalDesktopAlertSettings(false, false),
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default,
                BarkAlertChannelSettings.Default,
                new WxPusherAlertChannelSettings(true, "https://wxpusher.zjiecode.com", "AT_xxx", "UID_xxx", ""))));

        var service = CreateService(settingsService: settingsService, wxPusherSender: wxPusherSender);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");
        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        Assert.Single(wxPusherSender.Requests);
    }

    private async Task NotifyTaskFailedAsync_SuppressesDuplicateServerChanWithinWindow()
    {
        var serverChanSender = new FakeServerChanAlertSender();
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalDesktopAlertSettings(false, false),
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default,
                BarkAlertChannelSettings.Default,
                WxPusherAlertChannelSettings.Default,
                new ServerChanAlertChannelSettings(true, "SCT_xxx", false, "", ""))));

        var service = CreateService(settingsService: settingsService, serverChanSender: serverChanSender);

        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");
        await service.NotifyTaskFailedAsync("抢座", "预约请求超时");

        Assert.Single(serverChanSender.Requests);
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
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default with { Enabled = false },
                new LocalDesktopAlertSettings(false, false),
                new TelegramAlertChannelSettings(true, "https://api.telegram.org", "token-1", "chat-1"))));
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
        IEmailAlertSender? emailSender = null,
        ActivityLogService? activityLogService = null,
        INotificationService? notificationService = null,
        ITelegramAlertSender? telegramSender = null,
        IBarkAlertSender? barkSender = null,
        IWxPusherAlertSender? wxPusherSender = null,
        IServerChanAlertSender? serverChanSender = null)
    {
        settingsService ??= new FakeSettingsService(AppSettings.Default);
        var toastService = new ToastNotificationService(new AppWindowService());

        return new TaskEventAlertService(
            settingsService,
            emailSender ?? new FakeEmailAlertSender(),
            telegramSender ?? new FakeTelegramAlertSender(),
            barkSender ?? new FakeBarkAlertSender(),
            wxPusherSender ?? new FakeWxPusherAlertSender(),
            serverChanSender ?? new FakeServerChanAlertSender(),
            toastService,
            notificationService ?? new FakeNotificationService(),
            new AlertSoundService(),
            activityLogService ?? new ActivityLogService());
    }

    private static TaskEventAlertSettings CreateSingleRemoteChannelEnabledSettings(
        RemoteCancellationChannel channel)
    {
        return new TaskEventAlertSettings(
            new EmailAlertChannelSettings(
                Enabled: channel == RemoteCancellationChannel.Email,
                SmtpHost: "smtp.example.com",
                Port: 587,
                SecurityMode: EmailSecurityMode.Tls,
                Username: "tester",
                Password: "secret",
                FromAddress: "sender@example.com",
                ToAddress: "receiver@example.com"),
            new LocalDesktopAlertSettings(false, false),
            new TelegramAlertChannelSettings(
                channel == RemoteCancellationChannel.Telegram,
                "https://api.telegram.org",
                "token-1",
                "chat-1"),
            TaskEventAlertEventSettings.Default,
            new BarkAlertChannelSettings(
                channel == RemoteCancellationChannel.Bark,
                "https://api.day.app",
                "bark-key",
                "IGoLibrary-Ex",
                "alarm",
                "timeSensitive"),
            new WxPusherAlertChannelSettings(
                channel == RemoteCancellationChannel.WxPusher,
                "https://wxpusher.zjiecode.com",
                "AT_xxx",
                "UID_xxx",
                string.Empty),
            new ServerChanAlertChannelSettings(
                channel == RemoteCancellationChannel.ServerChan,
                "SCT_xxx",
                false,
                string.Empty,
                string.Empty));
    }

    private static AppSettings WithTaskEventAlerts(TaskEventAlertSettings alerts)
        => AppSettings.Default with
        {
            Notifications = AppSettings.Default.Notifications with
            {
                TaskEventAlerts = alerts
            }
        };

    private static TaskEventAlertSettings CreateAllChannelsEnabledSettings(TaskEventAlertEventSettings events)
    {
        return new TaskEventAlertSettings(
            new EmailAlertChannelSettings(
                Enabled: true,
                SmtpHost: "smtp.example.com",
                Port: 587,
                SecurityMode: EmailSecurityMode.Tls,
                Username: "tester",
                Password: "secret",
                FromAddress: "sender@example.com",
                ToAddress: "receiver@example.com"),
            new LocalDesktopAlertSettings(false, false),
            new TelegramAlertChannelSettings(true, "https://api.telegram.org", "token-1", "chat-1"),
            events,
            new BarkAlertChannelSettings(true, "https://api.day.app", "bark-key", "IGoLibrary-Ex", "alarm", "timeSensitive"),
            new WxPusherAlertChannelSettings(true, "https://wxpusher.zjiecode.com", "AT_xxx", "UID_xxx", ""),
            new ServerChanAlertChannelSettings(true, "SCT_xxx", true, "9|66", "user-1"));
    }

    private static TaskEventAlertEventSettings DisableEvent(TaskAlertTestEvent eventKind)
    {
        return eventKind switch
        {
            TaskAlertTestEvent.CookieExpiring => TaskEventAlertEventSettings.Default with { CookieExpiring = false },
            TaskAlertTestEvent.SessionInvalid => TaskEventAlertEventSettings.Default with { SessionInvalid = false },
            TaskAlertTestEvent.GrabSucceeded => TaskEventAlertEventSettings.Default with { GrabSucceeded = false },
            TaskAlertTestEvent.OccupyReReserveSucceeded => TaskEventAlertEventSettings.Default with { OccupyReReserveSucceeded = false },
            TaskAlertTestEvent.TomorrowReservationSucceeded => TaskEventAlertEventSettings.Default with { TomorrowReservationSucceeded = false },
            TaskAlertTestEvent.GlobalLeakSucceeded => TaskEventAlertEventSettings.Default with { GlobalLeakSucceeded = false },
            TaskAlertTestEvent.TaskFailed => TaskEventAlertEventSettings.Default with { TaskFailed = false },
            _ => TaskEventAlertEventSettings.Default
        };
    }

    private static Task NotifyEventAsync(TaskEventAlertService service, TaskAlertTestEvent eventKind)
    {
        return eventKind switch
        {
            TaskAlertTestEvent.CookieExpiring => IgnoreResultAsync(
                service.TryNotifyCookieExpiringAsync(
                    DateTimeOffset.Now.AddMinutes(9).AddSeconds(30),
                    TimeSpan.FromMinutes(9.5))),
            TaskAlertTestEvent.SessionInvalid => service.NotifySessionInvalidAsync("抢座轮询", "Cookie 无效"),
            TaskAlertTestEvent.GrabSucceeded => service.NotifyGrabSucceededAsync("自科阅览区一", "2号座"),
            TaskAlertTestEvent.OccupyReReserveSucceeded => service.NotifyOccupyReReserveSucceededAsync("2号座"),
            TaskAlertTestEvent.TomorrowReservationSucceeded => service.NotifyTomorrowReservationSucceededAsync("自科阅览区一", "2号座", "明日"),
            TaskAlertTestEvent.GlobalLeakSucceeded => service.NotifyGlobalLeakSucceededAsync("自科阅览区一", "2号座"),
            TaskAlertTestEvent.TaskFailed => service.NotifyTaskFailedAsync("抢座", "预约请求超时"),
            _ => Task.CompletedTask
        };
    }

    private static async Task IgnoreResultAsync(Task<bool> task)
    {
        _ = await task;
    }

    public enum TaskAlertTestEvent
    {
        CookieExpiring,
        SessionInvalid,
        GrabSucceeded,
        OccupyReReserveSucceeded,
        TomorrowReservationSucceeded,
        GlobalLeakSucceeded,
        TaskFailed
    }

    public enum ExpectedFallbackKind
    {
        None,
        Success,
        Warning,
        Info
    }

    public enum RemoteCancellationChannel
    {
        Email,
        Telegram,
        Bark,
        WxPusher,
        ServerChan
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

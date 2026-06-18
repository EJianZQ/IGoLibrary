using System.Net.Mail;
using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class DesktopNotificationTestService(
    IEmailAlertSender emailAlertSender,
    ITelegramAlertSender telegramAlertSender,
    IBarkAlertSender barkAlertSender,
    ToastNotificationService toastNotificationService,
    AlertSoundService alertSoundService) : INotificationTestService
{
    public async Task SendTestEmailAsync(
        EmailAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        ValidateEmailSettings(settings);
        await emailAlertSender.SendAsync(
            settings,
            subject: "IGoLibrary-Ex 测试邮件",
            body:
            """
            这是一封来自 IGoLibrary-Ex 的测试邮件。

            如果你收到了这封邮件，说明当前 SMTP 参数已经可以正常工作，
            可用于 Cookie 失效、抢座成功、占座成功、明日预约成功、空座出现、签到提醒和任务失败提醒。
            """,
            cancellationToken);
    }

    public Task SendTestTelegramAsync(
        TelegramAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return telegramAlertSender.SendAsync(
            settings,
            """
            这是一条来自 IGoLibrary-Ex 的 Telegram 测试消息。

            如果你收到了这条消息，说明当前 Bot Token、Chat ID 和 API 地址已经可以正常工作，
            可用于 Cookie 失效、抢座成功、占座成功、明日预约成功、空座出现、签到提醒和任务失败提醒。
            """,
            cancellationToken);
    }

    public Task SendTestBarkAsync(
        BarkAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return barkAlertSender.SendAsync(
            settings,
            "IGoLibrary-Ex Bark 测试",
            """
            这是一条来自 IGoLibrary-Ex 的 Bark 测试消息。

            如果你收到了这条消息，说明当前 Bark 地址和 Device Key 已经可以正常工作，
            可用于 Cookie 失效、抢座成功、占座成功、明日预约成功、空座出现、签到提醒和任务失败提醒。
            """,
            cancellationToken);
    }

    public async Task SendTestLocalAlertAsync(
        LocalDesktopAlertSettings settings,
        CancellationToken cancellationToken = default)
    {
        await toastNotificationService.ShowForcedAsync(
            ToastVisualKind.Info,
            "任务提醒测试通知",
            "这是一条测试通知，用于确认 Cookie 失效、抢座成功、占座成功、明日预约成功、空座出现、签到提醒和任务失败提醒的弹窗与提示音效果",
            cancellationToken);

        if (settings.SoundEnabled)
        {
            await alertSoundService.PlayAsync(cancellationToken);
        }
    }

    private static void ValidateEmailSettings(EmailAlertChannelSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            throw new InvalidOperationException("请填写 SMTP 服务器地址");
        }

        if (settings.Port <= 0 || settings.Port > 65535)
        {
            throw new InvalidOperationException("SMTP 端口必须在 1 到 65535 之间");
        }

        if (string.IsNullOrWhiteSpace(settings.FromAddress))
        {
            throw new InvalidOperationException("请填写发信人邮箱地址");
        }

        if (string.IsNullOrWhiteSpace(settings.ToAddress))
        {
            throw new InvalidOperationException("请填写收信人邮箱地址");
        }

        var hasUsername = !string.IsNullOrWhiteSpace(settings.Username);
        var hasPassword = !string.IsNullOrWhiteSpace(settings.Password);
        if (hasUsername != hasPassword)
        {
            throw new InvalidOperationException("SMTP 用户名和邮箱授权码/密码需要同时填写，或同时留空");
        }

        _ = new MailAddress(settings.FromAddress);
        _ = new MailAddress(settings.ToAddress);
    }
}

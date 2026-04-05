using System.Net.Mail;
using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class CookieExpiryAlertService(
    ISettingsService settingsService,
    IEmailAlertSender emailAlertSender,
    ToastNotificationService toastNotificationService,
    AlertSoundService alertSoundService,
    IActivityLogService activityLogService) : ICookieExpiryAlertService
{
    private readonly object _gate = new();
    private string? _lastAlertKey;
    private DateTimeOffset _lastAlertAt = DateTimeOffset.MinValue;

    public async Task NotifyCookieExpiredAsync(string source, string reason, CancellationToken cancellationToken = default)
    {
        if (ShouldSuppress(source, reason))
        {
            return;
        }

        var settings = await settingsService.LoadAsync(cancellationToken);
        var alertSettings = settings.CookieExpiryAlerts ?? CookieExpiryAlertSettings.Default;
        var title = "Cookie 已失效";
        var message = $"{source} 检测到当前 Cookie 已失效，请重新授权。";
        var detailMessage = string.IsNullOrWhiteSpace(reason)
            ? message
            : $"{message} 详细信息：{reason}";

        if (alertSettings.Email.Enabled)
        {
            try
            {
                ValidateEmailSettings(alertSettings.Email);
                await emailAlertSender.SendAsync(
                    alertSettings.Email,
                    subject: "IGoLibrary-Ex Cookie 失效提醒",
                    body: BuildEmailBody(source, reason),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送 Cookie 过期提醒邮件失败：{ex.Message}");
            }
        }

        if (alertSettings.Local.ToastEnabled)
        {
            try
            {
                await toastNotificationService.ShowForcedAsync(
                    ToastVisualKind.Warning,
                    title,
                    detailMessage,
                    cancellationToken);

                if (alertSettings.Local.SoundEnabled)
                {
                    await alertSoundService.PlayAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                activityLogService.Write(LogEntryKind.Warning, "Alert", $"展示 Cookie 过期屏幕提醒失败：{ex.Message}");
            }
        }
    }

    public async Task SendTestEmailAsync(CookieExpiryEmailAlertSettings settings, CancellationToken cancellationToken = default)
    {
        ValidateEmailSettings(settings);
        await emailAlertSender.SendAsync(
            settings,
            subject: "IGoLibrary-Ex 测试邮件",
            body: BuildTestEmailBody(),
            cancellationToken);
    }

    public async Task SendTestLocalAlertAsync(CookieExpiryLocalAlertSettings settings, CancellationToken cancellationToken = default)
    {
        await toastNotificationService.ShowForcedAsync(
            ToastVisualKind.Warning,
            "Cookie 过期测试提醒",
            "这是一条测试通知，用于确认右下角自定义弹窗和提示音效果。",
            cancellationToken);

        if (settings.SoundEnabled)
        {
            await alertSoundService.PlayAsync(cancellationToken);
        }
    }

    private bool ShouldSuppress(string source, string reason)
    {
        var key = $"{source}|{reason}";
        var now = DateTimeOffset.Now;

        lock (_gate)
        {
            if (_lastAlertKey == key && now - _lastAlertAt < TimeSpan.FromSeconds(15))
            {
                return true;
            }

            _lastAlertKey = key;
            _lastAlertAt = now;
            return false;
        }
    }

    private static void ValidateEmailSettings(CookieExpiryEmailAlertSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            throw new InvalidOperationException("请填写 SMTP 服务器地址。");
        }

        if (settings.Port <= 0 || settings.Port > 65535)
        {
            throw new InvalidOperationException("SMTP 端口必须在 1 到 65535 之间。");
        }

        if (string.IsNullOrWhiteSpace(settings.FromAddress))
        {
            throw new InvalidOperationException("请填写发信人邮箱地址。");
        }

        if (string.IsNullOrWhiteSpace(settings.ToAddress))
        {
            throw new InvalidOperationException("请填写收信人邮箱地址。");
        }

        var hasUsername = !string.IsNullOrWhiteSpace(settings.Username);
        var hasPassword = !string.IsNullOrWhiteSpace(settings.Password);
        if (hasUsername != hasPassword)
        {
            throw new InvalidOperationException("SMTP 用户名和邮箱授权码/密码需要同时填写，或同时留空。");
        }

        _ = new MailAddress(settings.FromAddress);
        _ = new MailAddress(settings.ToAddress);
    }

    private static string BuildEmailBody(string source, string reason)
    {
        var builder = new StringBuilder();
        builder.AppendLine("IGoLibrary-Ex 检测到 Cookie 已失效。");
        builder.AppendLine();
        builder.AppendLine($"触发模块：{source}");
        builder.AppendLine($"触发时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.AppendLine($"详细信息：{reason}");
        }

        builder.AppendLine();
        builder.AppendLine("请尽快重新授权，以恢复抢座/占座轮询。");
        return builder.ToString();
    }

    private static string BuildTestEmailBody()
    {
        return
            """
            这是一封来自 IGoLibrary-Ex 的测试邮件。

            如果你收到了这封邮件，说明当前 SMTP 参数已经可以正常工作。
            """;
    }
}

using System.Net.Mail;
using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class TaskAlertService(
    ISettingsService settingsService,
    IEmailAlertSender emailAlertSender,
    ToastNotificationService toastNotificationService,
    INotificationService notificationService,
    AlertSoundService alertSoundService,
    IActivityLogService activityLogService) : ITaskAlertService
{
    private readonly object _gate = new();
    private string? _lastAlertKey;
    private DateTimeOffset _lastAlertAt = DateTimeOffset.MinValue;

    public async Task NotifyCookieExpiredAsync(string source, string reason, CancellationToken cancellationToken = default)
    {
        if (ShouldSuppress($"cookie-expired|{source}|{reason}"))
        {
            return;
        }

        var title = "Cookie 已失效";
        var message = $"{source} 检测到当前 Cookie 已失效，请重新授权";
        var detailMessage = AppendDetail(message, reason);

        await DispatchAlertAsync(
            emailLabel: "Cookie 过期提醒",
            localLabel: "Cookie 过期提醒",
            emailSubject: "IGoLibrary-Ex Cookie 失效提醒",
            emailBody: BuildCookieExpiredEmailBody(source, reason),
            toastKind: ToastVisualKind.Warning,
            toastTitle: title,
            toastMessage: detailMessage,
            enableInAppFallback: false,
            cancellationToken);
    }

    public async Task NotifyGrabSucceededAsync(string libraryName, string seatName, CancellationToken cancellationToken = default)
    {
        var normalizedLibraryName = NormalizeLibraryName(libraryName);
        var normalizedSeatName = NormalizeSeatName(seatName);
        if (ShouldSuppress($"grab-success|{normalizedLibraryName}|{normalizedSeatName}"))
        {
            return;
        }

        await DispatchAlertAsync(
            emailLabel: "抢座成功提醒",
            localLabel: "抢座成功提醒",
            emailSubject: "IGoLibrary-Ex 抢座成功提醒",
            emailBody: BuildGrabSucceededEmailBody(normalizedLibraryName, normalizedSeatName),
            toastKind: ToastVisualKind.Success,
            toastTitle: "抢座成功",
            toastMessage: $"{normalizedLibraryName} · {normalizedSeatName} 已成功预约",
            enableInAppFallback: true,
            cancellationToken);
    }

    public async Task NotifyTaskFailedAsync(string taskName, string reason, CancellationToken cancellationToken = default)
    {
        var normalizedTaskName = NormalizeTaskName(taskName);
        if (ShouldSuppress($"task-failed|{normalizedTaskName}|{reason}"))
        {
            return;
        }

        var message = $"{normalizedTaskName}任务执行失败。";
        await DispatchAlertAsync(
            emailLabel: $"{normalizedTaskName}任务失败提醒",
            localLabel: $"{normalizedTaskName}任务失败提醒",
            emailSubject: $"IGoLibrary-Ex {normalizedTaskName}任务失败提醒",
            emailBody: BuildTaskFailedEmailBody(normalizedTaskName, reason),
            toastKind: ToastVisualKind.Warning,
            toastTitle: $"{normalizedTaskName}失败",
            toastMessage: AppendDetail(message, reason),
            enableInAppFallback: true,
            cancellationToken);
    }

    private async Task DispatchAlertAsync(
        string emailLabel,
        string localLabel,
        string emailSubject,
        string emailBody,
        ToastVisualKind toastKind,
        string toastTitle,
        string toastMessage,
        bool enableInAppFallback,
        CancellationToken cancellationToken)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        var alertSettings = settings.CookieExpiryAlerts ?? CookieExpiryAlertSettings.Default;
        var localAlertShown = false;

        if (alertSettings.Email.Enabled)
        {
            try
            {
                ValidateEmailSettings(alertSettings.Email);
                await emailAlertSender.SendAsync(
                    alertSettings.Email,
                    subject: emailSubject,
                    body: emailBody,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送{emailLabel}邮件失败：{ex.Message}");
            }
        }

        if (alertSettings.Local.ToastEnabled)
        {
            try
            {
                await toastNotificationService.ShowForcedAsync(
                    toastKind,
                    toastTitle,
                    toastMessage,
                    cancellationToken);
                localAlertShown = true;

                if (alertSettings.Local.SoundEnabled)
                {
                    await alertSoundService.PlayAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                activityLogService.Write(LogEntryKind.Warning, "Alert", $"展示{localLabel}屏幕提醒失败：{ex.Message}");
            }
        }

        if (enableInAppFallback && !localAlertShown)
        {
            await ShowInAppFallbackAsync(toastKind, toastTitle, toastMessage, cancellationToken);
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
            ToastVisualKind.Info,
            "任务提醒测试通知",
            "这是一条测试通知，用于确认抢座成功、任务失败和 Cookie 失效提醒的弹窗与提示音效果。",
            cancellationToken);

        if (settings.SoundEnabled)
        {
            await alertSoundService.PlayAsync(cancellationToken);
        }
    }

    private bool ShouldSuppress(string key)
    {
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

    private static string BuildCookieExpiredEmailBody(string source, string reason)
    {
        var builder = new StringBuilder();
        builder.AppendLine("IGoLibrary-Ex 检测到 Cookie 已失效");
        builder.AppendLine();
        builder.AppendLine($"触发模块：{source}");
        builder.AppendLine($"触发时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.AppendLine($"详细信息：{reason}");
        }

        builder.AppendLine();
        builder.AppendLine("请尽快重新授权，以恢复抢座/占座轮询");
        return builder.ToString();
    }

    private static string BuildGrabSucceededEmailBody(string libraryName, string seatName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("IGoLibrary-Ex 已成功预约到目标座位");
        builder.AppendLine();
        builder.AppendLine($"目标场馆：{libraryName}");
        builder.AppendLine($"目标座位：{seatName}");
        builder.AppendLine($"完成时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine();
        builder.AppendLine("你可以返回应用查看最新预约状态");
        return builder.ToString();
    }

    private static string BuildTaskFailedEmailBody(string taskName, string reason)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"IGoLibrary-Ex 检测到 {taskName}任务执行失败");
        builder.AppendLine();
        builder.AppendLine($"任务模块：{taskName}");
        builder.AppendLine($"触发时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.AppendLine($"详细信息：{reason}");
        }

        builder.AppendLine();
        builder.AppendLine("请返回应用检查任务状态、授权信息与场馆配置");
        return builder.ToString();
    }

    private static string BuildTestEmailBody()
    {
        return
            """
            这是一封来自 IGoLibrary-Ex 的测试邮件。

            如果你收到了这封邮件，说明当前 SMTP 参数已经可以正常工作，
            可用于 Cookie 失效、抢座成功和任务失败提醒。
            """;
    }

    private static string AppendDetail(string message, string detail)
        => string.IsNullOrWhiteSpace(detail)
            ? message
            : $"{message} 详细信息：{detail}";

    private static string NormalizeSeatName(string seatName)
        => string.IsNullOrWhiteSpace(seatName) ? "目标座位" : seatName.Trim();

    private static string NormalizeLibraryName(string libraryName)
        => string.IsNullOrWhiteSpace(libraryName) ? "目标场馆" : libraryName.Trim();

    private static string NormalizeTaskName(string taskName)
        => string.IsNullOrWhiteSpace(taskName) ? "任务" : taskName.Trim();

    private async Task ShowInAppFallbackAsync(
        ToastVisualKind kind,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (kind)
            {
                case ToastVisualKind.Success:
                    await notificationService.ShowSuccessAsync(title, message, cancellationToken);
                    break;
                case ToastVisualKind.Warning:
                    await notificationService.ShowWarningAsync(title, message, cancellationToken);
                    break;
                default:
                    await notificationService.ShowInfoAsync(title, message, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"展示应用内提醒失败：{ex.Message}");
        }
    }
}

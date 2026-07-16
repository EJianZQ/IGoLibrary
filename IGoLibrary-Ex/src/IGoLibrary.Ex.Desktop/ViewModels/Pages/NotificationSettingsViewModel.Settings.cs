using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class NotificationSettingsViewModel
{
    private DeferredAutoSaveController? _autoSave;
    private Func<bool> _canAutoSave = static () => false;
    private bool _settingsLoaded;

    private DeferredAutoSaveController AutoSave => _autoSave ??= new DeferredAutoSaveController(
        TimeSpan.FromMilliseconds(450),
        cancellationToken => SaveNotificationSettingsAsync(BuildTaskEventAlertSettingsSnapshot(), cancellationToken),
        _timeProvider);

    private static readonly string[] BarkAlertLevelValues = ["", "active", "timeSensitive", "passive", "critical"];

    public string[] EmailSecurityModes { get; } = ["无", "TLS"];

    public string[] BarkAlertLevels { get; } = ["默认", "主动 active", "时效 timeSensitive", "静默 passive", "重要 critical"];

    public string[] NotificationSettingsCategories { get; } = ["通知事件开关", "邮件提醒配置", "Telegram Bot 配置", "Server酱配置", "WxPusher 推送配置", "Bark 推送配置", "弹窗提醒配置"];

    [ObservableProperty]
    private bool emailAlertsEnabled;

    [ObservableProperty]
    private string emailAlertSmtpHost = string.Empty;

    [ObservableProperty]
    private int emailAlertSmtpPort = 587;

    [ObservableProperty]
    private int selectedEmailAlertSecurityModeIndex = 1;

    [ObservableProperty]
    private string emailAlertUsername = string.Empty;

    [ObservableProperty]
    private string emailAlertPassword = string.Empty;

    [ObservableProperty]
    private string emailAlertFromAddress = string.Empty;

    [ObservableProperty]
    private string emailAlertToAddress = string.Empty;

    [ObservableProperty]
    private bool telegramAlertsEnabled;

    [ObservableProperty]
    private string telegramAlertApiBaseUrl = TelegramAlertChannelSettings.DefaultApiBaseUrl;

    [ObservableProperty]
    private string telegramAlertBotToken = string.Empty;

    [ObservableProperty]
    private string telegramAlertChatId = string.Empty;

    [ObservableProperty]
    private bool serverChanAlertsEnabled;

    [ObservableProperty]
    private string serverChanAlertSendKey = string.Empty;

    [ObservableProperty]
    private bool serverChanAlertNoIp;

    [ObservableProperty]
    private string serverChanAlertChannel = string.Empty;

    [ObservableProperty]
    private string serverChanAlertOpenId = string.Empty;

    [ObservableProperty]
    private bool barkAlertsEnabled;

    [ObservableProperty]
    private string barkAlertApiBaseUrl = BarkAlertChannelSettings.DefaultApiBaseUrl;

    [ObservableProperty]
    private string barkAlertDeviceKey = string.Empty;

    [ObservableProperty]
    private string barkAlertGroup = string.Empty;

    [ObservableProperty]
    private string barkAlertSound = string.Empty;

    [ObservableProperty]
    private int selectedBarkAlertLevelIndex;

    [ObservableProperty]
    private bool wxPusherAlertsEnabled;

    [ObservableProperty]
    private string wxPusherAlertApiBaseUrl = WxPusherAlertChannelSettings.DefaultApiBaseUrl;

    [ObservableProperty]
    private string wxPusherAlertAppToken = string.Empty;

    [ObservableProperty]
    private string wxPusherAlertUids = string.Empty;

    [ObservableProperty]
    private string wxPusherAlertTopicIds = string.Empty;

    [ObservableProperty]
    private bool localToastAlertsEnabled = true;

    [ObservableProperty]
    private bool localSoundAlertsEnabled;

    [ObservableProperty]
    private bool cookieExpiringAlertsEnabled = true;

    [ObservableProperty]
    private bool grabSucceededAlertsEnabled = true;

    [ObservableProperty]
    private bool occupyReReserveSucceededAlertsEnabled = true;

    [ObservableProperty]
    private bool tomorrowReservationSucceededAlertsEnabled = true;

    [ObservableProperty]
    private bool globalLeakSucceededAlertsEnabled = true;

    [ObservableProperty]
    private bool sessionInvalidAlertsEnabled = true;

    [ObservableProperty]
    private bool taskFailedAlertsEnabled = true;

    [ObservableProperty]
    private bool cloudflareTunnelInterruptedAlertsEnabled = true;

    partial void OnEmailAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnEmailAlertSmtpHostChanged(string value) => ScheduleAutoSave();

    partial void OnEmailAlertSmtpPortChanged(int value) => ScheduleAutoSave();

    partial void OnSelectedEmailAlertSecurityModeIndexChanged(int value) => ScheduleAutoSave();

    partial void OnEmailAlertUsernameChanged(string value) => ScheduleAutoSave();

    partial void OnEmailAlertPasswordChanged(string value) => ScheduleAutoSave();

    partial void OnEmailAlertFromAddressChanged(string value) => ScheduleAutoSave();

    partial void OnEmailAlertToAddressChanged(string value) => ScheduleAutoSave();

    partial void OnTelegramAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnTelegramAlertApiBaseUrlChanged(string value) => ScheduleAutoSave();

    partial void OnTelegramAlertBotTokenChanged(string value) => ScheduleAutoSave();

    partial void OnTelegramAlertChatIdChanged(string value) => ScheduleAutoSave();

    partial void OnServerChanAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnServerChanAlertSendKeyChanged(string value) => ScheduleAutoSave();

    partial void OnServerChanAlertNoIpChanged(bool value) => ScheduleAutoSave();

    partial void OnServerChanAlertChannelChanged(string value) => ScheduleAutoSave();

    partial void OnServerChanAlertOpenIdChanged(string value) => ScheduleAutoSave();

    partial void OnBarkAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnBarkAlertApiBaseUrlChanged(string value) => ScheduleAutoSave();

    partial void OnBarkAlertDeviceKeyChanged(string value) => ScheduleAutoSave();

    partial void OnBarkAlertGroupChanged(string value) => ScheduleAutoSave();

    partial void OnBarkAlertSoundChanged(string value) => ScheduleAutoSave();

    partial void OnSelectedBarkAlertLevelIndexChanged(int value) => ScheduleAutoSave();

    partial void OnWxPusherAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnWxPusherAlertApiBaseUrlChanged(string value) => ScheduleAutoSave();

    partial void OnWxPusherAlertAppTokenChanged(string value) => ScheduleAutoSave();

    partial void OnWxPusherAlertUidsChanged(string value) => ScheduleAutoSave();

    partial void OnWxPusherAlertTopicIdsChanged(string value) => ScheduleAutoSave();

    partial void OnLocalToastAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnLocalSoundAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnCookieExpiringAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnGrabSucceededAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnOccupyReReserveSucceededAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnTomorrowReservationSucceededAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnGlobalLeakSucceededAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnSessionInvalidAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnTaskFailedAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    partial void OnCloudflareTunnelInterruptedAlertsEnabledChanged(bool value) => ScheduleAutoSave();

    public void ConfigureAutoSave(Func<bool> canAutoSave)
    {
        _canAutoSave = canAutoSave;
    }

    public void MarkSettingsLoaded()
    {
        _settingsLoaded = true;
    }

    public void ApplySettings(AppSettings settings)
    {
        var alertSettings = settings.Notifications.TaskEventAlerts ?? TaskEventAlertSettings.Default;
        var eventSettings = alertSettings.Events ?? TaskEventAlertEventSettings.Default;
        var barkSettings = alertSettings.Bark ?? BarkAlertChannelSettings.Default;
        var wxPusherSettings = alertSettings.WxPusher ?? WxPusherAlertChannelSettings.Default;
        var serverChanSettings = alertSettings.ServerChan ?? ServerChanAlertChannelSettings.Default;

        EmailAlertsEnabled = alertSettings.Email.Enabled;
        EmailAlertSmtpHost = alertSettings.Email.SmtpHost;
        EmailAlertSmtpPort = alertSettings.Email.Port;
        SelectedEmailAlertSecurityModeIndex = alertSettings.Email.SecurityMode == EmailSecurityMode.Tls ? 1 : 0;
        EmailAlertUsername = alertSettings.Email.Username;
        EmailAlertPassword = alertSettings.Email.Password;
        EmailAlertFromAddress = alertSettings.Email.FromAddress;
        EmailAlertToAddress = alertSettings.Email.ToAddress;
        TelegramAlertsEnabled = alertSettings.Telegram.Enabled;
        TelegramAlertApiBaseUrl = string.IsNullOrWhiteSpace(alertSettings.Telegram.ApiBaseUrl)
            ? TelegramAlertChannelSettings.DefaultApiBaseUrl
            : alertSettings.Telegram.ApiBaseUrl;
        TelegramAlertBotToken = alertSettings.Telegram.BotToken ?? string.Empty;
        TelegramAlertChatId = alertSettings.Telegram.ChatId ?? string.Empty;
        ServerChanAlertsEnabled = serverChanSettings.Enabled;
        ServerChanAlertSendKey = serverChanSettings.SendKey ?? string.Empty;
        ServerChanAlertNoIp = serverChanSettings.NoIp;
        ServerChanAlertChannel = serverChanSettings.Channel ?? string.Empty;
        ServerChanAlertOpenId = serverChanSettings.OpenId ?? string.Empty;
        BarkAlertsEnabled = barkSettings.Enabled;
        BarkAlertApiBaseUrl = string.IsNullOrWhiteSpace(barkSettings.ApiBaseUrl)
            ? BarkAlertChannelSettings.DefaultApiBaseUrl
            : barkSettings.ApiBaseUrl;
        BarkAlertDeviceKey = barkSettings.DeviceKey ?? string.Empty;
        BarkAlertGroup = barkSettings.Group ?? string.Empty;
        BarkAlertSound = barkSettings.Sound ?? string.Empty;
        SelectedBarkAlertLevelIndex = FindBarkAlertLevelIndex(barkSettings.Level);
        WxPusherAlertsEnabled = wxPusherSettings.Enabled;
        WxPusherAlertApiBaseUrl = string.IsNullOrWhiteSpace(wxPusherSettings.ApiBaseUrl)
            ? WxPusherAlertChannelSettings.DefaultApiBaseUrl
            : wxPusherSettings.ApiBaseUrl;
        WxPusherAlertAppToken = wxPusherSettings.AppToken ?? string.Empty;
        WxPusherAlertUids = wxPusherSettings.Uids ?? string.Empty;
        WxPusherAlertTopicIds = wxPusherSettings.TopicIds ?? string.Empty;
        LocalToastAlertsEnabled = alertSettings.Local.PopupEnabled;
        LocalSoundAlertsEnabled = alertSettings.Local.SoundEnabled;
        CookieExpiringAlertsEnabled = eventSettings.CookieExpiring;
        GrabSucceededAlertsEnabled = eventSettings.GrabSucceeded;
        OccupyReReserveSucceededAlertsEnabled = eventSettings.OccupyReReserveSucceeded;
        TomorrowReservationSucceededAlertsEnabled = eventSettings.TomorrowReservationSucceeded;
        GlobalLeakSucceededAlertsEnabled = eventSettings.GlobalLeakSucceeded;
        SessionInvalidAlertsEnabled = eventSettings.SessionInvalid;
        TaskFailedAlertsEnabled = eventSettings.TaskFailed;
        CloudflareTunnelInterruptedAlertsEnabled = eventSettings.CloudflareTunnelInterrupted;
    }

    public void CancelPendingAutoSave()
    {
        AutoSave.Cancel();
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return AutoSave.FlushAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task TestToastAsync()
    {
        if (notificationService is ToastNotificationService toastNotificationService)
        {
            await toastNotificationService.ShowPreviewAsync("测试通知", "这是一条用于测试界面动效与停留时间的 Toast 通知");
            return;
        }

        await notificationService.ShowInfoAsync("测试通知", "这是一条用于测试界面动效与停留时间的 Toast 通知");
    }

    [RelayCommand]
    private async Task SendTestEmailAlertAsync()
    {
        try
        {
            AutoSave.Cancel();
            await PersistSnapshotAsync();
            await SendTestEmailAsync(BuildTaskEventAlertSettingsSnapshot().Email);
            await notificationService.ShowSuccessAsync("测试邮件已发送", "请检查收件箱，确认当前 SMTP 配置可用");
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送测试邮件失败：{ex.Message}");
            await errorDialogService.ShowErrorAsync("测试邮件发送失败", ex.GetType().Name, BuildExceptionDetails(ex));
        }
    }

    [RelayCommand]
    private async Task SendTestTelegramAlertAsync()
    {
        try
        {
            AutoSave.Cancel();
            await PersistSnapshotAsync();
            await SendTestTelegramAsync(BuildTaskEventAlertSettingsSnapshot().Telegram);
            await notificationService.ShowSuccessAsync("测试 Telegram 已发送", "请检查 Telegram，确认当前 Bot 配置可用");
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送测试 Telegram 失败：{ex.Message}");
            await errorDialogService.ShowErrorAsync("测试 Telegram 发送失败", ex.GetType().Name, BuildExceptionDetails(ex));
        }
    }

    [RelayCommand]
    private async Task SendTestBarkAlertAsync()
    {
        try
        {
            AutoSave.Cancel();
            await PersistSnapshotAsync();
            await SendTestBarkAsync(BuildTaskEventAlertSettingsSnapshot().Bark);
            await notificationService.ShowSuccessAsync("测试 Bark 已发送", "请检查 Bark App，确认当前推送配置可用");
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送测试 Bark 失败：{ex.Message}");
            await errorDialogService.ShowErrorAsync("测试 Bark 发送失败", ex.GetType().Name, BuildExceptionDetails(ex));
        }
    }

    [RelayCommand]
    private async Task SendTestWxPusherAlertAsync()
    {
        try
        {
            AutoSave.Cancel();
            await PersistSnapshotAsync();
            await SendTestWxPusherAsync(BuildTaskEventAlertSettingsSnapshot().WxPusher);
            await notificationService.ShowSuccessAsync("测试 WxPusher 已发送", "请检查 WxPusher 客户端，确认当前推送配置可用");
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送测试 WxPusher 失败：{ex.Message}");
            await errorDialogService.ShowErrorAsync("测试 WxPusher 发送失败", ex.GetType().Name, BuildExceptionDetails(ex));
        }
    }

    [RelayCommand]
    private async Task SendTestServerChanAlertAsync()
    {
        try
        {
            AutoSave.Cancel();
            await PersistSnapshotAsync();
            await SendTestServerChanAsync(BuildTaskEventAlertSettingsSnapshot().ServerChan);
            await notificationService.ShowSuccessAsync("测试 Server酱 已发送", "请检查 Server酱 配置的通知通道，确认当前推送配置可用");
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送测试 Server酱 失败：{ex.Message}");
            await errorDialogService.ShowErrorAsync("测试 Server酱 发送失败", ex.GetType().Name, BuildExceptionDetails(ex));
        }
    }

    [RelayCommand]
    private async Task SendTestLocalAlertAsync()
    {
        try
        {
            AutoSave.Cancel();
            await PersistSnapshotAsync();
            await SendTestLocalAlertAsync(BuildTaskEventAlertSettingsSnapshot().Local);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"发送测试通知失败：{ex.Message}");
            await notificationService.ShowWarningAsync("测试通知发送失败", ex.Message);
        }
    }

    public TaskEventAlertSettings BuildTaskEventAlertSettingsSnapshot()
    {
        return new TaskEventAlertSettings(
            new EmailAlertChannelSettings(
                EmailAlertsEnabled,
                EmailAlertSmtpHost.Trim(),
                Math.Clamp(EmailAlertSmtpPort, 1, 65535),
                SelectedEmailAlertSecurityModeIndex == 1 ? EmailSecurityMode.Tls : EmailSecurityMode.None,
                EmailAlertUsername.Trim(),
                EmailAlertPassword,
                EmailAlertFromAddress.Trim(),
                EmailAlertToAddress.Trim()),
            new LocalDesktopAlertSettings(
                LocalToastAlertsEnabled,
                LocalSoundAlertsEnabled),
            new TelegramAlertChannelSettings(
                TelegramAlertsEnabled,
                NormalizeTelegramApiBaseUrlForSnapshot(TelegramAlertApiBaseUrl),
                (TelegramAlertBotToken ?? string.Empty).Trim(),
                (TelegramAlertChatId ?? string.Empty).Trim()),
            new TaskEventAlertEventSettings
            {
                CookieExpiring = CookieExpiringAlertsEnabled,
                GrabSucceeded = GrabSucceededAlertsEnabled,
                OccupyReReserveSucceeded = OccupyReReserveSucceededAlertsEnabled,
                TomorrowReservationSucceeded = TomorrowReservationSucceededAlertsEnabled,
                GlobalLeakSucceeded = GlobalLeakSucceededAlertsEnabled,
                SessionInvalid = SessionInvalidAlertsEnabled,
                TaskFailed = TaskFailedAlertsEnabled,
                CloudflareTunnelInterrupted = CloudflareTunnelInterruptedAlertsEnabled
            },
            new BarkAlertChannelSettings(
                BarkAlertsEnabled,
                NormalizeBarkApiBaseUrlForSnapshot(BarkAlertApiBaseUrl),
                (BarkAlertDeviceKey ?? string.Empty).Trim(),
                (BarkAlertGroup ?? string.Empty).Trim(),
                (BarkAlertSound ?? string.Empty).Trim(),
                GetSelectedBarkAlertLevel()),
            new WxPusherAlertChannelSettings(
                WxPusherAlertsEnabled,
                NormalizeWxPusherApiBaseUrlForSnapshot(WxPusherAlertApiBaseUrl),
                (WxPusherAlertAppToken ?? string.Empty).Trim(),
                (WxPusherAlertUids ?? string.Empty).Trim(),
                (WxPusherAlertTopicIds ?? string.Empty).Trim()),
            new ServerChanAlertChannelSettings(
                ServerChanAlertsEnabled,
                (ServerChanAlertSendKey ?? string.Empty).Trim(),
                ServerChanAlertNoIp,
                (ServerChanAlertChannel ?? string.Empty).Trim(),
                (ServerChanAlertOpenId ?? string.Empty).Trim()));
    }

    public Task PersistSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return SaveNotificationSettingsAsync(BuildTaskEventAlertSettingsSnapshot(), cancellationToken);
    }

    private void ScheduleAutoSave()
    {
        if (!_settingsLoaded || !_canAutoSave())
        {
            return;
        }

        AutoSave.Schedule(ex =>
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"自动保存通知设置失败：{ex.Message}"));
    }

    private static string NormalizeTelegramApiBaseUrlForSnapshot(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(trimmed)
            ? TelegramAlertChannelSettings.DefaultApiBaseUrl
            : trimmed;
    }

    private static string NormalizeBarkApiBaseUrlForSnapshot(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(trimmed)
            ? BarkAlertChannelSettings.DefaultApiBaseUrl
            : trimmed;
    }

    private static string NormalizeWxPusherApiBaseUrlForSnapshot(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(trimmed)
            ? WxPusherAlertChannelSettings.DefaultApiBaseUrl
            : trimmed;
    }

    private string GetSelectedBarkAlertLevel()
    {
        return SelectedBarkAlertLevelIndex >= 0 && SelectedBarkAlertLevelIndex < BarkAlertLevelValues.Length
            ? BarkAlertLevelValues[SelectedBarkAlertLevelIndex]
            : string.Empty;
    }

    private static int FindBarkAlertLevelIndex(string? level)
    {
        var normalized = (level ?? string.Empty).Trim();
        for (var index = 0; index < BarkAlertLevelValues.Length; index++)
        {
            if (string.Equals(BarkAlertLevelValues[index], normalized, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    private static string BuildExceptionDetails(Exception exception)
    {
        var builder = new System.Text.StringBuilder();
        var current = exception;
        var depth = 0;

        while (current is not null)
        {
            if (depth == 0)
            {
                builder.Append(current.Message);
            }
            else
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.Append($"内部异常 {depth}：{current.GetType().Name}: {current.Message}");
            }

            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }
}

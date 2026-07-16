using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _notificationSettingsConfigured;

    public string[] EmailSecurityModes => NotificationSettings.EmailSecurityModes;

    public string[] BarkAlertLevels => NotificationSettings.BarkAlertLevels;

    public string[] NotificationSettingsCategories => NotificationSettings.NotificationSettingsCategories;

    public bool EmailAlertsEnabled
    {
        get => NotificationSettings.EmailAlertsEnabled;
        set => NotificationSettings.EmailAlertsEnabled = value;
    }

    public string EmailAlertSmtpHost
    {
        get => NotificationSettings.EmailAlertSmtpHost;
        set => NotificationSettings.EmailAlertSmtpHost = value;
    }

    public int EmailAlertSmtpPort
    {
        get => NotificationSettings.EmailAlertSmtpPort;
        set => NotificationSettings.EmailAlertSmtpPort = value;
    }

    public int SelectedEmailAlertSecurityModeIndex
    {
        get => NotificationSettings.SelectedEmailAlertSecurityModeIndex;
        set => NotificationSettings.SelectedEmailAlertSecurityModeIndex = value;
    }

    public string EmailAlertUsername
    {
        get => NotificationSettings.EmailAlertUsername;
        set => NotificationSettings.EmailAlertUsername = value;
    }

    public string EmailAlertPassword
    {
        get => NotificationSettings.EmailAlertPassword;
        set => NotificationSettings.EmailAlertPassword = value;
    }

    public string EmailAlertFromAddress
    {
        get => NotificationSettings.EmailAlertFromAddress;
        set => NotificationSettings.EmailAlertFromAddress = value;
    }

    public string EmailAlertToAddress
    {
        get => NotificationSettings.EmailAlertToAddress;
        set => NotificationSettings.EmailAlertToAddress = value;
    }

    public bool TelegramAlertsEnabled
    {
        get => NotificationSettings.TelegramAlertsEnabled;
        set => NotificationSettings.TelegramAlertsEnabled = value;
    }

    public string TelegramAlertApiBaseUrl
    {
        get => NotificationSettings.TelegramAlertApiBaseUrl;
        set => NotificationSettings.TelegramAlertApiBaseUrl = value;
    }

    public string TelegramAlertBotToken
    {
        get => NotificationSettings.TelegramAlertBotToken;
        set => NotificationSettings.TelegramAlertBotToken = value;
    }

    public string TelegramAlertChatId
    {
        get => NotificationSettings.TelegramAlertChatId;
        set => NotificationSettings.TelegramAlertChatId = value;
    }

    public bool ServerChanAlertsEnabled
    {
        get => NotificationSettings.ServerChanAlertsEnabled;
        set => NotificationSettings.ServerChanAlertsEnabled = value;
    }

    public string ServerChanAlertSendKey
    {
        get => NotificationSettings.ServerChanAlertSendKey;
        set => NotificationSettings.ServerChanAlertSendKey = value;
    }

    public bool ServerChanAlertNoIp
    {
        get => NotificationSettings.ServerChanAlertNoIp;
        set => NotificationSettings.ServerChanAlertNoIp = value;
    }

    public string ServerChanAlertChannel
    {
        get => NotificationSettings.ServerChanAlertChannel;
        set => NotificationSettings.ServerChanAlertChannel = value;
    }

    public string ServerChanAlertOpenId
    {
        get => NotificationSettings.ServerChanAlertOpenId;
        set => NotificationSettings.ServerChanAlertOpenId = value;
    }

    public bool BarkAlertsEnabled
    {
        get => NotificationSettings.BarkAlertsEnabled;
        set => NotificationSettings.BarkAlertsEnabled = value;
    }

    public string BarkAlertApiBaseUrl
    {
        get => NotificationSettings.BarkAlertApiBaseUrl;
        set => NotificationSettings.BarkAlertApiBaseUrl = value;
    }

    public string BarkAlertDeviceKey
    {
        get => NotificationSettings.BarkAlertDeviceKey;
        set => NotificationSettings.BarkAlertDeviceKey = value;
    }

    public string BarkAlertGroup
    {
        get => NotificationSettings.BarkAlertGroup;
        set => NotificationSettings.BarkAlertGroup = value;
    }

    public string BarkAlertSound
    {
        get => NotificationSettings.BarkAlertSound;
        set => NotificationSettings.BarkAlertSound = value;
    }

    public int SelectedBarkAlertLevelIndex
    {
        get => NotificationSettings.SelectedBarkAlertLevelIndex;
        set => NotificationSettings.SelectedBarkAlertLevelIndex = value;
    }

    public bool WxPusherAlertsEnabled
    {
        get => NotificationSettings.WxPusherAlertsEnabled;
        set => NotificationSettings.WxPusherAlertsEnabled = value;
    }

    public string WxPusherAlertApiBaseUrl
    {
        get => NotificationSettings.WxPusherAlertApiBaseUrl;
        set => NotificationSettings.WxPusherAlertApiBaseUrl = value;
    }

    public string WxPusherAlertAppToken
    {
        get => NotificationSettings.WxPusherAlertAppToken;
        set => NotificationSettings.WxPusherAlertAppToken = value;
    }

    public string WxPusherAlertUids
    {
        get => NotificationSettings.WxPusherAlertUids;
        set => NotificationSettings.WxPusherAlertUids = value;
    }

    public string WxPusherAlertTopicIds
    {
        get => NotificationSettings.WxPusherAlertTopicIds;
        set => NotificationSettings.WxPusherAlertTopicIds = value;
    }

    public bool LocalToastAlertsEnabled
    {
        get => NotificationSettings.LocalToastAlertsEnabled;
        set => NotificationSettings.LocalToastAlertsEnabled = value;
    }

    public bool LocalSoundAlertsEnabled
    {
        get => NotificationSettings.LocalSoundAlertsEnabled;
        set => NotificationSettings.LocalSoundAlertsEnabled = value;
    }

    public bool CookieExpiringAlertsEnabled
    {
        get => NotificationSettings.CookieExpiringAlertsEnabled;
        set => NotificationSettings.CookieExpiringAlertsEnabled = value;
    }

    public bool GrabSucceededAlertsEnabled
    {
        get => NotificationSettings.GrabSucceededAlertsEnabled;
        set => NotificationSettings.GrabSucceededAlertsEnabled = value;
    }

    public bool OccupyReReserveSucceededAlertsEnabled
    {
        get => NotificationSettings.OccupyReReserveSucceededAlertsEnabled;
        set => NotificationSettings.OccupyReReserveSucceededAlertsEnabled = value;
    }

    public bool TomorrowReservationSucceededAlertsEnabled
    {
        get => NotificationSettings.TomorrowReservationSucceededAlertsEnabled;
        set => NotificationSettings.TomorrowReservationSucceededAlertsEnabled = value;
    }

    public bool GlobalLeakSucceededAlertsEnabled
    {
        get => NotificationSettings.GlobalLeakSucceededAlertsEnabled;
        set => NotificationSettings.GlobalLeakSucceededAlertsEnabled = value;
    }

    public bool SessionInvalidAlertsEnabled
    {
        get => NotificationSettings.SessionInvalidAlertsEnabled;
        set => NotificationSettings.SessionInvalidAlertsEnabled = value;
    }

    public bool TaskFailedAlertsEnabled
    {
        get => NotificationSettings.TaskFailedAlertsEnabled;
        set => NotificationSettings.TaskFailedAlertsEnabled = value;
    }

    public IAsyncRelayCommand TestToastCommand => NotificationSettings.TestToastCommand;

    public IAsyncRelayCommand SendTestEmailAlertCommand => NotificationSettings.SendTestEmailAlertCommand;

    public IAsyncRelayCommand SendTestTelegramAlertCommand => NotificationSettings.SendTestTelegramAlertCommand;

    public IAsyncRelayCommand SendTestBarkAlertCommand => NotificationSettings.SendTestBarkAlertCommand;

    public IAsyncRelayCommand SendTestWxPusherAlertCommand => NotificationSettings.SendTestWxPusherAlertCommand;

    public IAsyncRelayCommand SendTestServerChanAlertCommand => NotificationSettings.SendTestServerChanAlertCommand;

    public IAsyncRelayCommand SendTestLocalAlertCommand => NotificationSettings.SendTestLocalAlertCommand;

    private void ConfigureNotificationSettingsPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            NotificationSettings,
            nameof(EmailAlertsEnabled),
            nameof(EmailAlertSmtpHost),
            nameof(EmailAlertSmtpPort),
            nameof(SelectedEmailAlertSecurityModeIndex),
            nameof(EmailAlertUsername),
            nameof(EmailAlertPassword),
            nameof(EmailAlertFromAddress),
            nameof(EmailAlertToAddress),
            nameof(TelegramAlertsEnabled),
            nameof(TelegramAlertApiBaseUrl),
            nameof(TelegramAlertBotToken),
            nameof(TelegramAlertChatId),
            nameof(ServerChanAlertsEnabled),
            nameof(ServerChanAlertSendKey),
            nameof(ServerChanAlertNoIp),
            nameof(ServerChanAlertChannel),
            nameof(ServerChanAlertOpenId),
            nameof(BarkAlertsEnabled),
            nameof(BarkAlertApiBaseUrl),
            nameof(BarkAlertDeviceKey),
            nameof(BarkAlertGroup),
            nameof(BarkAlertSound),
            nameof(SelectedBarkAlertLevelIndex),
            nameof(WxPusherAlertsEnabled),
            nameof(WxPusherAlertApiBaseUrl),
            nameof(WxPusherAlertAppToken),
            nameof(WxPusherAlertUids),
            nameof(WxPusherAlertTopicIds),
            nameof(LocalToastAlertsEnabled),
            nameof(LocalSoundAlertsEnabled),
            nameof(CookieExpiringAlertsEnabled),
            nameof(GrabSucceededAlertsEnabled),
            nameof(OccupyReReserveSucceededAlertsEnabled),
            nameof(TomorrowReservationSucceededAlertsEnabled),
            nameof(GlobalLeakSucceededAlertsEnabled),
            nameof(SessionInvalidAlertsEnabled),
            nameof(TaskFailedAlertsEnabled));
    }

    private void EnsureNotificationSettingsConfigured()
    {
        if (_notificationSettingsConfigured)
        {
            return;
        }

        NotificationSettings.ConfigureAutoSave(() => !IsLoadingSettings && IsInitializationComplete);
        _notificationSettingsConfigured = true;
    }

    private TaskEventAlertSettings BuildTaskEventAlertSettingsSnapshot()
    {
        EnsureNotificationSettingsConfigured();
        return NotificationSettings.BuildTaskEventAlertSettingsSnapshot();
    }

    private void ScheduleNotificationSettingsAutoSave()
    {
        EnsureNotificationSettingsConfigured();
    }

    private async Task PersistNotificationSettingsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotificationSettingsConfigured();
        await NotificationSettings.PersistSnapshotAsync(cancellationToken);
    }

    private void CancelPendingNotificationSettingsAutoSave()
    {
        NotificationSettings.CancelPendingAutoSave();
    }
}

namespace IGoLibrary.Ex.Application.Configuration;

public sealed record TaskEventAlertSettings
{
    public TaskEventAlertSettings(
        EmailAlertChannelSettings email,
        LocalDesktopAlertSettings local,
        TelegramAlertChannelSettings? telegram = null,
        TaskEventAlertEventSettings? events = null,
        BarkAlertChannelSettings? bark = null,
        WxPusherAlertChannelSettings? wxPusher = null,
        ServerChanAlertChannelSettings? serverChan = null)
    {
        Email = email;
        Local = local;
        Telegram = telegram ?? TelegramAlertChannelSettings.Default;
        Bark = bark ?? BarkAlertChannelSettings.Default;
        WxPusher = wxPusher ?? WxPusherAlertChannelSettings.Default;
        ServerChan = serverChan ?? ServerChanAlertChannelSettings.Default;
        Events = events ?? TaskEventAlertEventSettings.Default;
    }

    public EmailAlertChannelSettings Email { get; init; }

    public LocalDesktopAlertSettings Local { get; init; }

    public TelegramAlertChannelSettings Telegram { get; init; }

    public BarkAlertChannelSettings Bark { get; init; }

    public WxPusherAlertChannelSettings WxPusher { get; init; }

    public ServerChanAlertChannelSettings ServerChan { get; init; }

    public TaskEventAlertEventSettings Events { get; init; }

    public static TaskEventAlertSettings Default { get; } = new(
        EmailAlertChannelSettings.Default,
        LocalDesktopAlertSettings.Default,
        TelegramAlertChannelSettings.Default,
        TaskEventAlertEventSettings.Default,
        BarkAlertChannelSettings.Default,
        WxPusherAlertChannelSettings.Default,
        ServerChanAlertChannelSettings.Default);
}

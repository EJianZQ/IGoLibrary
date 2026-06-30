namespace IGoLibrary.Ex.Application.Configuration;

public sealed record TaskEventAlertSettings
{
    public TaskEventAlertSettings(
        EmailAlertChannelSettings email,
        LocalDesktopAlertSettings local,
        TelegramAlertChannelSettings? telegram = null,
        BarkAlertChannelSettings? bark = null)
    {
        Email = email;
        Local = local;
        Telegram = telegram ?? TelegramAlertChannelSettings.Default;
        Bark = bark ?? BarkAlertChannelSettings.Default;
    }

    public EmailAlertChannelSettings Email { get; init; }

    public LocalDesktopAlertSettings Local { get; init; }

    public TelegramAlertChannelSettings Telegram { get; init; }

    public BarkAlertChannelSettings Bark { get; init; }

    public static TaskEventAlertSettings Default { get; } = new(
        EmailAlertChannelSettings.Default,
        LocalDesktopAlertSettings.Default,
        TelegramAlertChannelSettings.Default,
        BarkAlertChannelSettings.Default);
}

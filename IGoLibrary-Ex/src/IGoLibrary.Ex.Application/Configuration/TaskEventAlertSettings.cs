namespace IGoLibrary.Ex.Application.Configuration;

public sealed record TaskEventAlertSettings
{
    public TaskEventAlertSettings(
        EmailAlertChannelSettings email,
        LocalDesktopAlertSettings local,
        TelegramAlertChannelSettings? telegram = null)
    {
        Email = email;
        Local = local;
        Telegram = telegram ?? TelegramAlertChannelSettings.Default;
    }

    public EmailAlertChannelSettings Email { get; init; }

    public LocalDesktopAlertSettings Local { get; init; }

    public TelegramAlertChannelSettings Telegram { get; init; }

    public static TaskEventAlertSettings Default { get; } = new(
        EmailAlertChannelSettings.Default,
        LocalDesktopAlertSettings.Default,
        TelegramAlertChannelSettings.Default);
}

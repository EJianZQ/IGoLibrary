namespace IGoLibrary.Ex.Domain.Models;

public sealed record TaskEventAlertSettings
{
    public TaskEventAlertSettings(
        EmailAlertChannelSettings email,
        LocalAlertChannelSettings local,
        TelegramAlertChannelSettings? telegram = null)
    {
        Email = email;
        Local = local;
        Telegram = telegram ?? TelegramAlertChannelSettings.Default;
    }

    public EmailAlertChannelSettings Email { get; init; }

    public LocalAlertChannelSettings Local { get; init; }

    public TelegramAlertChannelSettings Telegram { get; init; }

    public static TaskEventAlertSettings Default { get; } = new(
        EmailAlertChannelSettings.Default,
        LocalAlertChannelSettings.Default,
        TelegramAlertChannelSettings.Default);
}

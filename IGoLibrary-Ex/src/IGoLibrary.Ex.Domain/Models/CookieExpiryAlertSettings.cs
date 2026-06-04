namespace IGoLibrary.Ex.Domain.Models;

public sealed record CookieExpiryAlertSettings
{
    public CookieExpiryAlertSettings(
        CookieExpiryEmailAlertSettings email,
        CookieExpiryLocalAlertSettings local,
        TelegramAlertSettings? telegram = null)
    {
        Email = email;
        Local = local;
        Telegram = telegram ?? TelegramAlertSettings.Default;
    }

    public CookieExpiryEmailAlertSettings Email { get; init; }

    public CookieExpiryLocalAlertSettings Local { get; init; }

    public TelegramAlertSettings Telegram { get; init; }

    public static CookieExpiryAlertSettings Default { get; } = new(
        CookieExpiryEmailAlertSettings.Default,
        CookieExpiryLocalAlertSettings.Default,
        TelegramAlertSettings.Default);
}

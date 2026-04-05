namespace IGoLibrary.Ex.Domain.Models;

public sealed record CookieExpiryAlertSettings(
    CookieExpiryEmailAlertSettings Email,
    CookieExpiryLocalAlertSettings Local)
{
    public static CookieExpiryAlertSettings Default { get; } = new(
        CookieExpiryEmailAlertSettings.Default,
        CookieExpiryLocalAlertSettings.Default);
}

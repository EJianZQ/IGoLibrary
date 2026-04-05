namespace IGoLibrary.Ex.Domain.Models;

public sealed record CookieExpiryLocalAlertSettings(
    bool ToastEnabled,
    bool SoundEnabled)
{
    public static CookieExpiryLocalAlertSettings Default { get; } = new(
        ToastEnabled: true,
        SoundEnabled: false);
}

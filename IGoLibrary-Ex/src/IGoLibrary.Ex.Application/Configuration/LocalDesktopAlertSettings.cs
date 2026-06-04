namespace IGoLibrary.Ex.Application.Configuration;

public sealed record LocalDesktopAlertSettings(
    bool PopupEnabled,
    bool SoundEnabled)
{
    public static LocalDesktopAlertSettings Default { get; } = new(
        PopupEnabled: true,
        SoundEnabled: false);
}

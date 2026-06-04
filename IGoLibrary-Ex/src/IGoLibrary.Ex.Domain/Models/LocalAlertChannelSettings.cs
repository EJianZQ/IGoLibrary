namespace IGoLibrary.Ex.Domain.Models;

public sealed record LocalAlertChannelSettings(
    bool ToastEnabled,
    bool SoundEnabled)
{
    public static LocalAlertChannelSettings Default { get; } = new(
        ToastEnabled: true,
        SoundEnabled: false);
}

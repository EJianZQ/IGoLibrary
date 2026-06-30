namespace IGoLibrary.Ex.Application.Configuration;

public sealed record BarkAlertChannelSettings(
    bool Enabled,
    string ServerUrl,
    string DeviceKey,
    string Sound,
    string Group)
{
    public const string DefaultServerUrl = "https://api.day.app";

    public static BarkAlertChannelSettings Default { get; } = new(
        Enabled: false,
        ServerUrl: DefaultServerUrl,
        DeviceKey: string.Empty,
        Sound: string.Empty,
        Group: "IGoLibrary-Ex");
}

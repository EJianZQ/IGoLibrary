namespace IGoLibrary.Ex.Application.Configuration;

public sealed record BarkAlertChannelSettings(
    bool Enabled,
    string ApiBaseUrl,
    string DeviceKey,
    string Group,
    string Sound,
    string Level)
{
    public const string DefaultApiBaseUrl = "https://api.day.app";

    public static BarkAlertChannelSettings Default { get; } = new(
        Enabled: false,
        ApiBaseUrl: DefaultApiBaseUrl,
        DeviceKey: string.Empty,
        Group: string.Empty,
        Sound: string.Empty,
        Level: string.Empty);
}

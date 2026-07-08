namespace IGoLibrary.Ex.Application.Configuration;

public sealed record ServerChanAlertChannelSettings(
    bool Enabled,
    string SendKey,
    bool NoIp,
    string Channel,
    string OpenId)
{
    public static ServerChanAlertChannelSettings Default { get; } = new(
        Enabled: false,
        SendKey: string.Empty,
        NoIp: false,
        Channel: string.Empty,
        OpenId: string.Empty);
}

namespace IGoLibrary.Ex.Application.Configuration;

public sealed record WxPusherAlertChannelSettings(
    bool Enabled,
    string ApiBaseUrl,
    string AppToken,
    string Uids,
    string TopicIds)
{
    public const string DefaultApiBaseUrl = "https://wxpusher.zjiecode.com";

    public static WxPusherAlertChannelSettings Default { get; } = new(
        Enabled: false,
        ApiBaseUrl: DefaultApiBaseUrl,
        AppToken: string.Empty,
        Uids: string.Empty,
        TopicIds: string.Empty);
}

namespace IGoLibrary.Ex.Application.Configuration;

public sealed record TelegramAlertChannelSettings(
    bool Enabled,
    string ApiBaseUrl,
    string BotToken,
    string ChatId)
{
    public const string DefaultApiBaseUrl = "https://api.telegram.org";

    public static TelegramAlertChannelSettings Default { get; } = new(
        Enabled: false,
        ApiBaseUrl: DefaultApiBaseUrl,
        BotToken: string.Empty,
        ChatId: string.Empty);
}

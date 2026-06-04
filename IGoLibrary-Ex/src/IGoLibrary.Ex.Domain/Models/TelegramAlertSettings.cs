namespace IGoLibrary.Ex.Domain.Models;

public sealed record TelegramAlertSettings(
    bool Enabled,
    string ApiBaseUrl,
    string BotToken,
    string ChatId)
{
    public const string DefaultApiBaseUrl = "https://api.telegram.org";

    public static TelegramAlertSettings Default { get; } = new(
        Enabled: false,
        ApiBaseUrl: DefaultApiBaseUrl,
        BotToken: string.Empty,
        ChatId: string.Empty);
}

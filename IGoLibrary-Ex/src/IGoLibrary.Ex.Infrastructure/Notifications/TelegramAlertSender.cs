using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal sealed class TelegramAlertSender(
    HttpClient httpClient,
    ISettingsService settingsService,
    TimeProvider? timeProvider = null,
    ILogger<TelegramAlertSender>? logger = null) : ITelegramAlertSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ILogger<TelegramAlertSender> _logger =
        logger ?? NullLogger<TelegramAlertSender>.Instance;

    public async Task SendAsync(
        TelegramAlertChannelSettings settings,
        string message,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("Telegram 消息内容不能为空");
        }

        using var response = await HttpNotificationRequestPolicy.ExecuteAsync(
            settingsService,
            "Telegram",
            token => SendOnceAsync(normalized, message, token),
            _timeProvider,
            cancellationToken,
            _logger);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfTelegramResponseFailed(response, raw);
    }

    internal static TelegramAlertChannelSettings Normalize(TelegramAlertChannelSettings settings)
    {
        var apiBaseUrl = (settings.ApiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            throw new InvalidOperationException("请填写 Telegram API 基础地址");
        }

        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Telegram API 基础地址必须是 http 或 https 绝对地址");
        }

        var botToken = (settings.BotToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            throw new InvalidOperationException("请填写 Telegram Bot Token");
        }

        var chatId = (settings.ChatId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(chatId))
        {
            throw new InvalidOperationException("请填写 Telegram Chat ID");
        }

        return settings with
        {
            ApiBaseUrl = apiBaseUrl,
            BotToken = botToken,
            ChatId = chatId
        };
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        TelegramAlertChannelSettings settings,
        string message,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildSendMessageUri(settings))
        {
            Content = JsonContent.Create(
                new TelegramSendMessageRequest(settings.ChatId, message),
                options: JsonOptions)
        };

        return await httpClient.SendAsync(request, cancellationToken);
    }

    internal static Uri BuildSendMessageUri(TelegramAlertChannelSettings settings)
    {
        var normalized = Normalize(settings);
        return new Uri($"{normalized.ApiBaseUrl}/bot{normalized.BotToken}/sendMessage");
    }

    internal static void ThrowIfTelegramResponseFailed(HttpResponseMessage response, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Telegram Bot API 返回为空");
            }

            throw new HttpRequestException(
                $"Telegram 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                null,
                response.StatusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("ok", out var okElement) ||
                okElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                throw new InvalidOperationException("Telegram Bot API 返回格式不正确：缺少 ok 字段");
            }

            if (okElement.GetBoolean())
            {
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                throw new HttpRequestException(
                    $"Telegram 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                    null,
                    response.StatusCode);
            }

            var description = ReadOptionalString(document.RootElement, "description");
            var errorCode = ReadOptionalInt(document.RootElement, "error_code");
            if (string.IsNullOrWhiteSpace(description))
            {
                description = $"HTTP {(int)response.StatusCode} {response.StatusCode}";
            }

            throw new InvalidOperationException(errorCode is null
                ? $"Telegram Bot API 返回失败：{description}"
                : $"Telegram Bot API 返回失败(error_code={errorCode})：{description}");
        }
        catch (JsonException ex)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Telegram 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                    ex,
                    response.StatusCode);
            }

            throw new InvalidOperationException("Telegram Bot API 返回不是有效 JSON", ex);
        }
    }

    private static string ReadOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int? ReadOptionalInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private sealed record TelegramSendMessageRequest(
        [property: JsonPropertyName("chat_id")] string ChatId,
        string Text);
}

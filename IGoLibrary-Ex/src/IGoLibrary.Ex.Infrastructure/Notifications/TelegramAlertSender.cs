using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal sealed class TelegramAlertSender(
    HttpClient httpClient,
    ISettingsService settingsService) : ITelegramAlertSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SendAsync(
        TelegramAlertSettings settings,
        string message,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("Telegram 消息内容不能为空");
        }

        using var response = await ExecuteWithRequestPolicyAsync(
            token => SendOnceAsync(normalized, message, token),
            cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfTelegramResponseFailed(response, raw);
    }

    internal static TelegramAlertSettings Normalize(TelegramAlertSettings settings)
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
        TelegramAlertSettings settings,
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

    internal static Uri BuildSendMessageUri(TelegramAlertSettings settings)
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

    private async Task<HttpResponseMessage> ExecuteWithRequestPolicyAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> operation,
        CancellationToken cancellationToken)
    {
        var settings = await LoadNetworkSettingsAsync(cancellationToken);
        Exception? lastException = null;

        for (var attempt = 0; attempt <= settings.RetryCount; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(settings.Timeout);

            try
            {
                var response = await operation(timeoutCts.Token);
                if (!IsTransient(response.StatusCode))
                {
                    return response;
                }

                lastException = new HttpRequestException(
                    $"Telegram 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                    null,
                    response.StatusCode);
                response.Dispose();
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                lastException = new TimeoutException($"Telegram 请求超时（>{settings.Timeout.TotalSeconds:0} 秒）。", ex);
            }
            catch (HttpRequestException ex) when (IsTransient(ex.StatusCode))
            {
                lastException = ex;
            }

            if (attempt >= settings.RetryCount)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
        }

        throw lastException ?? new InvalidOperationException("Telegram 请求失败。");
    }

    private async Task<(TimeSpan Timeout, int RetryCount)> LoadNetworkSettingsAsync(CancellationToken cancellationToken)
    {
        AppSettings settings;
        try
        {
            settings = await settingsService.LoadAsync(cancellationToken);
        }
        catch
        {
            settings = AppSettings.Default;
        }

        var timeoutSeconds = Math.Clamp(settings.ApiTimeoutSeconds, 1, 60);
        var retryCount = Math.Clamp(settings.RetryCount, 0, 10);
        return (TimeSpan.FromSeconds(timeoutSeconds), retryCount);
    }

    private static bool IsTransient(HttpStatusCode? statusCode)
    {
        return statusCode is null
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            || (int?)statusCode >= 500;
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

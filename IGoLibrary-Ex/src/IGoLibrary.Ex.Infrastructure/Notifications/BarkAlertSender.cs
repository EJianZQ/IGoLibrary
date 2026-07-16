using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal sealed class BarkAlertSender(
    HttpClient httpClient,
    ISettingsService settingsService,
    TimeProvider? timeProvider = null) : IBarkAlertSender
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> AllowedLevels = new(StringComparer.Ordinal)
    {
        "active",
        "timeSensitive",
        "passive",
        "critical"
    };

    public async Task SendAsync(
        BarkAlertChannelSettings settings,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Bark 推送标题不能为空");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("Bark 推送内容不能为空");
        }

        using var response = await HttpNotificationRequestPolicy.ExecuteAsync(
            settingsService,
            "Bark",
            token => SendOnceAsync(normalized, title.Trim(), body.Trim(), token),
            _timeProvider,
            cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfBarkResponseFailed(response, raw);
    }

    internal static BarkAlertChannelSettings Normalize(BarkAlertChannelSettings settings)
    {
        var apiBaseUrl = (settings.ApiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            throw new InvalidOperationException("请填写 Bark 服务端地址");
        }

        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Bark 服务端地址必须是 http 或 https 绝对地址");
        }

        var deviceKey = (settings.DeviceKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(deviceKey))
        {
            throw new InvalidOperationException("请填写 Bark Device Key");
        }

        var level = (settings.Level ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(level) && !AllowedLevels.Contains(level))
        {
            throw new InvalidOperationException("Bark 通知级别必须是 active、timeSensitive、passive 或 critical");
        }

        return settings with
        {
            ApiBaseUrl = apiBaseUrl,
            DeviceKey = deviceKey,
            Group = (settings.Group ?? string.Empty).Trim(),
            Sound = (settings.Sound ?? string.Empty).Trim(),
            Level = level
        };
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        BarkAlertChannelSettings settings,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildPushUri(settings))
        {
            Content = JsonContent.Create(
                new BarkPushRequest(
                    settings.DeviceKey,
                    title,
                    body,
                    EmptyToNull(settings.Group),
                    EmptyToNull(settings.Sound),
                    EmptyToNull(settings.Level)),
                options: JsonOptions)
        };

        return await httpClient.SendAsync(request, cancellationToken);
    }

    internal static Uri BuildPushUri(BarkAlertChannelSettings settings)
    {
        var normalized = Normalize(settings);
        return new Uri($"{normalized.ApiBaseUrl}/push");
    }

    internal static void ThrowIfBarkResponseFailed(HttpResponseMessage response, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Bark API 返回为空");
            }

            throw new HttpRequestException(
                $"Bark 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                null,
                response.StatusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("code", out var codeElement))
            {
                throw new InvalidOperationException("Bark API 返回格式不正确：缺少 code 字段");
            }

            var code = ReadCode(codeElement);
            if (code == 200)
            {
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                throw new HttpRequestException(
                    $"Bark 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                    null,
                    response.StatusCode);
            }

            var message = ReadOptionalString(document.RootElement, "message");
            if (string.IsNullOrWhiteSpace(message))
            {
                message = $"HTTP {(int)response.StatusCode} {response.StatusCode}";
            }

            throw new InvalidOperationException($"Bark API 返回失败(code={code})：{message}");
        }
        catch (JsonException ex)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Bark 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                    ex,
                    response.StatusCode);
            }

            throw new InvalidOperationException("Bark API 返回不是有效 JSON", ex);
        }
    }

    private static int ReadCode(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(element.GetString(), out var value) => value,
            _ => throw new InvalidOperationException("Bark API 返回格式不正确：code 字段不是数字")
        };
    }

    private static string ReadOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed record BarkPushRequest(
        [property: JsonPropertyName("device_key")] string DeviceKey,
        string Title,
        string Body,
        string? Group,
        string? Sound,
        string? Level);
}

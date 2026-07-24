using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal sealed class ServerChanAlertSender(
    HttpClient httpClient,
    ISettingsService settingsService,
    TimeProvider? timeProvider = null,
    ILogger<ServerChanAlertSender>? logger = null) : IServerChanAlertSender
{
    private const int SuccessCode = 0;
    private const int MaxTitleLength = 32;
    private const int MaxDespBytes = 32 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ILogger<ServerChanAlertSender> _logger =
        logger ?? NullLogger<ServerChanAlertSender>.Instance;

    public async Task SendAsync(
        ServerChanAlertChannelSettings settings,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        var normalizedTitle = NormalizeTitle(title);
        var normalizedBody = NormalizeBody(body);

        using var response = await HttpNotificationRequestPolicy.ExecuteAsync(
            settingsService,
            "Server酱",
            token => SendOnceAsync(normalized, normalizedTitle, normalizedBody, token),
            _timeProvider,
            cancellationToken,
            _logger);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfServerChanResponseFailed(response, raw);
    }

    internal static ServerChanAlertChannelSettings Normalize(ServerChanAlertChannelSettings settings)
    {
        var sendKey = (settings.SendKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sendKey))
        {
            throw new InvalidOperationException("请填写 Server酱 SendKey");
        }

        if (sendKey.StartsWith("sctp", StringComparison.OrdinalIgnoreCase) &&
            !TryGetServerChan3EndpointNumber(sendKey, out _))
        {
            throw new InvalidOperationException("Server酱³ SendKey 格式不正确");
        }

        return settings with
        {
            SendKey = sendKey,
            Channel = NormalizeChannel(settings.Channel),
            OpenId = (settings.OpenId ?? string.Empty).Trim()
        };
    }

    internal static Uri BuildSendUri(ServerChanAlertChannelSettings settings)
    {
        var normalized = Normalize(settings);
        var escapedSendKey = Uri.EscapeDataString(normalized.SendKey);
        if (TryGetServerChan3EndpointNumber(normalized.SendKey, out var endpointNumber))
        {
            return new Uri($"https://{endpointNumber}.push.ft07.com/send/{escapedSendKey}.send");
        }

        return new Uri($"https://sctapi.ftqq.com/{escapedSendKey}.send");
    }

    internal static void ThrowIfServerChanResponseFailed(HttpResponseMessage response, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Server酱 API 返回为空");
            }

            throw new HttpRequestException(
                $"Server酱请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                null,
                response.StatusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("code", out var codeElement))
            {
                throw new InvalidOperationException("Server酱 API 返回格式不正确：缺少 code 字段");
            }

            var code = ReadCode(codeElement);
            if (code == SuccessCode)
            {
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                throw new HttpRequestException(
                    $"Server酱请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                    null,
                    response.StatusCode);
            }

            var message = ReadFailureMessage(document.RootElement);
            if (string.IsNullOrWhiteSpace(message))
            {
                message = $"HTTP {(int)response.StatusCode} {response.StatusCode}";
            }

            throw new InvalidOperationException($"Server酱 API 返回失败(code={code})：{message}");
        }
        catch (JsonException ex)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Server酱请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                    ex,
                    response.StatusCode);
            }

            throw new InvalidOperationException("Server酱 API 返回不是有效 JSON", ex);
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        ServerChanAlertChannelSettings settings,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildSendUri(settings))
        {
            Content = JsonContent.Create(
                new ServerChanSendRequest(
                    title,
                    body,
                    settings.NoIp ? 1 : null,
                    EmptyToNull(settings.Channel),
                    EmptyToNull(settings.OpenId)),
                options: JsonOptions)
        };

        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static string NormalizeTitle(string? title)
    {
        var normalized = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Server酱推送标题不能为空");
        }

        return normalized.Length <= MaxTitleLength
            ? normalized
            : normalized[..MaxTitleLength];
    }

    private static string NormalizeBody(string? body)
    {
        var normalized = (body ?? string.Empty).Trim();
        var byteCount = Encoding.UTF8.GetByteCount(normalized);
        if (byteCount > MaxDespBytes)
        {
            throw new InvalidOperationException("Server酱推送内容不能超过 32KB");
        }

        return normalized;
    }

    private static string NormalizeChannel(string? channel)
    {
        var normalized = (channel ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return string.Empty;
        }

        var channels = normalized.Split('|', StringSplitOptions.TrimEntries);
        if (channels.Length > 2)
        {
            throw new InvalidOperationException("Server酱 channel 最多指定两个通道");
        }

        foreach (var item in channels)
        {
            if (string.IsNullOrWhiteSpace(item) ||
                !int.TryParse(item, out var channelValue) ||
                channelValue < 0)
            {
                throw new InvalidOperationException("Server酱 channel 必须是数字，多个通道用 | 分隔");
            }
        }

        return string.Join('|', channels);
    }

    private static bool TryGetServerChan3EndpointNumber(string sendKey, out string endpointNumber)
    {
        var match = Regex.Match(
            sendKey,
            @"^sctp(\d+)t",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (match.Success)
        {
            endpointNumber = match.Groups[1].Value;
            return true;
        }

        endpointNumber = string.Empty;
        return false;
    }

    private static int ReadCode(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(element.GetString(), out var value) => value,
            _ => throw new InvalidOperationException("Server酱 API 返回格式不正确：code 字段不是数字")
        };
    }

    private static string ReadFailureMessage(JsonElement root)
    {
        var message = ReadOptionalString(root, "message");
        if (string.IsNullOrWhiteSpace(message))
        {
            message = ReadOptionalString(root, "msg");
        }

        if (!string.IsNullOrWhiteSpace(message) ||
            !root.TryGetProperty("data", out var dataElement) ||
            dataElement.ValueKind != JsonValueKind.Object)
        {
            return message;
        }

        return ReadOptionalString(dataElement, "error");
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

    private sealed record ServerChanSendRequest(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("desp")] string Desp,
        [property: JsonPropertyName("noip")] int? NoIp,
        [property: JsonPropertyName("channel")] string? Channel,
        [property: JsonPropertyName("openid")] string? OpenId);
}

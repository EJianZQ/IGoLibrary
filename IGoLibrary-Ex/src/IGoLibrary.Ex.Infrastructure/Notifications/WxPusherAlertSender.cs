using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal sealed class WxPusherAlertSender(
    HttpClient httpClient,
    ISettingsService settingsService) : IWxPusherAlertSender
{
    private const int SuccessCode = 1000;
    private const int ContentTypeText = 1;
    private const int VerifyPayTypeDisabled = 0;
    private const int MaxContentLength = 40000;
    private const int MaxSummaryLength = 100;
    private const int MaxUidCount = 2000;
    private const int MaxTopicIdCount = 5;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly char[] RecipientSeparators = [',', ';', '，', '；', ' ', '\t', '\r', '\n'];

    public async Task SendAsync(
        WxPusherAlertChannelSettings settings,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        var content = NormalizeContent(body);
        var summary = NormalizeSummary(title, content);
        var uids = ParseUids(normalized.Uids);
        var topicIds = ParseTopicIds(normalized.TopicIds);

        using var response = await ExecuteWithRequestPolicyAsync(
            token => SendOnceAsync(normalized, content, summary, uids, topicIds, token),
            cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfWxPusherResponseFailed(response, raw);
    }

    internal static WxPusherAlertChannelSettings Normalize(WxPusherAlertChannelSettings settings)
    {
        var apiBaseUrl = (settings.ApiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            throw new InvalidOperationException("请填写 WxPusher API 基础地址");
        }

        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("WxPusher API 基础地址必须是 http 或 https 绝对地址");
        }

        var appToken = (settings.AppToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(appToken))
        {
            throw new InvalidOperationException("请填写 WxPusher AppToken");
        }

        var uids = (settings.Uids ?? string.Empty).Trim();
        var topicIds = (settings.TopicIds ?? string.Empty).Trim();
        var parsedUids = ParseUids(uids);
        var parsedTopicIds = ParseTopicIds(topicIds);
        if (parsedUids.Count == 0 && parsedTopicIds.Count == 0)
        {
            throw new InvalidOperationException("请至少填写一个 WxPusher UID 或 Topic ID");
        }

        return settings with
        {
            ApiBaseUrl = apiBaseUrl,
            AppToken = appToken,
            Uids = uids,
            TopicIds = topicIds
        };
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        WxPusherAlertChannelSettings settings,
        string content,
        string summary,
        IReadOnlyList<string> uids,
        IReadOnlyList<int> topicIds,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildSendMessageUri(settings))
        {
            Content = JsonContent.Create(
                new WxPusherSendMessageRequest(
                    settings.AppToken,
                    content,
                    summary,
                    ContentTypeText,
                    topicIds.Count == 0 ? null : topicIds,
                    uids.Count == 0 ? null : uids,
                    VerifyPayTypeDisabled),
                options: JsonOptions)
        };

        return await httpClient.SendAsync(request, cancellationToken);
    }

    internal static Uri BuildSendMessageUri(WxPusherAlertChannelSettings settings)
    {
        var normalized = Normalize(settings);
        return new Uri($"{normalized.ApiBaseUrl}/api/send/message");
    }

    internal static void ThrowIfWxPusherResponseFailed(HttpResponseMessage response, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("WxPusher API 返回为空");
            }

            throw new HttpRequestException(
                $"WxPusher 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                null,
                response.StatusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("code", out var codeElement))
            {
                throw new InvalidOperationException("WxPusher API 返回格式不正确：缺少 code 字段");
            }

            var code = ReadCode(codeElement);
            if (code != SuccessCode)
            {
                var message = ReadOptionalString(document.RootElement, "msg");
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = ReadOptionalString(document.RootElement, "message");
                }

                if (string.IsNullOrWhiteSpace(message))
                {
                    message = $"HTTP {(int)response.StatusCode} {response.StatusCode}";
                }

                throw new InvalidOperationException($"WxPusher API 返回失败(code={code})：{message}");
            }

            ThrowIfAnyRecipientFailed(document.RootElement);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            throw new HttpRequestException(
                $"WxPusher 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                null,
                response.StatusCode);
        }
        catch (JsonException ex)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"WxPusher 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                    ex,
                    response.StatusCode);
            }

            throw new InvalidOperationException("WxPusher API 返回不是有效 JSON", ex);
        }
    }

    private async Task<HttpResponseMessage> ExecuteWithRequestPolicyAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> operation,
        CancellationToken cancellationToken)
    {
        var settings = await LoadNetworkSettingsAsync(cancellationToken);
        Exception? lastException = null;

        for (var attempt = 0; attempt <= settings.MaxRetries; attempt++)
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
                    $"WxPusher 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                    null,
                    response.StatusCode);
                response.Dispose();
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                lastException = new TimeoutException($"WxPusher 请求超时（>{settings.Timeout.TotalSeconds:0} 秒）。", ex);
            }
            catch (HttpRequestException ex) when (IsTransient(ex.StatusCode))
            {
                lastException = ex;
            }

            if (attempt >= settings.MaxRetries)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
        }

        throw lastException ?? new InvalidOperationException("WxPusher 请求失败。");
    }

    private async Task<(TimeSpan Timeout, int MaxRetries)> LoadNetworkSettingsAsync(CancellationToken cancellationToken)
    {
        NetworkRequestSettings settings;
        try
        {
            settings = (await settingsService.LoadAsync(cancellationToken)).Network;
        }
        catch
        {
            settings = NetworkRequestSettings.Default;
        }

        var timeoutSeconds = Math.Clamp(settings.TimeoutSeconds, 1, 60);
        var maxRetries = Math.Clamp(settings.MaxRetries, 0, 10);
        return (TimeSpan.FromSeconds(timeoutSeconds), maxRetries);
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

    private static string NormalizeContent(string? body)
    {
        var content = (body ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("WxPusher 推送内容不能为空");
        }

        if (content.Length > MaxContentLength)
        {
            throw new InvalidOperationException($"WxPusher 推送内容不能超过 {MaxContentLength} 个字符");
        }

        return content;
    }

    private static string NormalizeSummary(string? title, string content)
    {
        var summary = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = content;
        }

        return summary.Length <= MaxSummaryLength
            ? summary
            : summary[..MaxSummaryLength];
    }

    private static IReadOnlyList<string> ParseUids(string? value)
    {
        var uids = SplitRecipients(value);
        if (uids.Count > MaxUidCount)
        {
            throw new InvalidOperationException($"WxPusher UID 数量不能超过 {MaxUidCount} 个");
        }

        return uids;
    }

    private static IReadOnlyList<int> ParseTopicIds(string? value)
    {
        var rawTopicIds = SplitRecipients(value);
        if (rawTopicIds.Count > MaxTopicIdCount)
        {
            throw new InvalidOperationException($"WxPusher Topic ID 数量不能超过 {MaxTopicIdCount} 个");
        }

        var topicIds = new List<int>(rawTopicIds.Count);
        foreach (var rawTopicId in rawTopicIds)
        {
            if (!int.TryParse(rawTopicId, out var topicId) || topicId <= 0)
            {
                throw new InvalidOperationException("WxPusher Topic ID 必须是正整数");
            }

            topicIds.Add(topicId);
        }

        return topicIds;
    }

    private static IReadOnlyList<string> SplitRecipients(string? value)
    {
        return (value ?? string.Empty)
            .Split(RecipientSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static void ThrowIfAnyRecipientFailed(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var dataElement) ||
            dataElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in dataElement.EnumerateArray())
        {
            if (!item.TryGetProperty("code", out var codeElement))
            {
                continue;
            }

            var code = ReadCode(codeElement);
            if (code == SuccessCode)
            {
                continue;
            }

            var status = ReadOptionalString(item, "status");
            if (string.IsNullOrWhiteSpace(status))
            {
                status = ReadOptionalString(item, "msg");
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                status = ReadOptionalString(item, "message");
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                status = "未知错误";
            }

            var uid = ReadOptionalString(item, "uid");
            var target = string.IsNullOrWhiteSpace(uid)
                ? string.Empty
                : $"，uid={uid}";

            throw new InvalidOperationException($"WxPusher API 返回部分失败(code={code}{target})：{status}");
        }
    }

    private static int ReadCode(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(element.GetString(), out var value) => value,
            _ => throw new InvalidOperationException("WxPusher API 返回格式不正确：code 字段不是数字")
        };
    }

    private static string ReadOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private sealed record WxPusherSendMessageRequest(
        string AppToken,
        string Content,
        string Summary,
        int ContentType,
        IReadOnlyList<int>? TopicIds,
        IReadOnlyList<string>? Uids,
        int VerifyPayType);
}

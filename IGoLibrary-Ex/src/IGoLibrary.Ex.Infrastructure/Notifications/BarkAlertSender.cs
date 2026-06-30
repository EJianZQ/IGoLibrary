using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal sealed class BarkAlertSender(
    HttpClient httpClient,
    ISettingsService settingsService) : IBarkAlertSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task SendAsync(
        BarkAlertChannelSettings settings,
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Bark 通知标题不能为空");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("Bark 通知内容不能为空");
        }

        using var response = await ExecuteWithRequestPolicyAsync(
            token => SendOnceAsync(normalized, title.Trim(), message.Trim(), token),
            cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfBarkResponseFailed(response, raw);
    }

    internal static BarkAlertChannelSettings Normalize(BarkAlertChannelSettings settings)
    {
        var serverUrl = (settings.ServerUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new InvalidOperationException("请填写 Bark 服务器地址");
        }

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Bark 服务器地址必须是 http 或 https 绝对地址");
        }

        var deviceKey = (settings.DeviceKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(deviceKey))
        {
            throw new InvalidOperationException("请填写 Bark Device Key");
        }

        return settings with
        {
            ServerUrl = serverUrl,
            DeviceKey = deviceKey,
            Sound = (settings.Sound ?? string.Empty).Trim(),
            Group = string.IsNullOrWhiteSpace(settings.Group) ? BarkAlertChannelSettings.Default.Group : settings.Group.Trim()
        };
    }

    internal static Uri BuildPushUri(BarkAlertChannelSettings settings)
    {
        var normalized = Normalize(settings);
        return new Uri($"{normalized.ServerUrl}/{Uri.EscapeDataString(normalized.DeviceKey)}");
    }

    internal static void ThrowIfBarkResponseFailed(HttpResponseMessage response, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
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

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var code = ReadOptionalInt(root, "code");
            if ((code is 200 || code is null) && response.IsSuccessStatusCode)
            {
                return;
            }

            var message = ReadOptionalString(root, "message");
            if (string.IsNullOrWhiteSpace(message))
            {
                message = $"HTTP {(int)response.StatusCode} {response.StatusCode}";
            }

            throw new InvalidOperationException(code is null
                ? $"Bark API 返回失败：{message}"
                : $"Bark API 返回失败(code={code})：{message}");
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
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        BarkAlertChannelSettings settings,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildPushUri(settings))
        {
            Content = JsonContent.Create(
                new BarkPushRequest(
                    title,
                    message,
                    string.IsNullOrWhiteSpace(settings.Sound) ? null : settings.Sound,
                    string.IsNullOrWhiteSpace(settings.Group) ? null : settings.Group),
                options: JsonOptions)
        };

        return await httpClient.SendAsync(request, cancellationToken);
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
                    $"Bark 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                    null,
                    response.StatusCode);
                response.Dispose();
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                lastException = new TimeoutException($"Bark 请求超时（>{settings.Timeout.TotalSeconds:0} 秒）。", ex);
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

        throw lastException ?? new InvalidOperationException("Bark 请求失败。");
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

    private static int? ReadOptionalInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property))
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

    private static string? ReadOptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private sealed record BarkPushRequest(
        string Title,
        string Body,
        string? Sound,
        string? Group);
}

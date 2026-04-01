using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Domain.Helpers;

namespace IGoLibrary.Ex.Infrastructure.Api;

public sealed class TraceIntApiClient(
    HttpClient httpClient,
    IProtocolTemplateStore protocolTemplateStore,
    ISettingsService settingsService) : ITraceIntApiClient
{
    private const string DesktopUserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.138 Safari/537.36 NetType/WIFI MicroMessenger/7.0.20.1781(0x6700143B) WindowsWechat(0x63070626)";
    private const string AppVersion = "2.0.11";
    private static readonly Regex SeatNameRegex = new(@"^\d{1,3}$", RegexOptions.Compiled);

    public async Task<string> GetCookieFromCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var requestUrl = templates.GetCookieUrlTemplate.Replace("ReplaceMeByCode", code, StringComparison.Ordinal);
        return await ExecuteWithRequestPolicyAsync(async requestToken =>
        {
            var cookieContainer = new CookieContainer();

            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All,
                CookieContainer = cookieContainer,
                UseCookies = true
            };
            using var authClient = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            authClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DesktopUserAgent);
            authClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            authClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            using var response = await authClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
            if ((int)response.StatusCode >= 400)
            {
                response.EnsureSuccessStatusCode();
            }

            var cookiesFromContainer = ExtractImportantCookies(EnumerateCookies(
                cookieContainer,
                new Uri("http://wechat.v2.traceint.com"),
                new Uri("https://wechat.v2.traceint.com"),
                new Uri("https://web.traceint.com")));
            var cookiesFromHeaders = ExtractImportantCookies(response.Headers.TryGetValues("Set-Cookie", out var values)
                ? values
                : []);
            var cookies = !string.IsNullOrWhiteSpace(cookiesFromContainer)
                ? cookiesFromContainer
                : cookiesFromHeaders;

            if (!cookies.Contains("Authorization=", StringComparison.OrdinalIgnoreCase) ||
                !cookies.Contains("SERVERID=", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("响应中的 Cookie 缺少 Authorization 或 SERVERID。");
            }

            return cookies;
        }, cancellationToken);
    }

    public async Task ValidateCookieAsync(string cookie, CancellationToken cancellationToken = default)
    {
        _ = await GetLibrariesAsync(cookie, cancellationToken);
    }

    public async Task<IReadOnlyList<LibrarySummary>> GetLibrariesAsync(string cookie, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        using var response = await SendGraphQlAsync(cookie, templates.QueryLibrariesTemplate, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
        var libs = document.RootElement
            .GetProperty("data")
            .GetProperty("userAuth")
            .GetProperty("reserve")
            .GetProperty("libs");

        var results = new List<LibrarySummary>();
        foreach (var item in libs.EnumerateArray())
        {
            var floor = item.GetProperty("lib_floor").GetString() ?? string.Empty;
            if (floor == "0")
            {
                continue;
            }

            var runtime = item.TryGetProperty("lib_rt", out var runtimeElement)
                ? runtimeElement
                : default;

            results.Add(new LibrarySummary(
                item.GetProperty("lib_id").GetInt32(),
                item.GetProperty("lib_name").GetString() ?? "Unknown",
                floor,
                item.GetProperty("is_open").GetBoolean(),
                ReadOptionalIntProperty(runtime, "seats_total"),
                ReadOptionalIntProperty(runtime, "seats_used"),
                ReadOptionalIntProperty(runtime, "seats_booking")));
        }

        return results;
    }

    public async Task<LibraryLayout> GetLibraryLayoutAsync(string cookie, int libraryId, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var payload = templates.QueryLibraryLayoutTemplate.Replace("ReplaceMe", libraryId.ToString(), StringComparison.Ordinal);
        using var response = await SendGraphQlAsync(cookie, payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
        var lib = document.RootElement
            .GetProperty("data")
            .GetProperty("userAuth")
            .GetProperty("reserve")
            .GetProperty("libs")[0];

        var layout = lib.GetProperty("lib_layout");
        var seats = new List<SeatSnapshot>();
        foreach (var seat in layout.GetProperty("seats").EnumerateArray())
        {
            var name = seat.GetProperty("name").GetString() ?? string.Empty;
            if (!SeatNameRegex.IsMatch(name))
            {
                continue;
            }

            seats.Add(new SeatSnapshot(
                seat.GetProperty("key").GetString() ?? string.Empty,
                name,
                seat.GetProperty("status").GetBoolean(),
                seat.GetProperty("x").GetInt32(),
                seat.GetProperty("y").GetInt32()));
        }

        return new LibraryLayout(
            lib.GetProperty("lib_id").GetInt32(),
            lib.GetProperty("lib_name").GetString() ?? "Unknown",
            lib.GetProperty("lib_floor").GetString() ?? string.Empty,
            lib.GetProperty("is_open").GetBoolean(),
            layout.GetProperty("seats_total").GetInt32(),
            layout.GetProperty("seats_booking").GetInt32(),
            layout.GetProperty("seats_used").GetInt32(),
            seats.OrderBy(x => int.TryParse(x.SeatName, out var number) ? number : int.MaxValue).ToList());
    }

    public async Task<LibraryRule> GetLibraryRuleAsync(string cookie, int libraryId, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var payload = templates.QueryLibraryRuleTemplate.Replace("ReplaceMe", libraryId.ToString(), StringComparison.Ordinal);
        using var response = await SendGraphQlAsync(cookie, payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
        var rule = document.RootElement
            .GetProperty("data")
            .GetProperty("userAuth")
            .GetProperty("reserve")
            .GetProperty("libRule");

        return new LibraryRule(
            libraryId,
            rule.GetProperty("advance_booking").GetString() ?? string.Empty,
            rule.GetProperty("lib_seat_ttl").GetString() ?? string.Empty,
            rule.GetProperty("lib_hold_ttl").GetString() ?? string.Empty,
            rule.GetProperty("lib_renew_time").GetString() ?? string.Empty,
            rule.GetProperty("hold_reason").GetString() ?? string.Empty,
            rule.TryGetProperty("close_start_date", out var closeStartDate) && closeStartDate.ValueKind != JsonValueKind.Null
                ? closeStartDate.GetString()
                : null,
            rule.TryGetProperty("close_end_date", out var closeEndDate) && closeEndDate.ValueKind != JsonValueKind.Null
                ? closeEndDate.GetString()
                : null,
            rule.GetProperty("open_time").GetInt64(),
            rule.GetProperty("open_time_str").GetString() ?? string.Empty,
            rule.GetProperty("close_time").GetInt64(),
            rule.GetProperty("close_time_str").GetString() ?? string.Empty,
            rule.GetProperty("lib_validate_time").GetInt32());
    }

    public async Task<ReservationInfo?> GetReservationInfoAsync(string cookie, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        using var response = await SendGraphQlAsync(cookie, templates.QueryReservationInfoTemplate, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
        var reserveNode = document.RootElement
            .GetProperty("data")
            .GetProperty("userAuth")
            .GetProperty("reserve");

        if (!reserveNode.TryGetProperty("reserve", out var reservation) || reservation.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var token = reserveNode.GetProperty("getSToken").GetString() ?? string.Empty;
        var expirationTimestamp = reservation.GetProperty("exp_date").GetInt64();

        return new ReservationInfo(
            token,
            reservation.GetProperty("lib_id").GetInt32(),
            reservation.GetProperty("lib_name").GetString() ?? string.Empty,
            reservation.GetProperty("seat_key").GetString() ?? string.Empty,
            reservation.GetProperty("seat_name").GetString() ?? string.Empty,
            ReservationTimeHelper.FromUnixSeconds(expirationTimestamp));
    }

    public async Task<bool> ReserveSeatAsync(string cookie, int libraryId, string seatKey, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var payload = templates.ReserveSeatTemplate
            .Replace("ReplaceMeBySeatKey", seatKey, StringComparison.Ordinal)
            .Replace("ReplaceMeByLibID", libraryId.ToString(), StringComparison.Ordinal);

        using var response = await SendGraphQlAsync(cookie, payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);

        ThrowIfGraphQlError(document.RootElement);
        var reserveResult = document.RootElement
            .GetProperty("data")
            .GetProperty("userAuth")
            .GetProperty("reserve")
            .GetProperty("reserueSeat");

        return ReadBooleanLike(reserveResult, "reserueSeat");
    }

    public async Task<bool> CancelReservationAsync(string cookie, string reservationToken, CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var payload = templates.CancelReservationTemplate.Replace("ReplaceMe", reservationToken, StringComparison.Ordinal);

        using var response = await SendGraphQlAsync(cookie, payload, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);

        if (document.RootElement.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            var message = errors[0].GetProperty("msg").GetString() ?? string.Empty;
            return message.Contains("成功", StringComparison.OrdinalIgnoreCase);
        }

        if (document.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("userAuth", out var userAuth) &&
            userAuth.TryGetProperty("reserve", out var reserve) &&
            reserve.TryGetProperty("reserveCancle", out _))
        {
            return true;
        }

        return false;
    }

    private async Task<HttpResponseMessage> SendGraphQlAsync(string cookie, string payload, CancellationToken cancellationToken)
    {
        return await ExecuteWithRequestPolicyAsync(async requestToken =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://wechat.v2.traceint.com/index.php/graphql/");
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            request.Headers.Host = "wechat.v2.traceint.com";
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            request.Headers.TryAddWithoutValidation("Connection", "keep-alive");
            request.Headers.TryAddWithoutValidation("Origin", "https://web.traceint.com");
            request.Headers.TryAddWithoutValidation("Referer", "https://web.traceint.com/web/index.html");
            request.Headers.TryAddWithoutValidation("User-Agent", DesktopUserAgent);
            request.Headers.TryAddWithoutValidation("App-Version", AppVersion);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            request.Headers.ExpectContinue = false;

            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            request.Content = new ByteArrayContent(payloadBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Content.Headers.ContentLength = payloadBytes.Length;

            var response = await httpClient.SendAsync(request, requestToken);
            response.EnsureSuccessStatusCode();
            return response;
        }, cancellationToken);
    }

    private async Task<T> ExecuteWithRequestPolicyAsync<T>(
        Func<CancellationToken, Task<T>> operation,
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
                return await operation(timeoutCts.Token);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                lastException = new TimeoutException($"请求超时（>{settings.Timeout.TotalSeconds:0} 秒）。", ex);
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

        throw lastException ?? new InvalidOperationException("请求失败。");
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

    private static void ThrowIfGraphQlError(JsonElement root)
    {
        if (root.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            var error = errors[0];
            var message = error.GetProperty("msg").GetString() ?? "未知错误";
            var code = error.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
                ? codeElement.GetInt32().ToString()
                : "n/a";
            throw new InvalidOperationException($"GraphQL 错误(code={code}): {message}");
        }
    }

    private static bool ReadBooleanLike(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(element.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue != 0,
            _ => throw new InvalidOperationException($"字段 {fieldName} 的返回类型不受支持: {element.ValueKind}")
        };
    }

    private static int ReadOptionalIntProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.String when int.TryParse(property.GetString(), out var intValue) => intValue,
            _ => 0
        };
    }

    private static string ExtractImportantCookies(IEnumerable<string> setCookieHeaders)
    {
        var allowedKeys = new[]
        {
            "FROM_TYPE", "FROM_CODE", "v", "wechatSESS_ID", "Authorization", "SERVERID"
        };

        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in setCookieHeaders)
        {
            var nameValue = header.Split(';', 2)[0];
            var index = nameValue.IndexOf('=', StringComparison.Ordinal);
            if (index <= 0)
            {
                continue;
            }

            var name = nameValue[..index].Trim();
            var value = nameValue[(index + 1)..].Trim();
            if (allowedKeys.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                cookies[name] = value;
            }
        }

        return string.Join("; ", allowedKeys.Where(cookies.ContainsKey).Select(key => $"{key}={cookies[key]}"));
    }

    private static IEnumerable<string> EnumerateCookies(CookieContainer cookieContainer, params Uri[] uris)
    {
        foreach (var uri in uris)
        {
            foreach (Cookie cookie in cookieContainer.GetCookies(uri))
            {
                yield return $"{cookie.Name}={cookie.Value}";
            }
        }
    }
}

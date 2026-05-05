using System.Net;
using System.Text.Json;
using IGoLibrary.Ex.Application.Exceptions;

namespace IGoLibrary.Ex.Application.Services;

public static class CookieExpiryDetector
{
    private static readonly string[] Keywords =
    [
        "cookie",
        "授权",
        "未登录",
        "登录",
        "失效",
        "过期",
        "token",
        "unauthorized",
        "forbidden",
        "expired"
    ];

    public static bool IsExpired(string? cookie, DateTimeOffset? now = null)
    {
        return TryGetExpirationTime(cookie, out var expirationTime) &&
               expirationTime <= (now ?? DateTimeOffset.Now);
    }

    public static bool TryGetExpirationTime(string? cookie, out DateTimeOffset expirationTime)
    {
        expirationTime = default;
        if (!TryExtractAuthorizationToken(cookie, out var token))
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        try
        {
            var payloadBytes = DecodeBase64Url(parts[1]);
            using var document = JsonDocument.Parse(payloadBytes);
            if (TryReadUnixSeconds(document.RootElement, "expireAt", out var unixSeconds) ||
                TryReadUnixSeconds(document.RootElement, "exp", out unixSeconds))
            {
                expirationTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static string BuildExpiredMessage(DateTimeOffset expirationTime)
    {
        return $"Cookie 已过期，到期时间：{expirationTime:yyyy-MM-dd HH:mm:ss}";
    }

    public static bool IsKnownExpiredCookieException(Exception exception, string? cookie)
    {
        if (TryGetExpirationTime(cookie, out _))
        {
            return IsExpired(cookie);
        }

        return IsExpired(exception);
    }

    public static bool IsExpired(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is TraceIntApiException traceIntApiException &&
                traceIntApiException.IsAuthorizationDenied)
            {
                return true;
            }

            if (current is HttpRequestException httpRequestException &&
                httpRequestException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return true;
            }

            var message = current.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            if (Keywords.Any(keyword => message.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractAuthorizationToken(string? cookie, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return false;
        }

        foreach (var segment in cookie.Split(';'))
        {
            var trimmed = segment.Trim();
            if (trimmed.StartsWith("Authorization=", StringComparison.OrdinalIgnoreCase))
            {
                token = trimmed["Authorization=".Length..].Trim();
                return LooksLikeJwt(token);
            }
        }

        var candidate = cookie.Trim();
        if (candidate.StartsWith("Authorization=", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate["Authorization=".Length..].Trim();
        }

        token = candidate;
        return LooksLikeJwt(token);
    }

    private static bool LooksLikeJwt(string token)
    {
        return token.Count(ch => ch == '.') == 2 &&
               token.StartsWith("ey", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => throw new FormatException("JWT payload is not valid base64url.")
        };

        return Convert.FromBase64String(normalized);
    }

    private static bool TryReadUnixSeconds(JsonElement root, string propertyName, out long unixSeconds)
    {
        unixSeconds = default;
        if (root.ValueKind is not JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        switch (property.ValueKind)
        {
            case JsonValueKind.Number when property.TryGetInt64(out unixSeconds):
                return true;
            case JsonValueKind.String when long.TryParse(property.GetString(), out unixSeconds):
                return true;
            default:
                return false;
        }
    }
}

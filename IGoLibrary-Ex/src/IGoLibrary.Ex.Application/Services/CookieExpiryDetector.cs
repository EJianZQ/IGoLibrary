using System.Net;
using IGoLibrary.Ex.Application.Exceptions;

namespace IGoLibrary.Ex.Application.Services;

internal static class CookieExpiryDetector
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
}

using System.Net;
using System.Text;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.Services;

namespace IGoLibrary.Ex.Tests;

public sealed class CookieExpiryDetectorTests
{
    [Fact]
    public void TryGetExpirationTime_ReturnsExpireAt_FromAuthorizationJwtCookie()
    {
        var expiresAt = DateTimeOffset.Now.AddHours(2);
        var cookie = BuildAuthorizationCookie(expiresAt);

        var parsed = CookieExpiryDetector.TryGetExpirationTime(cookie, out var parsedExpirationTime);

        Assert.True(parsed);
        Assert.Equal(expiresAt.ToUnixTimeSeconds(), parsedExpirationTime.ToUnixTimeSeconds());
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenAuthorizationJwtExpireAtIsPast()
    {
        var now = DateTimeOffset.Now;
        var cookie = BuildAuthorizationCookie(now.AddSeconds(-1));

        var isExpired = CookieExpiryDetector.IsExpired(cookie, now);

        Assert.True(isExpired);
    }

    [Fact]
    public void IsExpired_ReturnsFalse_WhenAuthorizationJwtExpireAtIsFuture()
    {
        var now = DateTimeOffset.Now;
        var cookie = BuildAuthorizationCookie(now.AddMinutes(5));

        var isExpired = CookieExpiryDetector.IsExpired(cookie, now);

        Assert.False(isExpired);
    }

    [Fact]
    public void IsKnownExpiredCookieException_IgnoresUnauthorizedException_WhenJwtIsStillValid()
    {
        var cookie = BuildAuthorizationCookie(DateTimeOffset.Now.AddMinutes(5));
        var exception = new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);

        var isExpired = CookieExpiryDetector.IsKnownExpiredCookieException(exception, cookie);

        Assert.False(isExpired);
    }

    [Fact]
    public void IsExpired_ReturnsTrue_ForAccessDenied40001ApiError()
    {
        var exception = new TraceIntApiException("access denied!", 40001, "access denied!", isAuthorizationDenied: true);

        var isExpired = CookieExpiryDetector.IsExpired(exception);

        Assert.True(isExpired);
    }

    [Fact]
    public void IsExpired_ReturnsFalse_ForOtherStructuredApiError()
    {
        var exception = new TraceIntApiException("Too Many Requests", 42900, "Too Many Requests");

        var isExpired = CookieExpiryDetector.IsExpired(exception);

        Assert.False(isExpired);
    }

    private static string BuildAuthorizationCookie(DateTimeOffset expiresAt)
    {
        var header = Base64Url("""{"typ":"JWT","alg":"RS256"}""");
        var payload = Base64Url($$"""{"userId":37580434,"schId":20175,"expireAt":{{expiresAt.ToUnixTimeSeconds()}},"tag":"cookie-test"}""");
        return $"Authorization={header}.{payload}.signature; SERVERID=d3936289adfff6c3874a2579058ac651|1777956374|1777956374";
    }

    private static string Base64Url(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

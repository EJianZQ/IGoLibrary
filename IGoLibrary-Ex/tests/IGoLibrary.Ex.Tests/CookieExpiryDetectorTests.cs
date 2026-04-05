using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.Services;

namespace IGoLibrary.Ex.Tests;

public sealed class CookieExpiryDetectorTests
{
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
}

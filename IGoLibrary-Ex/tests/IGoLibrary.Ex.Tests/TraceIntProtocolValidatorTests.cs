namespace IGoLibrary.Ex.Tests;

public sealed class TraceIntProtocolValidatorTests
{
    [Fact]
    public void Validate_AcceptsAbsoluteCustomAndPrivateAddresses()
    {
        var templates = TestProtocolTemplates.Create() with
        {
            GraphQlEndpointUrl = "http://127.0.0.1:18080/graphql?tenant=school",
            GraphQlDefaultOriginUrl = "http://192.168.1.20:19090/",
            TomorrowReservationQueueUrlTemplate = "ws://localhost:18081/queue"
        };

        var result = TraceIntProtocolValidator.Validate(templates);
        var normalized = TraceIntProtocolValidator.Normalize(templates);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal("http://192.168.1.20:19090", normalized.GraphQlDefaultOriginUrl);
    }

    [Fact]
    public void Validate_RejectsInvalidSchemesPartsOriginsAndPlaceholders()
    {
        var templates = TestProtocolTemplates.Create() with
        {
            GraphQlEndpointUrl = "/relative/graphql",
            GraphQlDefaultOriginUrl = "https://example.com/path?query=1",
            TomorrowReservationQueueUrlTemplate = "https://example.com/ws",
            RemoteCheckInAuthUrlTemplate = "https://example.com/auth?code=ReplaceMeByCode",
            RemoteCheckInDevicesEndpointUrl = "https://user:password@example.com/devices#fragment",
            RemoteCheckInApiRefererUrl = string.Empty
        };

        var result = TraceIntProtocolValidator.Validate(templates);
        var errorProperties = result.Errors.Select(static issue => issue.PropertyName).ToHashSet();

        Assert.False(result.IsValid);
        Assert.Contains(nameof(TraceIntProtocolTemplates.GraphQlEndpointUrl), errorProperties);
        Assert.Contains(nameof(TraceIntProtocolTemplates.GraphQlDefaultOriginUrl), errorProperties);
        Assert.Contains(nameof(TraceIntProtocolTemplates.TomorrowReservationQueueUrlTemplate), errorProperties);
        Assert.Contains(nameof(TraceIntProtocolTemplates.RemoteCheckInAuthUrlTemplate), errorProperties);
        Assert.Contains(nameof(TraceIntProtocolTemplates.RemoteCheckInDevicesEndpointUrl), errorProperties);
        Assert.Contains(nameof(TraceIntProtocolTemplates.RemoteCheckInApiRefererUrl), errorProperties);
    }

    [Fact]
    public void Validate_AllowsLegacyCookieTemplateAndReportsCompatibilityWarning()
    {
        var templates = TestProtocolTemplates.Create() with
        {
            GetCookieUrlTemplate = "https://example.com/auth?r=https%3A%2F%2Fexample.com%2Fapp&code=ReplaceMeByCode"
        };

        var result = TraceIntProtocolValidator.Validate(templates);

        Assert.True(result.IsValid);
        Assert.Single(result.Warnings);
        Assert.Equal(nameof(TraceIntProtocolTemplates.GetCookieUrlTemplate), result.Warnings[0].PropertyName);
    }

    [Fact]
    public void BuildAuthorizationUrl_EscapesCodeAndReturnUrl()
    {
        var result = TraceIntProtocolValidator.BuildAuthorizationUrl(
            "https://example.com/auth?r=ReplaceMeByReturnUrl&code=ReplaceMeByCode",
            "a+b",
            "https://example.com/app?x=1&y=2");

        Assert.Contains("code=a%2Bb", result, StringComparison.Ordinal);
        Assert.Contains("r=https%3A%2F%2Fexample.com%2Fapp%3Fx%3D1%26y%3D2", result, StringComparison.Ordinal);
    }
}

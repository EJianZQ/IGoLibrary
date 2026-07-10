using System.Text.Json.Serialization;

namespace IGoLibrary.Ex.Application.Protocol;

public sealed record TraceIntProtocolTemplateOverrides
{
    public string? GetCookieUrlTemplate { get; init; }

    public string? CookieAuthorizationReturnUrl { get; init; }

    public string? GraphQlEndpointUrl { get; init; }

    public string? GraphQlDefaultRefererUrl { get; init; }

    public string? GraphQlDefaultOriginUrl { get; init; }

    public string? GraphQlTomorrowRefererUrl { get; init; }

    public string? GraphQlTomorrowOriginUrl { get; init; }

    public string? TomorrowReservationQueueUrlTemplate { get; init; }

    public string? RemoteCheckInAuthUrlTemplate { get; init; }

    public string? RemoteCheckInAuthorizationReturnUrl { get; init; }

    public string? RemoteCheckInAuthRefererUrl { get; init; }

    public string? RemoteCheckInDevicesEndpointUrl { get; init; }

    public string? RemoteCheckInTimeEndpointUrl { get; init; }

    public string? RemoteCheckInSignEndpointUrl { get; init; }

    public string? RemoteCheckInApiRefererUrl { get; init; }

    public string? QueryLibrariesTemplate { get; init; }

    public string? QueryLibraryLayoutTemplate { get; init; }

    public string? QueryLibraryRuleTemplate { get; init; }

    public string? QueryReservationInfoTemplate { get; init; }

    public string? ReserveSeatTemplate { get; init; }

    public string? CancelReservationTemplate { get; init; }

    public string? TomorrowReservationWarmUpTemplate { get; init; }

    public string? TomorrowReservationSaveTemplate { get; init; }

    public string? TomorrowReservationInfoTemplate { get; init; }

    [JsonIgnore]
    public bool HasAnyValue => GetValues().Any(static value => !string.IsNullOrWhiteSpace(value));

    public static TraceIntProtocolTemplateOverrides FromDifferences(
        TraceIntProtocolTemplates current,
        TraceIntProtocolTemplates defaults)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(defaults);

        var normalizedCurrent = TraceIntProtocolValidator.Normalize(current);
        var normalizedDefaults = TraceIntProtocolValidator.Normalize(defaults);
        return new TraceIntProtocolTemplateOverrides
        {
            GetCookieUrlTemplate = Difference(normalizedCurrent.GetCookieUrlTemplate, normalizedDefaults.GetCookieUrlTemplate),
            CookieAuthorizationReturnUrl = Difference(normalizedCurrent.CookieAuthorizationReturnUrl, normalizedDefaults.CookieAuthorizationReturnUrl),
            GraphQlEndpointUrl = Difference(normalizedCurrent.GraphQlEndpointUrl, normalizedDefaults.GraphQlEndpointUrl),
            GraphQlDefaultRefererUrl = Difference(normalizedCurrent.GraphQlDefaultRefererUrl, normalizedDefaults.GraphQlDefaultRefererUrl),
            GraphQlDefaultOriginUrl = Difference(normalizedCurrent.GraphQlDefaultOriginUrl, normalizedDefaults.GraphQlDefaultOriginUrl),
            GraphQlTomorrowRefererUrl = Difference(normalizedCurrent.GraphQlTomorrowRefererUrl, normalizedDefaults.GraphQlTomorrowRefererUrl),
            GraphQlTomorrowOriginUrl = Difference(normalizedCurrent.GraphQlTomorrowOriginUrl, normalizedDefaults.GraphQlTomorrowOriginUrl),
            TomorrowReservationQueueUrlTemplate = Difference(normalizedCurrent.TomorrowReservationQueueUrlTemplate, normalizedDefaults.TomorrowReservationQueueUrlTemplate),
            RemoteCheckInAuthUrlTemplate = Difference(normalizedCurrent.RemoteCheckInAuthUrlTemplate, normalizedDefaults.RemoteCheckInAuthUrlTemplate),
            RemoteCheckInAuthorizationReturnUrl = Difference(normalizedCurrent.RemoteCheckInAuthorizationReturnUrl, normalizedDefaults.RemoteCheckInAuthorizationReturnUrl),
            RemoteCheckInAuthRefererUrl = Difference(normalizedCurrent.RemoteCheckInAuthRefererUrl, normalizedDefaults.RemoteCheckInAuthRefererUrl),
            RemoteCheckInDevicesEndpointUrl = Difference(normalizedCurrent.RemoteCheckInDevicesEndpointUrl, normalizedDefaults.RemoteCheckInDevicesEndpointUrl),
            RemoteCheckInTimeEndpointUrl = Difference(normalizedCurrent.RemoteCheckInTimeEndpointUrl, normalizedDefaults.RemoteCheckInTimeEndpointUrl),
            RemoteCheckInSignEndpointUrl = Difference(normalizedCurrent.RemoteCheckInSignEndpointUrl, normalizedDefaults.RemoteCheckInSignEndpointUrl),
            RemoteCheckInApiRefererUrl = Difference(normalizedCurrent.RemoteCheckInApiRefererUrl, normalizedDefaults.RemoteCheckInApiRefererUrl),
            QueryLibrariesTemplate = Difference(normalizedCurrent.QueryLibrariesTemplate, normalizedDefaults.QueryLibrariesTemplate),
            QueryLibraryLayoutTemplate = Difference(normalizedCurrent.QueryLibraryLayoutTemplate, normalizedDefaults.QueryLibraryLayoutTemplate),
            QueryLibraryRuleTemplate = Difference(normalizedCurrent.QueryLibraryRuleTemplate, normalizedDefaults.QueryLibraryRuleTemplate),
            QueryReservationInfoTemplate = Difference(normalizedCurrent.QueryReservationInfoTemplate, normalizedDefaults.QueryReservationInfoTemplate),
            ReserveSeatTemplate = Difference(normalizedCurrent.ReserveSeatTemplate, normalizedDefaults.ReserveSeatTemplate),
            CancelReservationTemplate = Difference(normalizedCurrent.CancelReservationTemplate, normalizedDefaults.CancelReservationTemplate),
            TomorrowReservationWarmUpTemplate = Difference(normalizedCurrent.TomorrowReservationWarmUpTemplate, normalizedDefaults.TomorrowReservationWarmUpTemplate),
            TomorrowReservationSaveTemplate = Difference(normalizedCurrent.TomorrowReservationSaveTemplate, normalizedDefaults.TomorrowReservationSaveTemplate),
            TomorrowReservationInfoTemplate = Difference(normalizedCurrent.TomorrowReservationInfoTemplate, normalizedDefaults.TomorrowReservationInfoTemplate)
        };
    }

    private IEnumerable<string?> GetValues()
    {
        yield return GetCookieUrlTemplate;
        yield return CookieAuthorizationReturnUrl;
        yield return GraphQlEndpointUrl;
        yield return GraphQlDefaultRefererUrl;
        yield return GraphQlDefaultOriginUrl;
        yield return GraphQlTomorrowRefererUrl;
        yield return GraphQlTomorrowOriginUrl;
        yield return TomorrowReservationQueueUrlTemplate;
        yield return RemoteCheckInAuthUrlTemplate;
        yield return RemoteCheckInAuthorizationReturnUrl;
        yield return RemoteCheckInAuthRefererUrl;
        yield return RemoteCheckInDevicesEndpointUrl;
        yield return RemoteCheckInTimeEndpointUrl;
        yield return RemoteCheckInSignEndpointUrl;
        yield return RemoteCheckInApiRefererUrl;
        yield return QueryLibrariesTemplate;
        yield return QueryLibraryLayoutTemplate;
        yield return QueryLibraryRuleTemplate;
        yield return QueryReservationInfoTemplate;
        yield return ReserveSeatTemplate;
        yield return CancelReservationTemplate;
        yield return TomorrowReservationWarmUpTemplate;
        yield return TomorrowReservationSaveTemplate;
        yield return TomorrowReservationInfoTemplate;
    }

    private static string? Difference(string current, string defaultValue)
    {
        return string.Equals(current, defaultValue, StringComparison.Ordinal) ? null : current;
    }
}

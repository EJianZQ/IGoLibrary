namespace IGoLibrary.Ex.Application.Protocol;

public sealed record TraceIntProtocolTemplates
{
    public required string GetCookieUrlTemplate { get; init; }

    public required string CookieAuthorizationReturnUrl { get; init; }

    public required string GraphQlEndpointUrl { get; init; }

    public required string GraphQlDefaultRefererUrl { get; init; }

    public required string GraphQlDefaultOriginUrl { get; init; }

    public required string GraphQlTomorrowRefererUrl { get; init; }

    public required string GraphQlTomorrowOriginUrl { get; init; }

    public required string TomorrowReservationQueueUrlTemplate { get; init; }

    public required string RemoteCheckInAuthUrlTemplate { get; init; }

    public required string RemoteCheckInAuthorizationReturnUrl { get; init; }

    public required string RemoteCheckInAuthRefererUrl { get; init; }

    public required string RemoteCheckInDevicesEndpointUrl { get; init; }

    public required string RemoteCheckInTimeEndpointUrl { get; init; }

    public required string RemoteCheckInSignEndpointUrl { get; init; }

    public required string RemoteCheckInApiRefererUrl { get; init; }

    public required string QueryLibrariesTemplate { get; init; }

    public required string QueryLibraryLayoutTemplate { get; init; }

    public required string QueryLibraryRuleTemplate { get; init; }

    public required string QueryReservationInfoTemplate { get; init; }

    public required string ReserveSeatTemplate { get; init; }

    public required string CancelReservationTemplate { get; init; }

    public required string TomorrowReservationWarmUpTemplate { get; init; }

    public required string TomorrowReservationSaveTemplate { get; init; }

    public required string TomorrowReservationInfoTemplate { get; init; }
}

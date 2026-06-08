namespace IGoLibrary.Ex.Application.Protocol;

public sealed record TraceIntGraphQlTemplateOverrides(
    string? GetCookieUrlTemplate = null,
    string? QueryLibrariesTemplate = null,
    string? QueryLibraryLayoutTemplate = null,
    string? QueryLibraryRuleTemplate = null,
    string? QueryReservationInfoTemplate = null,
    string? ReserveSeatTemplate = null,
    string? CancelReservationTemplate = null,
    string? TomorrowReservationQueueUrlTemplate = null,
    string? TomorrowReservationWarmUpTemplate = null,
    string? TomorrowReservationSaveTemplate = null,
    string? TomorrowReservationInfoTemplate = null)
{
    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(GetCookieUrlTemplate) ||
        !string.IsNullOrWhiteSpace(QueryLibrariesTemplate) ||
        !string.IsNullOrWhiteSpace(QueryLibraryLayoutTemplate) ||
        !string.IsNullOrWhiteSpace(QueryLibraryRuleTemplate) ||
        !string.IsNullOrWhiteSpace(QueryReservationInfoTemplate) ||
        !string.IsNullOrWhiteSpace(ReserveSeatTemplate) ||
        !string.IsNullOrWhiteSpace(CancelReservationTemplate) ||
        !string.IsNullOrWhiteSpace(TomorrowReservationQueueUrlTemplate) ||
        !string.IsNullOrWhiteSpace(TomorrowReservationWarmUpTemplate) ||
        !string.IsNullOrWhiteSpace(TomorrowReservationSaveTemplate) ||
        !string.IsNullOrWhiteSpace(TomorrowReservationInfoTemplate);
}

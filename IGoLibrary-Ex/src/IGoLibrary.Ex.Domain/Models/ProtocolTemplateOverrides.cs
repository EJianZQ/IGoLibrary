namespace IGoLibrary.Ex.Domain.Models;

public sealed record ProtocolTemplateOverrides(
    string? GetCookieUrlTemplate = null,
    string? QueryLibrariesTemplate = null,
    string? QueryLibraryLayoutTemplate = null,
    string? QueryLibraryRuleTemplate = null,
    string? QueryReservationInfoTemplate = null,
    string? ReserveSeatTemplate = null,
    string? CancelReservationTemplate = null)
{
    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(GetCookieUrlTemplate) ||
        !string.IsNullOrWhiteSpace(QueryLibrariesTemplate) ||
        !string.IsNullOrWhiteSpace(QueryLibraryLayoutTemplate) ||
        !string.IsNullOrWhiteSpace(QueryLibraryRuleTemplate) ||
        !string.IsNullOrWhiteSpace(QueryReservationInfoTemplate) ||
        !string.IsNullOrWhiteSpace(ReserveSeatTemplate) ||
        !string.IsNullOrWhiteSpace(CancelReservationTemplate);
}

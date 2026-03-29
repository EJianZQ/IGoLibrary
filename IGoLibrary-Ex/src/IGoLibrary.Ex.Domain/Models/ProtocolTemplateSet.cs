namespace IGoLibrary.Ex.Domain.Models;

public sealed record ProtocolTemplateSet(
    string GetCookieUrlTemplate,
    string QueryLibrariesTemplate,
    string QueryLibraryLayoutTemplate,
    string QueryLibraryRuleTemplate,
    string QueryReservationInfoTemplate,
    string ReserveSeatTemplate,
    string CancelReservationTemplate);

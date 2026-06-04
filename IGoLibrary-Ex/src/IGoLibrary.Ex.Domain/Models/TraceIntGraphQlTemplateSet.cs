namespace IGoLibrary.Ex.Domain.Models;

public sealed record TraceIntGraphQlTemplateSet(
    string GetCookieUrlTemplate,
    string QueryLibrariesTemplate,
    string QueryLibraryLayoutTemplate,
    string QueryLibraryRuleTemplate,
    string QueryReservationInfoTemplate,
    string ReserveSeatTemplate,
    string CancelReservationTemplate);

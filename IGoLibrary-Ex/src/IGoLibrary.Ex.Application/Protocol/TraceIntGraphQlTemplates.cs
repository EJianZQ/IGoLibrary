namespace IGoLibrary.Ex.Application.Protocol;

public sealed record TraceIntGraphQlTemplates(
    string GetCookieUrlTemplate,
    string QueryLibrariesTemplate,
    string QueryLibraryLayoutTemplate,
    string QueryLibraryRuleTemplate,
    string QueryReservationInfoTemplate,
    string ReserveSeatTemplate,
    string CancelReservationTemplate,
    string TomorrowReservationQueueUrlTemplate = "",
    string TomorrowReservationWarmUpTemplate = "",
    string TomorrowReservationSaveTemplate = "",
    string TomorrowReservationInfoTemplate = "");

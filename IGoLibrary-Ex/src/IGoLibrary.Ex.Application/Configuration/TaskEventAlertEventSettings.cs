namespace IGoLibrary.Ex.Application.Configuration;

public sealed record TaskEventAlertEventSettings
{
    public bool CookieExpiring { get; init; } = true;

    public bool GrabSucceeded { get; init; } = true;

    public bool OccupyReReserveSucceeded { get; init; } = true;

    public bool TomorrowReservationSucceeded { get; init; } = true;

    public bool GlobalLeakSucceeded { get; init; } = true;

    public bool SessionInvalid { get; init; } = true;

    public bool TaskFailed { get; init; } = true;

    public static TaskEventAlertEventSettings Default { get; } = new();
}

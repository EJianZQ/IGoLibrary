using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Application.Configuration;

public sealed record TaskExecutionSettings
{
    public GrabTaskSettings Grab { get; init; } = GrabTaskSettings.Default;

    public OccupyTaskSettings Occupy { get; init; } = OccupyTaskSettings.Default;

    public TaskExecutionSettings()
    {
    }

    public TaskExecutionSettings(GrabTaskSettings grab, OccupyTaskSettings occupy)
    {
        Grab = grab;
        Occupy = occupy;
    }

    public TaskExecutionSettings(GrabReservationStrategy grabReservationStrategy)
        : this(new GrabTaskSettings(grabReservationStrategy), OccupyTaskSettings.Default)
    {
    }

    public static TaskExecutionSettings Default { get; } = new();
}

public sealed record GrabTaskSettings
{
    public GrabReservationStrategy ReservationStrategy { get; init; } = GrabReservationStrategy.QueryThenReserve;

    public GrabTaskSettings()
    {
    }

    public GrabTaskSettings(GrabReservationStrategy reservationStrategy)
    {
        ReservationStrategy = reservationStrategy;
    }

    public static GrabTaskSettings Default { get; } = new();
}

public sealed record OccupyTaskSettings
{
    public int ReReservationMaxAttempts { get; init; } = 4;

    public OccupyTaskSettings()
    {
    }

    public OccupyTaskSettings(int reReservationMaxAttempts)
    {
        ReReservationMaxAttempts = reReservationMaxAttempts;
    }

    public static OccupyTaskSettings Default { get; } = new();
}

using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record TaskExecutionSettings
{
    public GrabReservationStrategy GrabReservationStrategy { get; init; } = GrabReservationStrategy.QueryThenReserve;

    public TaskExecutionSettings()
    {
    }

    public TaskExecutionSettings(GrabReservationStrategy grabReservationStrategy)
    {
        GrabReservationStrategy = grabReservationStrategy;
    }

    public static TaskExecutionSettings Default { get; } = new();
}

using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Application.Configuration;

public sealed record TaskExecutionSettings
{
    public GrabTaskSettings Grab { get; init; } = GrabTaskSettings.Default;

    public OccupyTaskSettings Occupy { get; init; } = OccupyTaskSettings.Default;

    public TomorrowReservationTaskSettings TomorrowReservation { get; init; } = TomorrowReservationTaskSettings.Default;

    public GlobalLeakTaskSettings GlobalLeak { get; init; } = GlobalLeakTaskSettings.Default;

    public TaskExecutionSettings()
    {
    }

    public TaskExecutionSettings(
        GrabTaskSettings grab,
        OccupyTaskSettings occupy,
        TomorrowReservationTaskSettings? tomorrowReservation = null,
        GlobalLeakTaskSettings? globalLeak = null)
    {
        Grab = grab;
        Occupy = occupy;
        TomorrowReservation = tomorrowReservation ?? TomorrowReservationTaskSettings.Default;
        GlobalLeak = globalLeak ?? GlobalLeakTaskSettings.Default;
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

    public TimeSpan DefaultScheduledStartTime { get; init; } = TimeSpan.Zero;

    public GrabTaskSettings()
    {
    }

    public GrabTaskSettings(GrabReservationStrategy reservationStrategy)
    {
        ReservationStrategy = reservationStrategy;
    }

    public GrabTaskSettings(GrabReservationStrategy reservationStrategy, TimeSpan defaultScheduledStartTime)
    {
        ReservationStrategy = reservationStrategy;
        DefaultScheduledStartTime = defaultScheduledStartTime;
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

public sealed record TomorrowReservationTaskSettings
{
    public TimeSpan DefaultScheduledStartTime { get; init; } = new(20, 0, 0);

    public TomorrowReservationTaskSettings()
    {
    }

    public TomorrowReservationTaskSettings(TimeSpan defaultScheduledStartTime)
    {
        DefaultScheduledStartTime = defaultScheduledStartTime;
    }

    public static TomorrowReservationTaskSettings Default { get; } = new();
}

public sealed record GlobalLeakTaskSettings
{
    public IReadOnlyList<GlobalLeakLibrarySelectionSettings> SelectedLibraries { get; init; } = [];

    public GlobalLeakTaskSettings()
    {
    }

    public GlobalLeakTaskSettings(IReadOnlyList<GlobalLeakLibrarySelectionSettings>? selectedLibraries)
    {
        SelectedLibraries = selectedLibraries ?? [];
    }

    public static GlobalLeakTaskSettings Default { get; } = new();
}

public sealed record GlobalLeakLibrarySelectionSettings(
    int LibraryId,
    string LibraryName,
    string Floor);

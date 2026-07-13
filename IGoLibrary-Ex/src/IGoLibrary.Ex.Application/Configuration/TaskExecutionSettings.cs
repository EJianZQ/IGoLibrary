using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Application.Configuration;

public sealed record TaskExecutionSettings
{
    public GrabTaskSettings Grab { get; init; } = GrabTaskSettings.Default;

    public OccupyTaskSettings Occupy { get; init; } = OccupyTaskSettings.Default;

    public AutoReleaseTaskSettings AutoRelease { get; init; } = AutoReleaseTaskSettings.Default;

    public TomorrowReservationTaskSettings TomorrowReservation { get; init; } = TomorrowReservationTaskSettings.Default;

    public GlobalLeakTaskSettings GlobalLeak { get; init; } = GlobalLeakTaskSettings.Default;

    public TaskExecutionSettings()
    {
    }

    public TaskExecutionSettings(
        GrabTaskSettings grab,
        OccupyTaskSettings occupy,
        TomorrowReservationTaskSettings? tomorrowReservation = null,
        GlobalLeakTaskSettings? globalLeak = null,
        AutoReleaseTaskSettings? autoRelease = null)
    {
        Grab = grab;
        Occupy = occupy;
        AutoRelease = autoRelease ?? AutoReleaseTaskSettings.Default;
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

    public bool OptimalStrategyReminderEnabled { get; init; } = true;

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

    public GrabTaskSettings(
        GrabReservationStrategy reservationStrategy,
        bool optimalStrategyReminderEnabled,
        TimeSpan defaultScheduledStartTime)
    {
        ReservationStrategy = reservationStrategy;
        OptimalStrategyReminderEnabled = optimalStrategyReminderEnabled;
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

public sealed record AutoReleaseTaskSettings
{
    public const int DefaultLeadSeconds = 60;
    public const int MinLeadSeconds = 1;
    public const int MaxLeadSeconds = 3600;

    public bool Enabled { get; init; }

    public int LeadSeconds { get; init; } = DefaultLeadSeconds;

    public AutoReleaseTaskSettings()
    {
    }

    public AutoReleaseTaskSettings(bool enabled, int leadSeconds)
    {
        Enabled = enabled;
        LeadSeconds = NormalizeLeadSeconds(leadSeconds);
    }

    public static int NormalizeLeadSeconds(int value)
    {
        return Math.Clamp(value, MinLeadSeconds, MaxLeadSeconds);
    }

    public static AutoReleaseTaskSettings Default { get; } = new();
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
    /// <summary>
    /// 场馆按扫描优先级从高到低排列；集合中的第一项会在每轮最先扫描。
    /// </summary>
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

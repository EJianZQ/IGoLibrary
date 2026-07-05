namespace IGoLibrary.Ex.Application.Configuration;

public enum HomeReservationProgressTimingMode
{
    FixedReservationDuration = 0,
    SoftwareRuntimeDuration = 1
}

public sealed record HomeReservationProgressSettings
{
    public const int MinFixedDurationMinutes = 1;
    public const int MaxFixedDurationMinutes = 180;
    public const int DefaultFixedDurationMinutes = 30;

    public HomeReservationProgressTimingMode Mode { get; init; } =
        HomeReservationProgressTimingMode.FixedReservationDuration;

    public int FixedDurationMinutes { get; init; } = DefaultFixedDurationMinutes;

    public HomeReservationProgressSettings()
    {
    }

    public HomeReservationProgressSettings(
        HomeReservationProgressTimingMode mode,
        int fixedDurationMinutes)
    {
        Mode = NormalizeMode(mode);
        FixedDurationMinutes = NormalizeFixedDurationMinutes(fixedDurationMinutes);
    }

    public static HomeReservationProgressSettings Default { get; } = new();

    public static HomeReservationProgressSettings Normalize(HomeReservationProgressSettings? settings)
    {
        settings ??= Default;
        return new HomeReservationProgressSettings(
            NormalizeMode(settings.Mode),
            NormalizeFixedDurationMinutes(settings.FixedDurationMinutes));
    }

    public static HomeReservationProgressTimingMode NormalizeMode(HomeReservationProgressTimingMode mode)
    {
        return Enum.IsDefined(mode)
            ? mode
            : HomeReservationProgressTimingMode.FixedReservationDuration;
    }

    public static int NormalizeFixedDurationMinutes(int value)
    {
        return Math.Clamp(value, MinFixedDurationMinutes, MaxFixedDurationMinutes);
    }
}

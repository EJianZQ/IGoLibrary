namespace IGoLibrary.Ex.Application.Configuration;

public enum HomeCookieProgressTimingMode
{
    FixedCookieDuration = 0,
    SoftwareRuntimeDuration = 1
}

public sealed record HomeCookieProgressSettings
{
    public const int MinFixedDurationMinutes = 1;
    public const int MaxFixedDurationMinutes = 1440;
    public const int DefaultFixedDurationMinutes = 120;

    public HomeCookieProgressTimingMode Mode { get; init; } =
        HomeCookieProgressTimingMode.FixedCookieDuration;

    public int FixedDurationMinutes { get; init; } = DefaultFixedDurationMinutes;

    public HomeCookieProgressSettings()
    {
    }

    public HomeCookieProgressSettings(
        HomeCookieProgressTimingMode mode,
        int fixedDurationMinutes)
    {
        Mode = NormalizeMode(mode);
        FixedDurationMinutes = NormalizeFixedDurationMinutes(fixedDurationMinutes);
    }

    public static HomeCookieProgressSettings Default { get; } = new();

    public static HomeCookieProgressSettings Normalize(HomeCookieProgressSettings? settings)
    {
        settings ??= Default;
        return new HomeCookieProgressSettings(
            NormalizeMode(settings.Mode),
            NormalizeFixedDurationMinutes(settings.FixedDurationMinutes));
    }

    public static HomeCookieProgressTimingMode NormalizeMode(HomeCookieProgressTimingMode mode)
    {
        return Enum.IsDefined(mode)
            ? mode
            : HomeCookieProgressTimingMode.FixedCookieDuration;
    }

    public static int NormalizeFixedDurationMinutes(int value)
    {
        return Math.Clamp(value, MinFixedDurationMinutes, MaxFixedDurationMinutes);
    }
}

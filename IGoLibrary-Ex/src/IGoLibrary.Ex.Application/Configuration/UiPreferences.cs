namespace IGoLibrary.Ex.Application.Configuration;

public sealed record UiPreferences
{
    public bool MinimizeToTray { get; init; } = true;

    public bool LaunchOnStartup { get; init; }

    public ThemePreferences? Theme { get; init; } = ThemePreferences.Default;

    public HomeReservationProgressSettings? HomeReservationProgress { get; init; } =
        HomeReservationProgressSettings.Default;

    public HomeCookieProgressSettings? HomeCookieProgress { get; init; } =
        HomeCookieProgressSettings.Default;

    public UiPreferences()
    {
    }

    public UiPreferences(bool minimizeToTray, bool launchOnStartup, ThemePreferences? theme)
    {
        MinimizeToTray = minimizeToTray;
        LaunchOnStartup = launchOnStartup;
        Theme = theme;
        HomeReservationProgress = HomeReservationProgressSettings.Default;
        HomeCookieProgress = HomeCookieProgressSettings.Default;
    }

    public static UiPreferences Default { get; } = new();
}

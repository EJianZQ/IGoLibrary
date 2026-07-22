using System.Text.Json.Serialization;

namespace IGoLibrary.Ex.Application.Configuration;

public sealed record UiPreferences
{
    public bool MinimizeToTray { get; init; } = true;

    [JsonPropertyName("preventSystemSleepWhileTasksActive")]
    public bool PreventSystemSleepWhileTasksActive { get; init; } = true;

    public bool LaunchOnStartup { get; init; }

    [JsonPropertyName("windowSize")]
    public MainViewSizePreferences? MainViewSize { get; init; } = MainViewSizePreferences.Default;

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
        MainViewSize = MainViewSizePreferences.Default;
        Theme = theme;
        HomeReservationProgress = HomeReservationProgressSettings.Default;
        HomeCookieProgress = HomeCookieProgressSettings.Default;
    }

    public static UiPreferences Default { get; } = new();
}

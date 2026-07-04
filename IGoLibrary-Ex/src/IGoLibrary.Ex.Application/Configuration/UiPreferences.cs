namespace IGoLibrary.Ex.Application.Configuration;

public sealed record UiPreferences
{
    public bool MinimizeToTray { get; init; } = true;

    public bool LaunchOnStartup { get; init; }

    public ThemePreferences? Theme { get; init; } = ThemePreferences.Default;

    public UiPreferences()
    {
    }

    public UiPreferences(bool minimizeToTray, bool launchOnStartup, ThemePreferences? theme)
    {
        MinimizeToTray = minimizeToTray;
        LaunchOnStartup = launchOnStartup;
        Theme = theme;
    }

    public static UiPreferences Default { get; } = new();
}

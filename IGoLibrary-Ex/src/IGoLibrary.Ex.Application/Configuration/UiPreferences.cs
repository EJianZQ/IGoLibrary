namespace IGoLibrary.Ex.Application.Configuration;

public sealed record UiPreferences
{
    public bool MinimizeToTray { get; init; } = true;

    public ThemePreferences? Theme { get; init; } = ThemePreferences.Default;

    public UiPreferences()
    {
    }

    public UiPreferences(bool minimizeToTray, ThemePreferences? theme)
    {
        MinimizeToTray = minimizeToTray;
        Theme = theme;
    }

    public static UiPreferences Default { get; } = new();
}

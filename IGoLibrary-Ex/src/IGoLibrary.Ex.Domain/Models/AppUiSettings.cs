namespace IGoLibrary.Ex.Domain.Models;

public sealed record AppUiSettings
{
    public bool MinimizeToTray { get; init; } = true;

    public ThemeSettings? Theme { get; init; } = ThemeSettings.Default;

    public AppUiSettings()
    {
    }

    public AppUiSettings(bool minimizeToTray, ThemeSettings? theme)
    {
        MinimizeToTray = minimizeToTray;
        Theme = theme;
    }

    public static AppUiSettings Default { get; } = new();
}

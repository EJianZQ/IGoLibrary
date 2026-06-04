using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record ThemeSettings
{
    public AppThemeMode Mode { get; init; } = AppThemeMode.FollowSystem;

    public bool UseSystemAccent { get; init; } = OperatingSystem.IsWindows();

    public ThemeSettings()
    {
    }

    public ThemeSettings(AppThemeMode mode, bool useSystemAccent)
    {
        Mode = mode;
        UseSystemAccent = useSystemAccent;
    }

    public static ThemeSettings Default { get; } = new();
}

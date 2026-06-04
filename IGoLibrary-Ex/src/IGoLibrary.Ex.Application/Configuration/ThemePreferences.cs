using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Application.Configuration;

public sealed record ThemePreferences
{
    public AppThemeMode Mode { get; init; } = AppThemeMode.FollowSystem;

    public bool UseSystemAccent { get; init; }

    public ThemePreferences()
    {
    }

    public ThemePreferences(AppThemeMode mode, bool useSystemAccent)
    {
        Mode = mode;
        UseSystemAccent = useSystemAccent;
    }

    public static ThemePreferences Default { get; } = new();
}

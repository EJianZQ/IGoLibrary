using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class DesktopAppSettingsDefaults : IAppSettingsDefaults
{
    public AppSettings CreateDefault()
    {
        var defaults = AppSettings.Default;
        var ui = defaults.Ui ?? UiPreferences.Default;
        var theme = ui.Theme ?? ThemePreferences.Default;
        return defaults with
        {
            Ui = ui with
            {
                Theme = theme with
                {
                    Mode = AppThemeMode.FollowSystem,
                    UseSystemAccent = OperatingSystem.IsWindows()
                }
            }
        };
    }
}

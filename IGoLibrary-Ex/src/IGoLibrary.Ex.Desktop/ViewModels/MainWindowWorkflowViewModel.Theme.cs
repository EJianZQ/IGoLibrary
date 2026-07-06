using Avalonia.Threading;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _themePaletteSubscribed;

    public string[] ThemeModes => SystemSettings.ThemeModes;

    public int SelectedAppThemeModeIndex
    {
        get => SystemSettings.SelectedAppThemeModeIndex;
        set
        {
            EnsureSystemSettingsConfigured();
            SystemSettings.SelectedAppThemeModeIndex = value;
        }
    }

    public bool UseSystemAccent
    {
        get => SystemSettings.UseSystemAccent;
        set
        {
            EnsureSystemSettingsConfigured();
            SystemSettings.UseSystemAccent = value;
        }
    }

    private void OnThemePaletteChanged(object? sender, AppThemePalette palette)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyThemePalette(palette);
            return;
        }

        Dispatcher.UIThread.Post(() => ApplyThemePalette(palette));
    }

    private void ApplyThemePalette(AppThemePalette palette)
    {
        Session.ApplyThemePalette(palette);
        HomeDashboard.ApplyThemePalette(palette);
        AccountVenue.ApplyThemePalette(palette);
        GrabPage.ApplyThemePalette(palette);
        GlobalLeakPage.ApplyThemePalette(palette);
        TomorrowReservationPage.ApplyThemePalette(palette);
        foreach (var logLine in OccupyLogLines)
        {
            logLine.RefreshTheme();
        }

        OnPropertyChanged(nameof(GrabDashboardStatusBrush));
        OnPropertyChanged(nameof(TomorrowDashboardStatusBrush));
        RefreshSidebarSessionExpirationPresentation(GetCurrentTime());
        UpdateHomeDashboardPresentation();
    }
}

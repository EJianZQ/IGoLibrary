using Avalonia.Media;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _homeDashboardConfigured;

    public string[] HomeReservationProgressTimingModes => HomeDashboard.HomeReservationProgressTimingModes;

    public string HomeGreetingTitleText
    {
        get => HomeDashboard.HomeGreetingTitleText;
        set => HomeDashboard.HomeGreetingTitleText = value;
    }

    public string HomeGreetingMessageText
    {
        get => HomeDashboard.HomeGreetingMessageText;
        set => HomeDashboard.HomeGreetingMessageText = value;
    }

    public string HomeDateText
    {
        get => HomeDashboard.HomeDateText;
        set => HomeDashboard.HomeDateText = value;
    }

    public string HomeTimeText
    {
        get => HomeDashboard.HomeTimeText;
        set => HomeDashboard.HomeTimeText = value;
    }

    public string HomeHeroStatusText
    {
        get => HomeDashboard.HomeHeroStatusText;
        set => HomeDashboard.HomeHeroStatusText = value;
    }

    public string HomeHeroStatusDetailText
    {
        get => HomeDashboard.HomeHeroStatusDetailText;
        set => HomeDashboard.HomeHeroStatusDetailText = value;
    }

    public IBrush HomeHeroStatusBrush
    {
        get => HomeDashboard.HomeHeroStatusBrush;
        set => HomeDashboard.HomeHeroStatusBrush = value;
    }

    public IBrush HomeHeroStatusBackgroundBrush
    {
        get => HomeDashboard.HomeHeroStatusBackgroundBrush;
        set => HomeDashboard.HomeHeroStatusBackgroundBrush = value;
    }

    public int HomeHistoricalSuccessCount
    {
        get => HomeDashboard.HomeHistoricalSuccessCount;
        set => HomeDashboard.HomeHistoricalSuccessCount = value;
    }

    public string HomeTotalGuardDurationText
    {
        get => HomeDashboard.HomeTotalGuardDurationText;
        set => HomeDashboard.HomeTotalGuardDurationText = value;
    }

    public string HomeEngineSummaryText
    {
        get => HomeDashboard.HomeEngineSummaryText;
        set => HomeDashboard.HomeEngineSummaryText = value;
    }

    public string HomeMemoryUsageText
    {
        get => HomeDashboard.HomeMemoryUsageText;
        set => HomeDashboard.HomeMemoryUsageText = value;
    }

    public string HomeReservationSeatNumberText
    {
        get => HomeDashboard.HomeReservationSeatNumberText;
        set => HomeDashboard.HomeReservationSeatNumberText = value;
    }

    public string HomeReservationExpirationTimeText
    {
        get => HomeDashboard.HomeReservationExpirationTimeText;
        set => HomeDashboard.HomeReservationExpirationTimeText = value;
    }

    public string HomeReservationBadgeText
    {
        get => HomeDashboard.HomeReservationBadgeText;
        set => HomeDashboard.HomeReservationBadgeText = value;
    }

    public IBrush HomeReservationBadgeBrush
    {
        get => HomeDashboard.HomeReservationBadgeBrush;
        set => HomeDashboard.HomeReservationBadgeBrush = value;
    }

    public IBrush HomeReservationBadgeBackgroundBrush
    {
        get => HomeDashboard.HomeReservationBadgeBackgroundBrush;
        set => HomeDashboard.HomeReservationBadgeBackgroundBrush = value;
    }

    public string HomeReservationRemainingText
    {
        get => HomeDashboard.HomeReservationRemainingText;
        set => HomeDashboard.HomeReservationRemainingText = value;
    }

    public double HomeReservationProgressValue
    {
        get => HomeDashboard.HomeReservationProgressValue;
        set => HomeDashboard.HomeReservationProgressValue = value;
    }

    public IBrush HomeReservationProgressBrush
    {
        get => HomeDashboard.HomeReservationProgressBrush;
        set => HomeDashboard.HomeReservationProgressBrush = value;
    }

    public int SelectedHomeReservationProgressTimingModeIndex
    {
        get => HomeDashboard.SelectedHomeReservationProgressTimingModeIndex;
        set => HomeDashboard.SelectedHomeReservationProgressTimingModeIndex = value;
    }

    public bool IsHomeReservationFixedProgressMode => HomeDashboard.IsHomeReservationFixedProgressMode;

    public int HomeReservationFixedDurationMinutes
    {
        get => HomeDashboard.HomeReservationFixedDurationMinutes;
        set => HomeDashboard.HomeReservationFixedDurationMinutes = value;
    }

    private HomeReservationProgressTimingMode CurrentHomeReservationProgressTimingMode =>
        HomeDashboard.CurrentHomeReservationProgressTimingMode;

    private void EnsureHomeDashboardConfigured()
    {
        if (_homeDashboardConfigured)
        {
            return;
        }

        _homeDashboardConfigured = true;
        HomeDashboard.Configure(
            () => IsAuthorized,
            () => IsGrabTaskActive,
            () => IsGlobalLeakTaskActive,
            () => IsTomorrowTaskActive,
            () => IsOccupyRunning,
            () => HasLockedVenue,
            () => OccupyPage.CurrentReservation,
            value => HomeReservationVenueText = value,
            metrics => SystemSettings.SaveDashboardMetricsAsync(metrics),
            ScheduleSystemSettingsAutoSave);
    }

    private void ConfigureHomeDashboardPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            HomeDashboard,
            nameof(HomeDashboard.HomeGreetingTitleText),
            nameof(HomeDashboard.HomeGreetingMessageText),
            nameof(HomeDashboard.HomeDateText),
            nameof(HomeDashboard.HomeTimeText),
            nameof(HomeDashboard.HomeHeroStatusText),
            nameof(HomeDashboard.HomeHeroStatusDetailText),
            nameof(HomeDashboard.HomeHeroStatusBrush),
            nameof(HomeDashboard.HomeHeroStatusBackgroundBrush),
            nameof(HomeDashboard.HomeHistoricalSuccessCount),
            nameof(HomeDashboard.HomeTotalGuardDurationText),
            nameof(HomeDashboard.HomeEngineSummaryText),
            nameof(HomeDashboard.HomeMemoryUsageText),
            nameof(HomeDashboard.HomeReservationSeatNumberText),
            nameof(HomeDashboard.HomeReservationExpirationTimeText),
            nameof(HomeDashboard.HomeReservationBadgeText),
            nameof(HomeDashboard.HomeReservationBadgeBrush),
            nameof(HomeDashboard.HomeReservationBadgeBackgroundBrush),
            nameof(HomeDashboard.HomeReservationRemainingText),
            nameof(HomeDashboard.HomeReservationProgressValue),
            nameof(HomeDashboard.HomeReservationProgressBrush),
            nameof(HomeDashboard.SelectedHomeReservationProgressTimingModeIndex),
            nameof(HomeDashboard.IsHomeReservationFixedProgressMode),
            nameof(HomeDashboard.HomeReservationFixedDurationMinutes));
    }

    private void UpdateHomeDashboardPresentation()
    {
        EnsureHomeDashboardConfigured();
        HomeDashboard.UpdatePresentation();
        AccountVenue.RefreshHomeLockedVenuePresentation();
        UpdateHomeCookieCardPresentation(GetCurrentTime());
    }

    private void UpdateHomeDashboardClock()
    {
        EnsureHomeDashboardConfigured();
        HomeDashboard.UpdateClock();
        UpdateHomeCookieCardPresentation(GetCurrentTime());
    }

    private void UpdateHomeHeroPresentation(DateTimeOffset now)
    {
        EnsureHomeDashboardConfigured();
        HomeDashboard.UpdateHeroPresentation(now);
    }

    private void UpdateHomeReservationCardPresentation(DateTimeOffset now)
    {
        EnsureHomeDashboardConfigured();
        HomeDashboard.UpdateReservationCardPresentation(now);
    }

    private void UpdateHomeSystemInfoPresentation()
    {
        EnsureHomeDashboardConfigured();
        HomeDashboard.UpdateSystemInfoPresentation();
    }

    private void UpdateGuardTracking(DateTimeOffset timestamp)
    {
        EnsureHomeDashboardConfigured();
        HomeDashboard.UpdateGuardTracking(timestamp);
    }

    private Task RecordSuccessfulReservationAsync()
    {
        EnsureHomeDashboardConfigured();
        return HomeDashboard.RecordSuccessfulReservationAsync();
    }

    private void EnsureHomeReservationProgressTracking(ReservationInfo reservation, DateTimeOffset observedAt)
    {
        EnsureHomeDashboardConfigured();
        HomeDashboard.EnsureReservationProgressTracking(reservation, observedAt);
    }

    private void ClearHomeReservationProgressTracking()
    {
        EnsureHomeDashboardConfigured();
        HomeDashboard.ClearReservationProgressTracking();
    }
}

using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record AppSettings(
    bool NotificationsEnabled,
    bool MinimizeToTray,
    bool CustomApiOverridesEnabled,
    int ApiTimeoutSeconds,
    int RetryCount,
    AppThemeMode ThemeMode,
    bool UseSystemAccent,
    GrabReservationStrategy GrabReservationStrategy,
    int? LastLibraryId,
    string? LastLibraryName,
    CookieExpiryAlertSettings? CookieExpiryAlerts = null,
    int SuccessfulReservationCount = 0,
    long TotalGuardSeconds = 0)
{
    public static AppSettings Default { get; } = new(
        NotificationsEnabled: true,
        MinimizeToTray: true,
        CustomApiOverridesEnabled: false,
        ApiTimeoutSeconds: 5,
        RetryCount: 3,
        ThemeMode: AppThemeMode.FollowSystem,
        UseSystemAccent: OperatingSystem.IsWindows(),
        GrabReservationStrategy: GrabReservationStrategy.QueryThenReserve,
        LastLibraryId: null,
        LastLibraryName: null,
        CookieExpiryAlerts: CookieExpiryAlertSettings.Default,
        SuccessfulReservationCount: 0,
        TotalGuardSeconds: 0);
}

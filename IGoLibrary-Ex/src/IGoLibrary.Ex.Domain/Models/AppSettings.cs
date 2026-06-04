using System.Text.Json.Serialization;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record AppSettings(
    bool NotificationsEnabled,
    bool MinimizeToTray,
    [property: JsonPropertyName("customApiOverridesEnabled")]
    bool ProtocolTemplateOverridesEnabled,
    int ApiTimeoutSeconds,
    int RetryCount,
    AppThemeMode ThemeMode,
    bool UseSystemAccent,
    GrabReservationStrategy GrabReservationStrategy,
    int? LastLibraryId,
    string? LastLibraryName,
    [property: JsonPropertyName("cookieExpiryAlerts")]
    TaskEventAlertSettings? TaskEventAlerts = null,
    int SuccessfulReservationCount = 0,
    long TotalGuardSeconds = 0)
{
    public static AppSettings Default { get; } = new(
        NotificationsEnabled: true,
        MinimizeToTray: true,
        ProtocolTemplateOverridesEnabled: false,
        ApiTimeoutSeconds: 5,
        RetryCount: 3,
        ThemeMode: AppThemeMode.FollowSystem,
        UseSystemAccent: OperatingSystem.IsWindows(),
        GrabReservationStrategy: GrabReservationStrategy.QueryThenReserve,
        LastLibraryId: null,
        LastLibraryName: null,
        TaskEventAlerts: TaskEventAlertSettings.Default,
        SuccessfulReservationCount: 0,
        TotalGuardSeconds: 0);
}

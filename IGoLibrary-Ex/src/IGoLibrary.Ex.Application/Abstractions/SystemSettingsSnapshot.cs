using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Application.Abstractions;

public sealed record SystemSettingsSnapshot(
    bool AppBannerNotificationsEnabled,
    bool MinimizeToTray,
    bool TraceIntGraphQlOverridesEnabled,
    int RequestTimeoutSeconds,
    int NetworkMaxRetries,
    ThemePreferences Theme,
    GrabReservationStrategy GrabReservationStrategy,
    TaskEventAlertSettings TaskEventAlerts);

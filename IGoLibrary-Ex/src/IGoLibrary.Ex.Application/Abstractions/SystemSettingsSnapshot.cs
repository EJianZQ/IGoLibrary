using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Application.Abstractions;

public sealed record SystemSettingsSnapshot(
    bool MinimizeToTray,
    bool TraceIntGraphQlOverridesEnabled,
    bool CheckUpdatesOnStartup,
    int RequestTimeoutSeconds,
    int NetworkMaxRetries,
    ThemePreferences Theme,
    GrabReservationStrategy GrabReservationStrategy,
    TaskEventAlertSettings TaskEventAlerts);

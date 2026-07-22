using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Application.Abstractions;

public sealed record SystemSettingsSnapshot(
    bool MinimizeToTray,
    bool PreventSystemSleepWhileTasksActive,
    bool LaunchOnStartup,
    bool RememberMainViewSize,
    bool TraceIntGraphQlOverridesEnabled,
    bool CheckUpdatesOnStartup,
    int RequestTimeoutSeconds,
    int NetworkMaxRetries,
    ThemePreferences Theme,
    HomeReservationProgressSettings HomeReservationProgress,
    HomeCookieProgressSettings HomeCookieProgress,
    GrabReservationStrategy GrabReservationStrategy,
    bool OptimalGrabStrategyReminderEnabled,
    bool AutoReleaseEnabled,
    int AutoReleaseLeadSeconds,
    TaskEventAlertSettings TaskEventAlerts);

using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public sealed record SystemSettingsSnapshot(
    bool AppBannerNotificationsEnabled,
    bool MinimizeToTray,
    bool ProtocolTemplateOverridesEnabled,
    int RequestTimeoutSeconds,
    int RequestRetryCount,
    ThemeSettings Theme,
    GrabReservationStrategy GrabReservationStrategy,
    TaskEventAlertSettings TaskEventAlerts);

using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record AppSettings(
    bool NotificationsEnabled,
    bool MinimizeToTray,
    bool AdvancedMode,
    int ApiTimeoutSeconds,
    int RetryCount,
    GrabReservationStrategy GrabReservationStrategy,
    int? LastLibraryId,
    string? LastLibraryName)
{
    public static AppSettings Default { get; } = new(
        NotificationsEnabled: true,
        MinimizeToTray: true,
        AdvancedMode: false,
        ApiTimeoutSeconds: 5,
        RetryCount: 3,
        GrabReservationStrategy: GrabReservationStrategy.QueryThenReserve,
        LastLibraryId: null,
        LastLibraryName: null);
}

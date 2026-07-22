namespace IGoLibrary.Ex.Infrastructure.Persistence;

internal static class AppDatabaseSchema
{
    // ASCII "IGOE". This distinguishes application backups from arbitrary SQLite files.
    public const int ApplicationId = 0x49474F45;

    public const int CurrentVersion = 1;

    public static readonly string[] RequiredTables =
    [
        "Settings",
        "Favorites",
        "SeatLabels",
        "ProtocolOverrides",
        "MobileTaskLaunchHistory"
    ];
}

namespace IGoLibrary.Ex.Desktop.Services;

public sealed record MobileControlStatusSnapshot(
    DateTimeOffset GeneratedAt,
    string GeneratedAtText,
    MobileControlCookieSnapshot Cookie,
    MobileControlReservationSnapshot Reservation,
    MobileControlGrabTaskSnapshot Grab,
    MobileControlGlobalLeakTaskSnapshot GlobalLeak,
    MobileControlTomorrowTaskSnapshot Tomorrow,
    MobileControlOccupyTaskSnapshot Occupy);

public sealed record MobileControlCookieSnapshot(
    bool IsAuthorized,
    string StatusText,
    string SourceText,
    string SavedAtText,
    string ExpirationTimeText,
    string RemainingText,
    double ProgressValue,
    string ProgressLevel);

public sealed record MobileControlReservationSnapshot(
    bool HasReservation,
    string SummaryText,
    string LibraryName,
    string SeatName,
    string ExpirationTimeText,
    string RemainingText,
    double ProgressValue,
    string ProgressLevel);

public sealed record MobileControlGrabTaskSnapshot(
    string StateText,
    string StatusText,
    bool IsActive,
    int PollCount,
    int RequestCount,
    string LastRequestText,
    string RuntimeText,
    IReadOnlyList<string> Logs);

public sealed record MobileControlGlobalLeakTaskSnapshot(
    string StateText,
    string StatusText,
    bool IsActive,
    int ScanRoundCount,
    int RequestCount,
    string LastRequestText,
    string RuntimeText,
    IReadOnlyList<string> Logs);

public sealed record MobileControlTomorrowTaskSnapshot(
    string StateText,
    string StatusText,
    bool IsActive,
    string ScheduledTimeText,
    int RequestCount,
    string LastRequestText,
    string VerificationText,
    string RuntimeText,
    IReadOnlyList<string> Logs);

public sealed record MobileControlOccupyTaskSnapshot(
    string StateText,
    string StatusText,
    bool IsActive,
    string ExpirationTimeText,
    string ReReserveCountdownText,
    IReadOnlyList<string> Logs);

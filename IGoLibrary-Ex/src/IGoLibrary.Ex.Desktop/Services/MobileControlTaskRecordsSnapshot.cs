namespace IGoLibrary.Ex.Desktop.Services;

public sealed record MobileControlTaskRecordsSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<MobileControlGrabTaskRecordSnapshot> Grab,
    IReadOnlyList<MobileControlGlobalLeakTaskRecordSnapshot> GlobalLeak);

public sealed record MobileControlGrabTaskRecordSnapshot(
    string RecordId,
    string RecordedAtText,
    string LibraryName,
    IReadOnlyList<string> SeatNames,
    string PollingModeText,
    string ReservationStrategyText);

public sealed record MobileControlGlobalLeakTaskRecordSnapshot(
    string RecordId,
    string RecordedAtText,
    IReadOnlyList<MobileControlGlobalLeakLibrarySnapshot> Libraries,
    double ScanIntervalSeconds);

public sealed record MobileControlGlobalLeakLibrarySnapshot(
    string LibraryName,
    string Floor);

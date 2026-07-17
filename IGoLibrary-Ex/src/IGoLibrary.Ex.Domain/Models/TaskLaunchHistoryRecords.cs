using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record GrabTaskLaunchRecord(
    string RecordId,
    DateTimeOffset RecordedAtUtc,
    int LibraryId,
    string LibraryName,
    IReadOnlyList<SeatReference> Seats,
    GrabPollingMode PollingMode,
    GrabReservationStrategy ReservationStrategy);

public sealed record GlobalLeakTaskLaunchRecord(
    string RecordId,
    DateTimeOffset RecordedAtUtc,
    IReadOnlyList<GlobalLeakLibraryTarget> Libraries,
    TimeSpan ScanInterval);

public sealed record TaskLaunchHistorySaveResult(
    string RecordId,
    bool RefreshedExisting,
    int PrunedCount);

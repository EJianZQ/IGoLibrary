namespace IGoLibrary.Ex.Domain.Models;

public sealed record RemoteCheckInSessionCredentials(
    string Token,
    DateTimeOffset SavedAt,
    bool CanAutoRestore,
    DateTimeOffset? ExpiresAt = null);

public sealed record RemoteCheckInOAuthExchangeResult(
    string Token,
    DateTimeOffset? ExpiresAt);

public sealed record RemoteCheckInUserSummary(
    string Nickname,
    string School,
    string StudentName,
    string StudentNumber);

public sealed record RemoteCheckInDeviceInfo(
    RemoteCheckInUserSummary User,
    IReadOnlyList<string> BeaconUuids);

public sealed record RemoteCheckInAuthorizationResult(
    RemoteCheckInSessionCredentials Session,
    RemoteCheckInDeviceInfo? DeviceInfo,
    string? DeviceRefreshWarning);

public sealed record RemoteCheckInServerTime(
    string Value,
    long UnixSeconds);

public sealed record RemoteCheckInSignPlan(
    int ExpectedLibraryId,
    string ExpectedLibraryName,
    string BeaconUuid,
    int Major,
    int Minor,
    decimal Latitude,
    decimal Longitude);

public sealed record RemoteCheckInSignRequest(
    string BeaconUuid,
    ushort Major,
    ushort Minor,
    decimal Latitude,
    decimal Longitude,
    string ServerTimestamp);

public sealed record RemoteCheckInResult(
    string Message,
    int? Status,
    int? LibraryId,
    string LibraryName,
    string LibraryFloor,
    string SeatKey,
    string SeatName,
    DateTimeOffset? SignedAt,
    DateTimeOffset? ExpirationTime);

using System.Text.Json;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

internal static class TaskLaunchHistoryPayloadCodec
{
    private const int CurrentVersion = 1;
    private static readonly TimeSpan MinimumGlobalLeakInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumGlobalLeakInterval = TimeSpan.FromHours(1);

    public static string Serialize(GrabTaskLaunchRecord record)
    {
        var payload = new GrabPayload(
            CurrentVersion,
            record.LibraryId,
            record.LibraryName,
            record.Seats.Select(static seat => new SeatPayload(seat.SeatKey, seat.SeatName)).ToArray(),
            record.PollingMode,
            record.ReservationStrategy);
        return JsonSerializer.Serialize(payload, AppJson.Default);
    }

    public static string Serialize(GlobalLeakTaskLaunchRecord record)
    {
        var payload = new GlobalLeakPayload(
            CurrentVersion,
            record.Libraries.Select(static library => new LibraryPayload(
                library.LibraryId,
                library.LibraryName,
                library.Floor)).ToArray(),
            record.ScanInterval.Ticks);
        return JsonSerializer.Serialize(payload, AppJson.Default);
    }

    public static bool TryDeserializeGrab(
        string payloadJson,
        string recordId,
        DateTimeOffset recordedAtUtc,
        out GrabTaskLaunchRecord? record,
        out string error)
    {
        record = null;
        try
        {
            var payload = JsonSerializer.Deserialize<GrabPayload>(payloadJson, AppJson.Default);
            if (payload is null || payload.Version != CurrentVersion)
            {
                error = "记录版本不受支持";
                return false;
            }

            if (payload.LibraryId <= 0 || string.IsNullOrWhiteSpace(payload.LibraryName))
            {
                error = "场馆信息无效";
                return false;
            }

            if (!Enum.IsDefined(payload.PollingMode) || !Enum.IsDefined(payload.ReservationStrategy))
            {
                error = "抢座模式无效";
                return false;
            }

            if (payload.Seats is not { Count: > 0 } ||
                payload.Seats.Any(static seat => seat is null ||
                    string.IsNullOrWhiteSpace(seat.SeatKey) || string.IsNullOrWhiteSpace(seat.SeatName)) ||
                payload.Seats.Select(static seat => seat.SeatKey.Trim()).Distinct(StringComparer.Ordinal).Count() != payload.Seats.Count)
            {
                error = "座位列表无效";
                return false;
            }

            record = new GrabTaskLaunchRecord(
                recordId,
                recordedAtUtc,
                payload.LibraryId,
                payload.LibraryName.Trim(),
                payload.Seats.Select(static seat => new SeatReference(seat.SeatKey.Trim(), seat.SeatName.Trim())).ToArray(),
                payload.PollingMode,
                payload.ReservationStrategy);
            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryDeserializeGlobalLeak(
        string payloadJson,
        string recordId,
        DateTimeOffset recordedAtUtc,
        out GlobalLeakTaskLaunchRecord? record,
        out string error)
    {
        record = null;
        try
        {
            var payload = JsonSerializer.Deserialize<GlobalLeakPayload>(payloadJson, AppJson.Default);
            if (payload is null || payload.Version != CurrentVersion)
            {
                error = "记录版本不受支持";
                return false;
            }

            var interval = TimeSpan.FromTicks(payload.ScanIntervalTicks);
            if (interval < MinimumGlobalLeakInterval || interval > MaximumGlobalLeakInterval)
            {
                error = "扫描间隔无效";
                return false;
            }

            if (payload.Libraries is not { Count: > 0 } ||
                payload.Libraries.Any(static library => library is null ||
                    library.LibraryId <= 0 || string.IsNullOrWhiteSpace(library.LibraryName)) ||
                payload.Libraries.Select(static library => library.LibraryId).Distinct().Count() != payload.Libraries.Count)
            {
                error = "场馆列表无效";
                return false;
            }

            record = new GlobalLeakTaskLaunchRecord(
                recordId,
                recordedAtUtc,
                payload.Libraries.Select(static library => new GlobalLeakLibraryTarget(
                    library.LibraryId,
                    library.LibraryName.Trim(),
                    library.Floor?.Trim() ?? string.Empty)).ToArray(),
                interval);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentOutOfRangeException or OverflowException)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed record GrabPayload(
        int Version,
        int LibraryId,
        string LibraryName,
        IReadOnlyList<SeatPayload> Seats,
        GrabPollingMode PollingMode,
        GrabReservationStrategy ReservationStrategy);

    private sealed record SeatPayload(string SeatKey, string SeatName);

    private sealed record GlobalLeakPayload(
        int Version,
        IReadOnlyList<LibraryPayload> Libraries,
        long ScanIntervalTicks);

    private sealed record LibraryPayload(int LibraryId, string LibraryName, string? Floor);
}

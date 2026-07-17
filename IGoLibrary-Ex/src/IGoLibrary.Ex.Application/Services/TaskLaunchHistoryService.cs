using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class TaskLaunchHistoryService(
    ITaskLaunchHistoryRepository repository,
    TimeProvider timeProvider) : ITaskLaunchHistoryService
{
    public Task<IReadOnlyList<GrabTaskLaunchRecord>> GetRecentGrabAsync(
        CancellationToken cancellationToken = default)
    {
        return repository.GetRecentGrabAsync(cancellationToken);
    }

    public Task<IReadOnlyList<GlobalLeakTaskLaunchRecord>> GetRecentGlobalLeakAsync(
        CancellationToken cancellationToken = default)
    {
        return repository.GetRecentGlobalLeakAsync(cancellationToken);
    }

    public Task<GrabTaskLaunchRecord?> GetGrabAsync(
        string recordId,
        CancellationToken cancellationToken = default)
    {
        return repository.GetGrabAsync(recordId, cancellationToken);
    }

    public Task<GlobalLeakTaskLaunchRecord?> GetGlobalLeakAsync(
        string recordId,
        CancellationToken cancellationToken = default)
    {
        return repository.GetGlobalLeakAsync(recordId, cancellationToken);
    }

    public Task<TaskLaunchHistorySaveResult> RecordGrabAsync(
        GrabSeatPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Seats is not { Count: > 0 } ||
            plan.Seats.Any(static seat => seat is null))
        {
            throw new ArgumentException("抢座任务计划缺少可记录的座位", nameof(plan));
        }

        var seats = plan.Seats.Select(static seat => new SeatReference(
            seat.SeatKey?.Trim() ?? string.Empty,
            seat.SeatName?.Trim() ?? string.Empty)).ToArray();
        if (plan.LibraryId <= 0 ||
            string.IsNullOrWhiteSpace(plan.LibraryName) ||
            !Enum.IsDefined(plan.PollingMode) ||
            !Enum.IsDefined(plan.ReservationStrategy) ||
            seats.Any(static seat =>
                string.IsNullOrWhiteSpace(seat.SeatKey) || string.IsNullOrWhiteSpace(seat.SeatName)) ||
            seats.Select(static seat => seat.SeatKey).Distinct(StringComparer.Ordinal).Count() != seats.Length)
        {
            throw new ArgumentException("抢座任务计划缺少可记录的场馆或座位", nameof(plan));
        }

        var record = new GrabTaskLaunchRecord(
            Guid.NewGuid().ToString("N"),
            timeProvider.GetUtcNow().ToUniversalTime(),
            plan.LibraryId,
            plan.LibraryName.Trim(),
            seats,
            plan.PollingMode,
            plan.ReservationStrategy);
        return repository.SaveGrabAsync(record, CreateGrabFingerprint(record), cancellationToken);
    }

    public Task<TaskLaunchHistorySaveResult> RecordGlobalLeakAsync(
        GlobalLeakPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Libraries is not { Count: > 0 } ||
            plan.Libraries.Any(static library => library is null ||
                library.LibraryId <= 0 || string.IsNullOrWhiteSpace(library.LibraryName)) ||
            plan.Libraries.Select(static library => library.LibraryId).Distinct().Count() != plan.Libraries.Count)
        {
            throw new ArgumentException("全域捡漏任务计划缺少可记录的场馆", nameof(plan));
        }

        var record = new GlobalLeakTaskLaunchRecord(
            Guid.NewGuid().ToString("N"),
            timeProvider.GetUtcNow().ToUniversalTime(),
            plan.Libraries
                .Select(static library => new GlobalLeakLibraryTarget(
                    library.LibraryId,
                    library.LibraryName.Trim(),
                    library.Floor?.Trim() ?? string.Empty))
                .ToArray(),
            GlobalLeakStateMachine.NormalizeScanInterval(plan.ScanInterval));
        return repository.SaveGlobalLeakAsync(record, CreateGlobalLeakFingerprint(record), cancellationToken);
    }

    internal static string CreateGrabFingerprint(GrabTaskLaunchRecord record)
    {
        var builder = new StringBuilder();
        Append(builder, record.LibraryId.ToString(CultureInfo.InvariantCulture));
        Append(builder, ((int)record.PollingMode).ToString(CultureInfo.InvariantCulture));
        Append(builder, ((int)record.ReservationStrategy).ToString(CultureInfo.InvariantCulture));
        foreach (var seat in record.Seats)
        {
            Append(builder, seat.SeatKey);
        }

        return Hash(builder);
    }

    internal static string CreateGlobalLeakFingerprint(GlobalLeakTaskLaunchRecord record)
    {
        var builder = new StringBuilder();
        Append(builder, record.ScanInterval.Ticks.ToString(CultureInfo.InvariantCulture));
        foreach (var library in record.Libraries)
        {
            Append(builder, library.LibraryId.ToString(CultureInfo.InvariantCulture));
        }

        return Hash(builder);
    }

    private static void Append(StringBuilder builder, string value)
    {
        value ??= string.Empty;
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }

    private static string Hash(StringBuilder builder)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
